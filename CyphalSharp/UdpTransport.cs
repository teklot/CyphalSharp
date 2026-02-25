using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CyphalSharp
{
    /// <summary>
    /// Implementation of the Cyphal/UDP transport with multicast support and reassembly.
    /// </summary>
    public class UdpTransport : ITransport
    {
        private UdpClient _udpClient;
        private CancellationTokenSource _cts;
        private readonly ushort _localNodeId;
        private readonly HashSet<ushort> _joinedSubjects = new HashSet<ushort>();
        private readonly ConcurrentDictionary<string, TransferContext> _reassemblyBuffers = new ConcurrentDictionary<string, TransferContext>();
        private readonly TimeSpan _reassemblyTimeout = TimeSpan.FromSeconds(2);
        private readonly Timer _cleanupTimer;

        /// <inheritdoc />
        public string Name => "UDP";

        /// <inheritdoc />
        public int HeaderLength => UdpProtocol.HeaderLength;

        /// <inheritdoc />
        public int MaxPayloadSize => UdpProtocol.MaxPayloadSize;

        /// <inheritdoc />
        public event EventHandler<IFrame> FrameReceived;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpTransport"/> class.
        /// </summary>
        /// <param name="localNodeId">The ID of the local node.</param>
        /// <param name="reassemblyTimeoutMs">Timeout in milliseconds for multi-frame transfer reassembly (default: 2000ms).</param>
        public UdpTransport(ushort localNodeId, int reassemblyTimeoutMs = 2000)
        {
            _localNodeId = localNodeId;
            _reassemblyTimeout = TimeSpan.FromMilliseconds(reassemblyTimeoutMs);
            _cleanupTimer = new Timer(CleanupStaleTransfers, null, _reassemblyTimeout, _reassemblyTimeout);
        }

        /// <inheritdoc />
        public Task StartAsync()
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, UdpProtocol.CyphalUdpPort));

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(_cts.Token));
            
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task SendAsync(IFrame frame)
        {
            if (frame is not UdpFrame udpFrame) throw new ArgumentException("Frame must be a UdpFrame");

            byte[] data = udpFrame.ToBytes();
            IPAddress targetAddr = GetMulticastAddress(udpFrame.DataSpecifierId);
            await _udpClient.SendAsync(data, data.Length, new IPEndPoint(targetAddr, UdpProtocol.CyphalUdpPort));
        }

        /// <summary>
        /// Subscribes to a Cyphal subject and joins its corresponding multicast group.
        /// </summary>
        /// <param name="subjectId">The subject ID to subscribe to.</param>
        public void SubscribeToSubject(ushort subjectId)
        {
            if (_joinedSubjects.Add(subjectId))
            {
                var addr = GetMulticastAddress(subjectId);
                _udpClient.JoinMulticastGroup(addr);
            }
        }

        private IPAddress GetMulticastAddress(ushort dataSpecifierId)
        {
            // Cyphal/UDP: 239.0.X.Y where X.Y is the DataSpecifierId
            byte x = (byte)((dataSpecifierId >> 8) & 0xFF);
            byte y = (byte)(dataSpecifierId & 0xFF);
            return IPAddress.Parse($"239.0.{x}.{y}");
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    var frame = new UdpFrame();
                    if (frame.TryParse(result.Buffer))
                    {
                        HandleIncomingFrame(frame);
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { /* Log error */ }
            }
        }

        private void HandleIncomingFrame(UdpFrame frame)
        {
            if (frame.EndOfTransfer && frame.FrameIndex == 0)
            {
                FrameReceived?.Invoke(this, frame);
                return;
            }

            string key = $"{frame.SourceNodeId}_{frame.DataSpecifierId}_{frame.TransferId}";
            var context = _reassemblyBuffers.GetOrAdd(key, _ => new TransferContext(key));

            lock (context)
            {
                context.LastUpdate = DateTime.UtcNow;
                context.Frames.Add(frame);
                
                var lastFrame = context.Frames.FirstOrDefault(f => f.EndOfTransfer);
                if (lastFrame != null && context.Frames.Count == (lastFrame.FrameIndex + 1))
                {
                    bool allPresent = true;
                    for (int i = 0; i <= lastFrame.FrameIndex; i++)
                    {
                        if (!context.Frames.Any(f => f.FrameIndex == i))
                        {
                            allPresent = false;
                            break;
                        }
                    }

                    if (allPresent)
                    {
                        var completeFrame = Reassemble(context.Frames);
                        _reassemblyBuffers.TryRemove(key, out _);
                        if (completeFrame != null)
                        {
                            FrameReceived?.Invoke(this, completeFrame);
                        }
                    }
                }
            }
        }

        private void CleanupStaleTransfers(object state)
        {
            var now = DateTime.UtcNow;
            var staleKeys = _reassemblyBuffers
                .Where(kvp => (now - kvp.Value.LastUpdate) > _reassemblyTimeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _reassemblyBuffers.TryRemove(key, out _);
            }
        }

        private UdpFrame Reassemble(List<UdpFrame> frames)
        {
            // Simple reassembly logic for MVP
            frames.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
            
            // Check for gaps
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].FrameIndex != i) return null;
            }

            var first = frames[0];
            var result = new UdpFrame
            {
                SourceNodeId = first.SourceNodeId,
                DestinationNodeId = first.DestinationNodeId,
                DataSpecifierId = first.DataSpecifierId,
                TransferId = first.TransferId,
                Message = first.Message,
                EndOfTransfer = true
            };

            int totalPayload = 0;
            foreach (var f in frames) totalPayload += f.PayloadLength;

            byte[] fullPayload = new byte[totalPayload];
            int offset = 0;
            foreach (var f in frames)
            {
                Array.Copy(f.Payload, 0, fullPayload, offset, f.PayloadLength);
                offset += f.PayloadLength;
            }

            // Copy to the new frame's internal payload buffer
            result.SetPayload(fullPayload);
            
            return result;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts?.Cancel();
            _cleanupTimer?.Dispose();
            _udpClient?.Dispose();
        }

        private class TransferContext
        {
            public string Key { get; }
            public List<UdpFrame> Frames { get; }
            public DateTime LastUpdate { get; set; }

            public TransferContext(string key)
            {
                Key = key;
                Frames = new List<UdpFrame>();
                LastUpdate = DateTime.UtcNow;
            }
        }
    }
}
