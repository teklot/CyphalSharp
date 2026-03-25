namespace CyphalSharp
{
    /// <summary>
    /// Contains constants that define the structure of Cyphal/CAN protocol frames.
    /// Reference: OpenCyphal/CAN Specification.
    /// </summary>
    public static class CanProtocol
    {
        /// <summary>
        /// Maximum payload size for CAN Classic (8 bytes including tail byte).
        /// </summary>
        public const int MaxPayloadSizeClassic = 8;

        /// <summary>
        /// Maximum payload size for CAN FD (64 bytes including tail byte).
        /// </summary>
        public const int MaxPayloadSizeFD = 64;

        /// <summary>
        /// Mask for the Priority bits (28-26) in the CAN ID.
        /// </summary>
        public const uint PriorityMask = 0x1C000000;
        /// <summary>
        /// Shift for the Priority bits in the CAN ID.
        /// </summary>
        public const int PriorityShift = 26;

        /// <summary>
        /// Mask for the Service bit (24) in the CAN ID.
        /// </summary>
        public const uint ServiceMask = 0x01000000;
        /// <summary>
        /// Shift for the Service bit in the CAN ID.
        /// </summary>
        public const int ServiceShift = 24;

        /// <summary>
        /// Mask for the Subject ID bits (23-8) in a Message CAN ID.
        /// </summary>
        public const uint MessageSubjectIdMask = 0x00FFFF00;
        /// <summary>
        /// Shift for the Subject ID bits in a Message CAN ID.
        /// </summary>
        public const int MessageSubjectIdShift = 8;

        /// <summary>
        /// Mask for the Service ID bits (23-15) in a Service CAN ID.
        /// </summary>
        public const uint ServiceIdMask = 0x00FF8000;
        /// <summary>
        /// Shift for the Service ID bits in a Service CAN ID.
        /// </summary>
        public const int ServiceIdShift = 15;

        /// <summary>
        /// Mask for the Request/Response bit (14) in a Service CAN ID.
        /// </summary>
        public const uint ServiceRequestResponseMask = 0x00004000;
        /// <summary>
        /// Shift for the Request/Response bit in a Service CAN ID.
        /// </summary>
        public const int ServiceRequestResponseShift = 14;

        /// <summary>
        /// Mask for the Destination Node ID bits (13-7) in a Service CAN ID.
        /// </summary>
        public const uint ServiceDestinationNodeIdMask = 0x00003F80;
        /// <summary>
        /// Shift for the Destination Node ID bits in a Service CAN ID.
        /// </summary>
        public const int ServiceDestinationNodeIdShift = 7;

        /// <summary>
        /// Mask for the Source Node ID bits (6-0) in the CAN ID.
        /// </summary>
        public const uint SourceNodeIdMask = 0x0000007F;
        /// <summary>
        /// Shift for the Source Node ID bits in the CAN ID.
        /// </summary>
        public const int SourceNodeIdShift = 0;

        /// <summary>
        /// Tail Byte: Start of Transfer (bit 7).
        /// </summary>
        public const byte TailStartOfTransferMask = 0x80;

        /// <summary>
        /// Tail Byte: End of Transfer (bit 6).
        /// </summary>
        public const byte TailEndOfTransferMask = 0x40;

        /// <summary>
        /// Tail Byte: Toggle bit (bit 5).
        /// </summary>
        public const byte TailToggleMask = 0x20;

        /// <summary>
        /// Tail Byte: Transfer ID bits (4-0).
        /// </summary>
        public const byte TailTransferIdMask = 0x1F;
    }
}
