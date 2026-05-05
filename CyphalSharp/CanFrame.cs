using CyphalSharp.Enums;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace CyphalSharp
{
    /// <summary>
    /// Represents a single, parsed Cyphal/CAN frame.
    /// This class contains the raw data from the CAN bus and the decoded payload fields.
    /// </summary>
    public class CanFrame : Frame, IFrameWithDestination
    {
        #region Properties
        /// <summary>
        /// The 29-bit CAN ID.
        /// </summary>
        public uint CanId { get; set; }

        /// <summary>
        /// True if this is the start of a transfer (from tail byte).
        /// </summary>
        public bool StartOfTransfer { get; set; }

        /// <summary>
        /// Toggle bit (from tail byte).
        /// </summary>
        public bool Toggle { get; set; }

        /// <inheritdoc />
        public override bool IsResponse
        {
            get
            {
                if (!IsService) return false;
                // Bit 13 of CAN ID is set for Responses in Cyphal/CAN
                return (CanId & CanProtocol.ServiceRequestResponseMask) != 0;
            }
        }
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="CanFrame"/> class.
        /// Uses the FD max payload size as the internal buffer limit.
        /// </summary>
        public CanFrame() : base(CanProtocol.MaxPayloadSizeFD)
        {
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            CanId = 0;
            DestinationNodeId = 0;
            StartOfTransfer = false;
            Toggle = false;
        }

        /// <inheritdoc />
        public override byte[] ToBytes()
        {
            if (Message == null) throw new InvalidOperationException("Message metadata must be set before serialization.");

            // CAN frame is: 4 bytes CAN ID + Payload (including tail byte)
            int totalLen = 4 + PayloadLength + 1;
            byte[] packet = new byte[totalLen];
            Span<byte> span = packet.AsSpan();

            // Encode CAN ID
            uint id = (uint)(Priority << CanProtocol.PriorityShift) & CanProtocol.PriorityMask;
            if (IsService)
            {
                id |= CanProtocol.ServiceMask;
                id |= ((uint)PortId << CanProtocol.ServiceIdShift) & CanProtocol.ServiceIdMask;
                if (IsResponse)
                {
                    id |= CanProtocol.ServiceRequestResponseMask;
                }
                id |= ((uint)DestinationNodeId << CanProtocol.ServiceDestinationNodeIdShift) & CanProtocol.ServiceDestinationNodeIdMask;
            }
            else
            {
                id |= ((uint)PortId << CanProtocol.MessageSubjectIdShift) & CanProtocol.MessageSubjectIdMask;
            }
            id |= ((uint)SourceNodeId << CanProtocol.SourceNodeIdShift) & CanProtocol.SourceNodeIdMask;
            
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), id);

            // Encode Payload
            Payload.AsSpan(0, PayloadLength).CopyTo(span.Slice(4));

            // Encode Tail Byte
            byte tail = (byte)(TransferId & CanProtocol.TailTransferIdMask);
            if (StartOfTransfer) tail |= CanProtocol.TailStartOfTransferMask;
            if (EndOfTransfer) tail |= CanProtocol.TailEndOfTransferMask;
            if (Toggle) tail |= CanProtocol.TailToggleMask;
            span[4 + PayloadLength] = tail;

            return packet;
        }

        /// <inheritdoc />
        public override bool TryParse(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out SequencePosition examined)
        {
            Cyphal.ThrowIfNotInitialized();
            consumed = sequence.Start;
            examined = sequence.End;

            // Minimum CAN frame is 4 bytes ID + 1 byte tail
            if (sequence.Length < 5) return false;

            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)sequence.Length);
            try
            {
                sequence.CopyTo(buffer);
                if (TryParse(buffer.AsSpan(0, (int)sequence.Length)))
                {
                    consumed = sequence.End;
                    examined = sequence.End;
                    return true;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return false;
        }

        /// <inheritdoc />
        public override bool TryParse(ReadOnlySpan<byte> packet)
        {
            Cyphal.ThrowIfNotInitialized();
            this.Reset();

            if (packet.Length < 5)
            {
                this.ErrorReason = ErrorReason.FrameTooShort;
                return false;
            }

            this.CanId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(0, 4));
            this.Priority = (byte)((this.CanId & CanProtocol.PriorityMask) >> CanProtocol.PriorityShift);
            bool isService = (this.CanId & CanProtocol.ServiceMask) != 0;
            this.SourceNodeId = (byte)(this.CanId & CanProtocol.SourceNodeIdMask);

            if (isService)
            {
                ushort serviceId = (ushort)((this.CanId & CanProtocol.ServiceIdMask) >> CanProtocol.ServiceIdShift);
                this.DataSpecifierId = (ushort)(serviceId | ServiceMask);
                this.DestinationNodeId = (byte)((this.CanId & CanProtocol.ServiceDestinationNodeIdMask) >> CanProtocol.ServiceDestinationNodeIdShift);
            }
            else
            {
                this.DataSpecifierId = (ushort)((this.CanId & CanProtocol.MessageSubjectIdMask) >> CanProtocol.MessageSubjectIdShift);
            }

            // Tail byte is the last byte of the packet
            byte tail = packet[packet.Length - 1];
            this.StartOfTransfer = (tail & CanProtocol.TailStartOfTransferMask) != 0;
            this.EndOfTransfer = (tail & CanProtocol.TailEndOfTransferMask) != 0;
            this.Toggle = (tail & CanProtocol.TailToggleMask) != 0;
            this.TransferId = (ulong)(tail & CanProtocol.TailTransferIdMask);

            if (!Cyphal.RegisteredMessages.TryGetValue(this.DataSpecifierId, out var message))
            {
                this.ErrorReason = ErrorReason.MessageNotFound;
                return false;
            }

            if (!message.IsIncluded)
            {
                this.ErrorReason = ErrorReason.MessageExcluded;
                return false;
            }

            this.Message = message;

            // Payload is between CAN ID and Tail Byte
            this.PayloadLength = packet.Length - 4 - 1;
            
            int targetLength = message.PayloadLength;
            if (message.IsServiceDefinition && IsResponse)
            {
                targetLength = message.ResponsePayloadLength;
            }

            // Truncation/Extension is handled by the transport/multi-frame logic, 
            // but for a single frame we copy what we have.
            // Note: CAN usually requires multi-frame reassembly if message is larger than 7/63 bytes.
            
            int bytesToCopy = Math.Min(this.PayloadLength, targetLength);
            if (bytesToCopy > 0)
            {
                packet.Slice(4, bytesToCopy).CopyTo(this.Payload);
            }

            return true;
        }
    }
}
