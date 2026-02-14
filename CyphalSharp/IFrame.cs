using System;
using System.Buffers;
using System.Collections.Generic;
using CyphalSharp.Enums;

namespace CyphalSharp
{
    /// <summary>
    /// Represents a generic Cyphal frame.
    /// </summary>
    public interface IFrame
    {
        /// <summary>
        /// The priority of the frame (0-7, lower is higher priority).
        /// </summary>
        byte Priority { get; set; }

        /// <summary>
        /// The source node ID.
        /// </summary>
        ushort SourceNodeId { get; set; }

        /// <summary>
        /// The data specifier ID (Subject ID or Service ID).
        /// </summary>
        ushort DataSpecifierId { get; set; }

        /// <summary>
        /// True if the transfer is a Service (Request/Response), false if it is a Message (Subject).
        /// This is determined by the MSB of the DataSpecifierId.
        /// </summary>
        bool IsService { get; }

        /// <summary>
        /// The Port ID (Subject ID or Service ID) extracted from the <see cref="DataSpecifierId"/> by masking out the Service bit.
        /// This represents the raw Port ID parsed from the wire header.
        /// </summary>
        ushort PortId { get; }

        /// <summary>
        /// Monotonic transfer sequence number.
        /// </summary>
        ulong TransferId { get; set; }

        /// <summary>
        /// True if this is the last frame in the transfer.
        /// </summary>
        bool EndOfTransfer { get; set; }

        /// <summary>
        /// The message metadata associated with this frame.
        /// </summary>
        Message Message { get; set; }

        /// <summary>
        /// The raw message payload buffer.
        /// </summary>
        byte[] Payload { get; }
        
        /// <summary>
        /// The actual length of the payload in bytes.
        /// </summary>
        int PayloadLength { get; }

        /// <summary>
        /// The UTC timestamp when the frame was created or received.
        /// </summary>
        DateTime Timestamp { get; }

        /// <summary>
        /// A dictionary holding the decoded payload fields as key-value pairs.
        /// </summary>
        Dictionary<string, object> Fields { get; }

        /// <summary>
        /// If an error occurred during parsing, this property specifies the reason.
        /// </summary>
        ErrorReason ErrorReason { get; }

        /// <summary>
        /// Resets the frame state for reuse.
        /// </summary>
        void Reset();

        /// <summary>
        /// Populates the frame's payload fields with the provided values.
        /// </summary>
        void SetFields(IDictionary<string, object> values);

        /// <summary>
        /// Serializes the current frame into a raw byte array.
        /// </summary>
        byte[] ToBytes();

        /// <summary>
        /// Attempts to parse a Cyphal frame from a raw byte span.
        /// </summary>
        bool TryParse(ReadOnlySpan<byte> packet);

        /// <summary>
        /// Attempts to parse a Cyphal frame from a <see cref="ReadOnlySequence{Byte}"/>.
        /// </summary>
        bool TryParse(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out SequencePosition examined);
    }
}
