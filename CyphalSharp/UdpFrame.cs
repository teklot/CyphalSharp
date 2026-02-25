using CyphalSharp.Enums;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace CyphalSharp
{
    /// <summary>
    /// Represents a single, parsed Cyphal/UDP frame.
    /// This class contains the raw data from the wire and the decoded payload fields.
    /// </summary>
    public class UdpFrame : Frame
    {
        #region Properties
        /// <summary>
        /// Protocol version (0).
        /// </summary>
        public byte Version { get; set; } = 0;

        /// <summary>
        /// Destination Node ID (16-bit).
        /// </summary>
        public ushort DestinationNodeId { get; set; }

        /// <summary>
        /// Frame index within the transfer (31 bits).
        /// </summary>
        public uint FrameIndex { get; set; }

        /// <summary>
        /// Opaque user data (16-bit).
        /// </summary>
        public ushort UserData { get; set; }

        /// <summary>
        /// For Cyphal/UDP, a Service Response is identified if the destination node ID is not the broadcast address (0xFFFF) 
        /// and the service bit is set.
        /// </summary>
        public override bool IsResponse => IsService && DestinationNodeId != 0xFFFF && DestinationNodeId != 0; // Simplified
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpFrame"/> class and sets the creation timestamp.
        /// </summary>
        public UdpFrame() : base(UdpProtocol.MaxPayloadSize)
        {
        }

        /// <inheritdoc />
        public override void Reset()
        {
            base.Reset();
            Version = 0;
            DestinationNodeId = 0;
            FrameIndex = 0;
            UserData = 0;
        }

        /// <inheritdoc />
        public override byte[] ToBytes()
        {
            if (Message == null) throw new InvalidOperationException("Message metadata must be set before serialization.");

            int totalLen = UdpProtocol.HeaderLength + PayloadLength;
            byte[] packet = new byte[totalLen];
            Span<byte> span = packet.AsSpan();

            // Header
            span[0] = Version;
            span[1] = Priority;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), SourceNodeId);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), DestinationNodeId);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), DataSpecifierId);
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8), TransferId);
            
            uint indexAndEot = FrameIndex;
            if (EndOfTransfer) indexAndEot |= UdpProtocol.EndOfTransferMask;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), indexAndEot);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20), UserData);

            // Payload
            Payload.AsSpan(0, PayloadLength).CopyTo(span.Slice(UdpProtocol.HeaderLength));

            // Header CRC
            ushort headerCrc = Crc.Calculate(span.Slice(0, UdpProtocol.HeaderLength - 2));
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22), headerCrc);

            return packet;
        }

        /// <inheritdoc />
        public override bool TryParse(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out SequencePosition examined)
        {
            Cyphal.ThrowIfNotInitialized();
            consumed = sequence.Start;
            examined = sequence.End;

            if (sequence.Length < UdpProtocol.HeaderLength) return false;

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

            if (packet.Length < UdpProtocol.HeaderLength)
            {
                this.ErrorReason = ErrorReason.FrameTooShort;
                return false;
            }

            ushort expectedCrc = Crc.Calculate(packet.Slice(0, UdpProtocol.HeaderLength - 2));
            ushort actualCrc = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(22, 2));

            if (expectedCrc != actualCrc)
            {
                this.ErrorReason = ErrorReason.HeaderCrcMismatch;
                return false;
            }

            this.Version = packet[0];
            if (this.Version != 0)
            {
                this.ErrorReason = ErrorReason.UnsupportedVersion;
                return false;
            }

            this.Priority = packet[1];
            this.SourceNodeId = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(2, 2));
            this.DestinationNodeId = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(4, 2));
            this.DataSpecifierId = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(6, 2));
            this.TransferId = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(8, 8));
            
            uint indexAndEot = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(16, 4));
            this.EndOfTransfer = (indexAndEot & UdpProtocol.EndOfTransferMask) != 0;
            this.FrameIndex = indexAndEot & ~UdpProtocol.EndOfTransferMask;
            this.UserData = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(20, 2));

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

            // Payload handling with Truncation/Extension Rules
            this.PayloadLength = packet.Length - UdpProtocol.HeaderLength;
            
            int targetLength = message.PayloadLength;
            if (message.IsServiceDefinition && IsResponse)
            {
                targetLength = message.ResponsePayloadLength;
            }

            // Validate payload length - flag severely malformed payloads
            if (targetLength > 0 && (this.PayloadLength < targetLength / 2 || this.PayloadLength > targetLength * 2))
            {
                this.ErrorReason = ErrorReason.PayloadLengthInvalid;
            }

            int bytesToCopy = Math.Min(this.PayloadLength, targetLength);
            
            if (bytesToCopy > 0)
            {
                packet.Slice(UdpProtocol.HeaderLength, bytesToCopy).CopyTo(this.Payload);
            }

            if (bytesToCopy < targetLength)
            {
                this.Payload.AsSpan(bytesToCopy, targetLength - bytesToCopy).Clear();
            }

            return true;
        }
    }
}
