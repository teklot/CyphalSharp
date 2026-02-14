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
        private readonly ConcurrentDictionary<string, List<UdpFrame>> _reassemblyBuffers = new ConcurrentDictionary<string, List<UdpFrame>>();

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
        public UdpTransport(ushort localNodeId)
        {
            _localNodeId = localNodeId;
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
                // Single frame transfer
                FrameReceived?.Invoke(this, frame);
                return;
            }

            // Multi-frame reassembly
            string key = $"{frame.SourceNodeId}_{frame.DataSpecifierId}_{frame.TransferId}";
            var buffer = _reassemblyBuffers.GetOrAdd(key, _ => new List<UdpFrame>());
            
            lock (buffer)
            {
                buffer.Add(frame);
                
                // Check if we have the last frame and all intermediate frames
                var lastFrame = buffer.FirstOrDefault(f => f.EndOfTransfer);
                if (lastFrame != null)
                {
                    // The FrameIndex of the last frame + 1 should be the total number of frames
                    if (buffer.Count == (lastFrame.FrameIndex + 1))
                    {
                        var completeFrame = Reassemble(buffer);
                        _reassemblyBuffers.TryRemove(key, out _);
                        if (completeFrame != null)
                        {
                            FrameReceived?.Invoke(this, completeFrame);
                        }
                    }
                }
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
            _udpClient?.Dispose();
        }
    }
}
