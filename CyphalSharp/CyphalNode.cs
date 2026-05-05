using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CyphalSharp
{
    /// <summary>
    /// Represents a Cyphal node with Plug-and-Play capabilities.
    /// Implements standard uavcan.node services: Heartbeat, GetInfo, ExecuteCommand.
    /// </summary>
    public class CyphalNode : IDisposable
    {
        private readonly ITransport _transport;
        private readonly Timer _heartbeatTimer;
        private DateTime _startTime;
        private bool _disposed;

        /// <summary>
        /// Gets the node ID.
        /// </summary>
        public ushort NodeId { get; }

        /// <summary>
        /// Gets or sets the node health status.
        /// </summary>
        public byte Health { get; set; } = 0; // HEALTH_NOMINAL

        /// <summary>
        /// Gets or sets the node mode.
        /// </summary>
        public byte Mode { get; set; } = 1; // MODE_INITIALIZING

        /// <summary>
        /// Gets or sets the vendor-specific status code.
        /// </summary>
        public byte VendorStatusCode { get; set; }

        /// <summary>
        /// Gets or sets the node name (max 50 bytes UTF-8).
        /// </summary>
        public string Name { get; set; } = "CyphalSharp.Node";

        /// <summary>
        /// Gets or sets the unique ID (16 bytes).
        /// </summary>
        public byte[] UniqueId { get; set; } = new byte[16];

        /// <summary>
        /// Gets or sets the protocol version.
        /// </summary>
        public (byte Major, byte Minor) ProtocolVersion { get; set; } = (1, 0);

        /// <summary>
        /// Gets or sets the hardware version.
        /// </summary>
        public (byte Major, byte Minor) HardwareVersion { get; set; } = (0, 0);

        /// <summary>
        /// Gets or sets the software version.
        /// </summary>
        public (byte Major, byte Minor) SoftwareVersion { get; set; } = (1, 0);

        /// <summary>
        /// Gets or sets the software VCS revision ID.
        /// </summary>
        public ulong? SoftwareVcsRevisionId { get; set; }

        /// <summary>
        /// Event raised when an ExecuteCommand request is received.
        /// Set the Result to indicate success (0) or failure (1-255).
        /// </summary>
        public event EventHandler<ExecuteCommandEventArgs> ExecuteCommandReceived;

        /// <summary>
        /// Initializes a new instance of the <see cref="CyphalNode"/> class.
        /// </summary>
        /// <param name="nodeId">The node ID (1-65534).</param>
        /// <param name="transport">The transport layer to use.</param>
        public CyphalNode(ushort nodeId, ITransport transport)
        {
            NodeId = nodeId;
            _transport = transport;
            _transport.FrameReceived += OnFrameReceived;
            _heartbeatTimer = new Timer(PublishHeartbeat, null, Timeout.Infinite, Timeout.Infinite);
            _startTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Starts the node: begins publishing Heartbeat and listening for services.
        /// </summary>
        public async Task StartAsync()
        {
            ThrowIfNotInitialized();
            await _transport.StartAsync();
            Mode = 2; // MODE_OPERATIONAL
            _heartbeatTimer.Change(0, 1000); // Publish heartbeat every 1 second
        }

        /// <summary>
        /// Stops the node and releases resources.
        /// </summary>
        public void Stop()
        {
            Mode = 3; // MODE_OFF
            _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
            PublishHeartbeat(null); // Send one final heartbeat
        }

        private void PublishHeartbeat(object state)
        {
            if (_disposed) return;

            try
            {
                var heartbeatMsg = Cyphal.RegisteredMessages.Values
                    .ToList().FirstOrDefault(m => m.Name == "uavcan.node.Heartbeat");
                if (heartbeatMsg == null) return;

                uint uptime = (uint)(DateTime.UtcNow - _startTime).TotalSeconds;

                IFrame frame = CreateFrame((ushort)heartbeatMsg.PortId, heartbeatMsg);
                frame.SetFields(new Dictionary<string, object>
                {
                    { "uptime", uptime },
                    { "health", Health },
                    { "mode", Mode },
                    { "vendor_specific_status_code", VendorStatusCode }
                });

                _transport.SendAsync(frame).GetAwaiter().GetResult();
            }
            catch { }
        }

        private void OnFrameReceived(object sender, IFrame frame)
        {
            if (frame.IsService && frame is IFrameWithDestination)
            {
                HandleServiceRequest(frame);
            }
        }

        private void HandleServiceRequest(IFrame frame)
        {
            if (frame.Message?.IsServiceDefinition != true) return;

            // GetInfo service ID = 1 (DataSpecifierId = 0x8001)
            if (frame.DataSpecifierId == 0x8001)
            {
                HandleGetInfo(frame);
            }
            // ExecuteCommand service ID = 2 (DataSpecifierId = 0x8002)
            else if (frame.DataSpecifierId == 0x8002)
            {
                HandleExecuteCommand(frame);
            }
        }

        private void HandleGetInfo(IFrame requestFrame)
        {
            var responseMsg = Cyphal.RegisteredMessages.Values
                .ToList().FirstOrDefault(m => m.Name == "uavcan.node.GetInfo" && m.IsServiceDefinition);
            if (responseMsg == null) return;

            var responseFrame = CreateFrame((ushort)requestFrame.DataSpecifierId, responseMsg, isResponse: true);
            if (responseFrame is IFrameWithDestination destFrame)
                destFrame.DestinationNodeId = requestFrame.SourceNodeId;

            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(Name);
            if (nameBytes.Length > 50) Array.Resize(ref nameBytes, 50);

            var fields = new Dictionary<string, object>
            {
                { "protocol_version.major", ProtocolVersion.Major },
                { "protocol_version.minor", ProtocolVersion.Minor },
                { "hardware_version.major", HardwareVersion.Major },
                { "hardware_version.minor", HardwareVersion.Minor },
                { "software_version.major", SoftwareVersion.Major },
                { "software_version.minor", SoftwareVersion.Minor },
                { "unique_id", UniqueId },
                { "name", nameBytes },
                { "software_vcs_revision_id", SoftwareVcsRevisionId.HasValue ? new ulong[] { SoftwareVcsRevisionId.Value } : Array.Empty<ulong>() }
            };

            responseFrame.SetFields(fields);
            _transport.SendAsync(responseFrame).GetAwaiter().GetResult();
        }

        private void HandleExecuteCommand(IFrame requestFrame)
        {
            var responseMsg = Cyphal.RegisteredMessages.Values
                .ToList().FirstOrDefault(m => m.Name == "uavcan.node.ExecuteCommand" && m.IsServiceDefinition);
            if (responseMsg == null) return;

            var args = new ExecuteCommandEventArgs
            {
                Command = requestFrame.Fields.TryGetValue("command", out var cmd) ? Convert.ToUInt16(cmd) : (ushort)0,
                Parameter = requestFrame.Fields.TryGetValue("parameter", out var param) ? (byte[])param : Array.Empty<byte>()
            };

            ExecuteCommandReceived?.Invoke(this, args);

            var responseFrame = CreateFrame((ushort)requestFrame.DataSpecifierId, responseMsg, isResponse: true);
            if (responseFrame is IFrameWithDestination destFrame)
                destFrame.DestinationNodeId = (ushort)requestFrame.SourceNodeId;
            responseFrame.SetFields(new Dictionary<string, object>
            {
                { "status", args.Result }
            });

            _transport.SendAsync(responseFrame).GetAwaiter().GetResult();
        }

        private IFrame CreateFrame(ushort portId, Message message, bool isResponse = false)
        {
            IFrame frame = _transport.Name switch
            {
                "UDP" => new UdpFrame(),
                "CAN" => new CanFrame(),
                _ => throw new NotSupportedException($"Transport {_transport.Name} not supported")
            };

            frame.SourceNodeId = NodeId;
            frame.DataSpecifierId = portId;
            frame.Message = message;
            frame.EndOfTransfer = true;

            return frame;
        }

        private void ThrowIfNotInitialized()
        {
            if (!Cyphal.RegisteredMessages.Any())
                throw new InvalidOperationException("Cyphal.Initialize() must be called before starting the node.");
        }

        /// <summary>
        /// Releases all resources used by the CyphalNode.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _heartbeatTimer?.Dispose();
            if (_transport != null)
                _transport.FrameReceived -= OnFrameReceived;
            _transport?.Dispose();
        }
    }

    /// <summary>
    /// Event arguments for ExecuteCommand requests.
    /// </summary>
    public class ExecuteCommandEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the command value from the request.
        /// </summary>
        public ushort Command { get; set; }

        /// <summary>
        /// Gets or sets the parameter bytes from the request.
        /// </summary>
        public byte[] Parameter { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Gets or sets the result to send in the response (0 = SUCCESS).
        /// </summary>
        public byte Result { get; set; } = 0; // SUCCESS
    }

    /// <summary>
    /// Interface for frames that support destination node ID.
    /// </summary>
    public interface IFrameWithDestination
    {
        /// <summary>
        /// Gets or sets the destination node ID.
        /// </summary>
        ushort DestinationNodeId { get; set; }
    }
}
