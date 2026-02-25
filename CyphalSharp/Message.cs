using System.Collections.Generic;

namespace CyphalSharp
{
    /// <summary>
    /// Represents a message or service definition from a Cyphal DSDL.
    /// </summary>
    public class Message
    {
        /// <summary>
        /// The fixed Port ID (Subject ID or Service ID) defined in the DSDL for this message type.
        /// This is used as the registry key to match incoming <see cref="IFrame.PortId"/> to its definition.
        /// </summary>
        public uint PortId { get; set; }

        /// <summary>
        /// Human readable form for the message. It is used for naming helper functions in generated libraries, but is not sent over the wire.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Human readable description of message, shown in user interfaces and in code comments.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// True if this definition represents a Service (has Request/Response parts).
        /// </summary>
        public bool IsServiceDefinition { get; set; }

        /// <summary>
        /// True if this definition represents a Union (only one field active at a time).
        /// </summary>
        public bool IsUnion { get; set; }

        /// <summary>
        /// The index of the tag field within the Fields list (for unions).
        /// </summary>
        public int UnionTagFieldIndex { get; set; }

        /// <summary>
        /// Fields for the Message (if not a service) or the Request part (if a service).
        /// </summary>
        public List<Field> Fields { get; set; } = new List<Field>();

        /// <summary>
        /// Fields for the Response part (only if <see cref="IsServiceDefinition"/> is true).
        /// </summary>
        public List<Field> ResponseFields { get; set; } = new List<Field>();

        #region Helpers
        /// <summary>
        /// Expected payload length in bytes for the Message or Request part.
        /// </summary>
        public int PayloadLength { get; set; }

        /// <summary>
        /// Expected payload length in bytes for the Response part.
        /// </summary>
        public int ResponsePayloadLength { get; set; }

        /// <summary>
        /// Whether the message to be parsed.
        /// </summary>
        public bool IsIncluded { get; private set; }

        /// <summary>
        /// Include the message for parsing.
        /// </summary>
        public void Include() => IsIncluded = true;

        /// <summary>
        /// Exclude the message from parsing.
        /// </summary>
        public void Exclude() => IsIncluded = false;

        /// <summary>
        /// Gets the active union field based on the tag value read from payload.
        /// Returns null if not a union or tag value is out of range.
        /// </summary>
        /// <param name="tagValue">The tag value from the payload.</param>
        /// <returns>The active field or null.</returns>
        public Field GetActiveUnionField(int tagValue)
        {
            if (!IsUnion || Fields.Count <= 1) return null;
            
            foreach (var field in Fields)
            {
                if (field.IsUnionVariant && field.UnionTagValue == tagValue)
                    return field;
            }
            return null;
        }
        #endregion
    }
}
