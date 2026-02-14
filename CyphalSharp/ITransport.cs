using System;
using System.Threading.Tasks;

namespace CyphalSharp
{
    /// <summary>
    /// Defines the interface for a Cyphal transport layer (e.g., UDP, CAN, Serial).
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// The name of the transport.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The fixed length of the transport header in bytes.
        /// </summary>
        int HeaderLength { get; }

        /// <summary>
        /// The maximum payload size for a single frame in this transport.
        /// </summary>
        int MaxPayloadSize { get; }

        /// <summary>
        /// Starts the transport to listen for incoming frames.
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Sends a frame over the transport.
        /// </summary>
        Task SendAsync(IFrame frame);

        /// <summary>
        /// Event raised when a complete frame (or reassembled transfer) is received.
        /// </summary>
        event EventHandler<IFrame> FrameReceived;
    }
}
