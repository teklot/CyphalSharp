using BenchmarkDotNet.Attributes;
using System.Buffers.Binary;
using System.Linq;
using System;

namespace CyphalSharp.Benchmark
{
    [MemoryDiagnoser]
    public class MessageParsingBenchmark
    {
        private byte[] _heartbeatPacket = null!;
        private ushort _messageId = 0; // HEARTBEAT (Subject ID)
        private readonly UdpFrame _frame = new UdpFrame();

        [GlobalSetup]
        public void Setup()
        {
            Cyphal.Initialize("DSDL");

            var heartbeatMessage = Cyphal.RegisteredMessages.Values.First(m => m.Name == "uavcan.node.Heartbeat");
            _messageId = (ushort)heartbeatMessage.PortId;

            var payload = new byte[heartbeatMessage.PayloadLength];
            // uptime, health, mode, vssc = 0

            int totalLen = UdpProtocol.HeaderLength + payload.Length;
            _heartbeatPacket = new byte[totalLen];
            var span = _heartbeatPacket.AsSpan();

            // Header
            span[0] = 0; // Version
            span[1] = 3; // Priority
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), 100); // Src
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), 200); // Dst
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), _messageId); // Subject ID
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8), 123456789); // Transfer ID
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0 | UdpProtocol.EndOfTransferMask); // Index 0 + EOT
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20), 0); // User Data

            // Payload
            payload.CopyTo(span.Slice(UdpProtocol.HeaderLength));

            // Header CRC
            ushort crc = Crc.Calculate(span.Slice(0, UdpProtocol.HeaderLength - 2));
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22), crc);
        }

        [Benchmark]
        public bool TryParse()
        {
            return _frame.TryParse(_heartbeatPacket);
        }
    }
}
