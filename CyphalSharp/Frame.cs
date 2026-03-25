using CyphalSharp.Enums;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace CyphalSharp
{
    /// <summary>
    /// Abstract base class for Cyphal frames, providing common metadata and payload handling.
    /// </summary>
    public abstract class Frame : IFrame
    {
        /// <summary>
        /// Bit 15 of DataSpecifierId indicates a Service transfer if set.
        /// This is standard across most Cyphal transports.
        /// </summary>
        protected const ushort ServiceMask = 0x8000;

        #region Properties
        /// <inheritdoc />
        public byte Priority { get; set; }

        /// <inheritdoc />
        public ushort SourceNodeId { get; set; }

        /// <inheritdoc />
        public ushort DataSpecifierId { get; set; }

        /// <inheritdoc />
        public bool IsService => (DataSpecifierId & ServiceMask) != 0;

        /// <inheritdoc />
        public ushort PortId => (ushort)(DataSpecifierId & ~ServiceMask);

        /// <inheritdoc />
        public ulong TransferId { get; set; }

        /// <inheritdoc />
        public bool EndOfTransfer { get; set; }

        /// <inheritdoc />
        public Message Message { get; set; }

        /// <inheritdoc />
        public byte[] Payload { get; }
        
        /// <inheritdoc />
        public int PayloadLength { get; protected set; }

        /// <inheritdoc />
        public DateTime Timestamp { get; protected set; }

        /// <summary>
        /// Manually set the payload and its length. Used during reassembly.
        /// </summary>
        public void SetPayload(ReadOnlySpan<byte> data)
        {
            data.CopyTo(Payload);
            PayloadLength = data.Length;
            _fields = null; // Invalidate field cache
        }

        private Dictionary<string, object> _fields;
        /// <inheritdoc />
        public Dictionary<string, object> Fields
        {
            get
            {
                if (_fields == null)
                {
                    _fields = new Dictionary<string, object>();
                    if (Message != null)
                    {
                        var fields = Message.Fields;
                        var length = Message.PayloadLength;

                        // If it's a service response, use ResponseFields instead.
                        // In Cyphal/UDP, Responses usually have specific header/port bits, 
                        // but logic depends on transport. The abstract class handles the decoding logic.
                        if (Message.IsServiceDefinition && IsResponse)
                        {
                            fields = Message.ResponseFields;
                            length = Message.ResponsePayloadLength;
                        }

                        ReadOnlySpan<byte> span = Payload.AsSpan(0, length);
                        foreach (var @field in fields)
                        {
                            if (@field.BitOffset + @field.BitLength <= span.Length * 8)
                            {
                                _fields[@field.Name] = @field.GetValue(span);
                            }
                        }
                    }
                }
                return _fields;
            }
        }

        /// <summary>
        /// True if this frame represents a Response in a Service transfer.
        /// Transport-specific implementations must override this if they support services.
        /// </summary>
        public virtual bool IsResponse => false;

        /// <inheritdoc />
        public ErrorReason ErrorReason { get; protected set; }
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="Frame"/> class with a specified max payload size.
        /// </summary>
        /// <param name="maxPayloadSize">The maximum size of the payload buffer.</param>
        protected Frame(int maxPayloadSize)
        {
            Payload = new byte[maxPayloadSize];
            Timestamp = DateTime.UtcNow;
        }

        /// <inheritdoc />
        public virtual void Reset()
        {
            Priority = 0;
            SourceNodeId = 0;
            DataSpecifierId = 0;
            TransferId = 0;
            EndOfTransfer = false;
            Message = null;
            PayloadLength = 0;
            Timestamp = DateTime.UtcNow;
            _fields = null;
            ErrorReason = ErrorReason.None;
        }

        /// <inheritdoc />
        public void SetFields(IDictionary<string, object> values)
        {
            if (Message == null) throw new InvalidOperationException("Message metadata must be set before setting fields.");

            var fields = Message.Fields;
            var targetPayloadLength = Message.PayloadLength;

            if (Message.IsServiceDefinition && IsResponse)
            {
                fields = Message.ResponseFields;
                targetPayloadLength = Message.ResponsePayloadLength;
            }

            foreach (var field in fields)
            {
                if (values.TryGetValue(field.Name, out var value))
                {
                    field.SetValue(Payload.AsSpan(), value);
                }
            }

            PayloadLength = targetPayloadLength;
        }

        /// <inheritdoc />
        public abstract byte[] ToBytes();

        /// <inheritdoc />
        public abstract bool TryParse(ReadOnlySpan<byte> packet);

        /// <inheritdoc />
        public abstract bool TryParse(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out SequencePosition examined);
    }
}
