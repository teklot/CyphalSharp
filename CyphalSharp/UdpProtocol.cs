namespace CyphalSharp
{
    /// <summary>
    /// Contains constants that define the structure of Cyphal/UDP protocol frames.
    /// Reference: OpenCyphal/UDP Specification.
    /// </summary>
    public static class UdpProtocol
    {
        /// <summary>
        /// The fixed length of the Cyphal/UDP header in bytes.
        /// </summary>
        public const int HeaderLength = 24;

        /// <summary>
        /// The maximum payload size for a Cyphal/UDP frame (based on MTU).
        /// </summary>
        public const int MaxPayloadSize = 1400;

        /// <summary>
        /// The offset for the protocol version field.
        /// </summary>
        public const int OffsetVersion = 0;
        /// <summary>
        /// The offset for the frame priority field.
        /// </summary>
        public const int OffsetPriority = 1;
        /// <summary>
        /// The offset for the source node ID field.
        /// </summary>
        public const int OffsetSourceNodeId = 2;
        /// <summary>
        /// The offset for the destination node ID field.
        /// </summary>
        public const int OffsetDestinationNodeId = 4;
        /// <summary>
        /// The offset for the data specifier ID field.
        /// </summary>
        public const int OffsetDataSpecifierId = 6;
        /// <summary>
        /// The offset for the transfer ID field.
        /// </summary>
        public const int OffsetTransferId = 8;
        /// <summary>
        /// The offset for the frame index field.
        /// </summary>
        public const int OffsetFrameIndex = 16;
        /// <summary>
        /// The offset for the user data field.
        /// </summary>
        public const int OffsetUserData = 20;
        /// <summary>
        /// The offset for the header CRC field.
        /// </summary>
        public const int OffsetHeaderCrc = 22;

        /// <summary>
        /// The current Cyphal/UDP protocol version (0).
        /// </summary>
        public const byte Version = 0;

        /// <summary>
        /// Mask for the End of Transfer bit in the Frame Index field (MSB).
        /// </summary>
        public const uint EndOfTransferMask = 0x80000000;

        /// <summary>
        /// Standard Cyphal/UDP port number.
        /// </summary>
        public const int CyphalUdpPort = 9382;

        /// <summary>
        /// Base multicast IP for Cyphal/UDP (239.0.0.0).
        /// </summary>
        public const string MulticastGroupBase = "239.0.0.0";
    }
}
