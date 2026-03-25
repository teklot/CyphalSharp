using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace CyphalSharp
{
    /// <summary>
    /// Implementation of the Cyphal/CAN transport with multi-frame reassembly.
    /// This transport doesn't implement a specific CAN driver but provides the logic 
    /// for parsing and reassembling Cyphal frames from raw CAN frames.
    /// </summary>
    public class CanTransport : ITransport
    {
        private readonly ConcurrentDictionary<string, CanTransferContext> _reassemblyBuffers = new ConcurrentDictionary<string, CanTransferContext>();
        private readonly TimeSpan _reassemblyTimeout = TimeSpan.FromSeconds(2);
        private readonly Timer _cleanupTimer;

        /// <inheritdoc />
        public string Name => "CAN";

        /// <inheritdoc />
        public int HeaderLength => 4; // 29-bit CAN ID (represented as 4 bytes)

        /// <inheritdoc />
        public int MaxPayloadSize => CanProtocol.MaxPayloadSizeFD - 1; // Excluding tail byte

        /// <inheritdoc />
        public event EventHandler<IFrame> FrameReceived;

        /// <summary>
        /// Event raised when a raw CAN frame needs to be sent. 
        /// The user should hook this up to their CAN driver.
        /// </summary>
        public event EventHandler<CanRawFrame> RawFrameSent;

        /// <summary>
        /// Initializes a new instance of the <see cref="CanTransport"/> class.
        /// </summary>
        /// <param name="reassemblyTimeoutMs">Timeout in milliseconds for multi-frame transfer reassembly (default: 2000ms).</param>
        public CanTransport(int reassemblyTimeoutMs = 2000)
        {
            _reassemblyTimeout = TimeSpan.FromMilliseconds(reassemblyTimeoutMs);
            _cleanupTimer = new Timer(CleanupStaleTransfers, null, _reassemblyTimeout, _reassemblyTimeout);
        }

        /// <inheritdoc />
        public Task StartAsync()
        {
            // Nothing to start as it relies on external ProcessRawFrame calls.
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SendAsync(IFrame frame)
        {
            if (frame is not CanFrame canFrame) throw new ArgumentException("Frame must be a CanFrame");

            // For simplicity, this currently only supports single-frame transfers.
            // Multi-frame fragmentation should be implemented if needed.
            if (canFrame.PayloadLength > (canFrame.Payload.Length - 1))
            {
                 throwNotSupported("Multi-frame fragmentation for sending is not yet implemented.");
            }

            canFrame.StartOfTransfer = true;
            canFrame.EndOfTransfer = true;
            canFrame.Toggle = true; // Single-frame transfer toggle is 1

            byte[] bytes = canFrame.ToBytes();
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
            byte[] payload = new byte[bytes.Length - 4];
            Array.Copy(bytes, 4, payload, 0, payload.Length);

            RawFrameSent?.Invoke(this, new CanRawFrame { CanId = id, Payload = payload });
            
            return Task.CompletedTask;
        }

        private void throwNotSupported(string message) => throw new NotSupportedException(message);

        /// <summary>
        /// Processes a raw CAN frame received from the CAN bus.
        /// </summary>
        /// <param name="canId">The 29-bit CAN ID.</param>
        /// <param name="payload">The CAN frame payload (including tail byte).</param>
        public void ProcessRawFrame(uint canId, byte[] payload)
        {
            var frame = new CanFrame();
            byte[] packet = new byte[4 + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, canId);
            payload.CopyTo(packet, 4);
            
            if (frame.TryParse(packet))
            {
                HandleIncomingFrame(frame);
            }
        }

        private void HandleIncomingFrame(CanFrame frame)
        {
            if (frame.StartOfTransfer && frame.EndOfTransfer)
            {
                // Single-frame transfer
                FrameReceived?.Invoke(this, frame);
                return;
            }

            // Multi-frame reassembly
            string key = $"{frame.SourceNodeId}_{frame.DataSpecifierId}_{frame.TransferId}";
            var context = _reassemblyBuffers.GetOrAdd(key, _ => new CanTransferContext(key));

            lock (context)
            {
                // Basic validation: 
                // If SOT, reset context.
                if (frame.StartOfTransfer)
                {
                    context.Frames.Clear();
                    context.ExpectedToggle = false;
                }
                else if (context.Frames.Count == 0)
                {
                    // Ignore frame if we haven't seen the start
                    return;
                }

                // Check toggle bit
                if (frame.Toggle != context.ExpectedToggle)
                {
                    _reassemblyBuffers.TryRemove(key, out _);
                    return;
                }

                context.LastUpdate = DateTime.UtcNow;
                context.Frames.Add(frame);
                context.ExpectedToggle = !context.ExpectedToggle;

                if (frame.EndOfTransfer)
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

        private CanFrame Reassemble(List<CanFrame> frames)
        {
            var first = frames[0];
            var result = new CanFrame
            {
                SourceNodeId = first.SourceNodeId,
                DataSpecifierId = first.DataSpecifierId,
                TransferId = first.TransferId,
                Message = first.Message,
                EndOfTransfer = true
            };

            int totalPayload = frames.Sum(f => f.PayloadLength);
            if (totalPayload < 2) return null; // Must at least have CRC

            byte[] fullPayloadWithCrc = new byte[totalPayload];
            int offset = 0;
            foreach (var f in frames)
            {
                Array.Copy(f.Payload, 0, fullPayloadWithCrc, offset, f.PayloadLength);
                offset += f.PayloadLength;
            }

            // The last 2 bytes are the CRC
            ushort receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(fullPayloadWithCrc.AsSpan(totalPayload - 2));
            ushort calculatedCrc = Crc.Calculate(fullPayloadWithCrc.AsSpan(0, totalPayload - 2));

            if (receivedCrc != calculatedCrc)
            {
                return null; // CRC mismatch
            }

            // Payload without CRC
            result.SetPayload(fullPayloadWithCrc.AsSpan(0, totalPayload - 2));
            
            return result;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }

        private class CanTransferContext
        {
            public string Key { get; }
            public List<CanFrame> Frames { get; }
            public DateTime LastUpdate { get; set; }
            public bool ExpectedToggle { get; set; }

            public CanTransferContext(string key)
            {
                Key = key;
                Frames = new List<CanFrame>();
                LastUpdate = DateTime.UtcNow;
                ExpectedToggle = false;
            }
        }
    }

    /// <summary>
    /// Represents a raw CAN frame.
    /// </summary>
    public struct CanRawFrame
    {
        /// <summary>
        /// 29-bit CAN ID.
        /// </summary>
        public uint CanId;

        /// <summary>
        /// CAN payload (1-64 bytes).
        /// </summary>
        public byte[] Payload;
    }
}
