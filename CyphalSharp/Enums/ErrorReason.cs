namespace CyphalSharp.Enums
{
    /// <summary>
    /// Specifies the possible reasons why a Cyphal frame parsing operation might fail.
    /// </summary>
    public enum ErrorReason
    {
        /// <summary>
        /// No error occurred. Parsing was successful.
        /// </summary>
        None = 0,
        /// <summary>
        /// The header CRC does not match the calculated CRC for the frame header.
        /// </summary>
        HeaderCrcMismatch,
        /// <summary>
        /// The frame is shorter than the minimum expected length (24 bytes).
        /// </summary>
        FrameTooShort,
        /// <summary>
        /// The protocol version in the header is not supported (currently version 0).
        /// </summary>
        UnsupportedVersion,
        /// <summary>
        /// The Data Specifier ID (Subject ID) found in the frame does not correspond to any known message in the loaded DSDL definitions.
        /// </summary>
        MessageNotFound,
        /// <summary>
        /// The message was found in the DSDL definitions, but it has been explicitly excluded from parsing.
        /// </summary>
        MessageExcluded,
        /// <summary>
        /// The payload length specified in the frame header is invalid or inconsistent with the message definition.
        /// </summary>
        PayloadLengthInvalid,
        /// <summary>
        /// The transfer sequence number is out of order or unexpected (for streaming/reassembly).
        /// </summary>
        TransferIdMismatch,
    }
}
