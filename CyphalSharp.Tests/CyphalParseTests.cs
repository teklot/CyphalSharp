using System.Buffers.Binary;

namespace CyphalSharp.Tests;

public class CyphalParseTests
{
    public CyphalParseTests()
    {
        // Ensure initialization happens once or is safe to call
        Cyphal.Initialize("DSDL");
    }

    [Fact]
    public void Parse_ValidHeartbeatPacket_ReturnsCorrectFrame()
    {
        // Arrange
        ushort sourceNodeId = 123;
        ushort destNodeId = 456;
        ulong transferId = 999;
        
        // Heartbeat.1.0.dsdl
        var heartbeatMessage = Cyphal.RegisteredMessages.Values.First(m => m.Name == "uavcan.node.Heartbeat");
        ushort dataSpecifierId = (ushort)heartbeatMessage.PortId;

        // Create a dummy payload for Heartbeat
        // uint32 uptime, uint8 health, uint8 mode, uint8 vssc
        var payload = new byte[heartbeatMessage.PayloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1234); // uptime
        payload[4] = 0; // health = NOMINAL
        payload[5] = 0; // mode = OPERATIONAL
        payload[6] = 42; // vssc

        // Manually construct Cyphal/UDP packet
        var packetBytes = CreatePacketRaw(sourceNodeId, destNodeId, dataSpecifierId, transferId, payload);

        // Act
        var frame = new UdpFrame();
        var result = frame.TryParse(packetBytes);

        // Assert
        Assert.True(result, $"Parse failed: {frame.ErrorReason}");
        Assert.NotNull(frame);
        Assert.Equal(sourceNodeId, frame.SourceNodeId);
        Assert.Equal(destNodeId, frame.DestinationNodeId);
        Assert.Equal(transferId, frame.TransferId);
        Assert.Equal(dataSpecifierId, frame.DataSpecifierId);
        Assert.True(frame.EndOfTransfer);
        Assert.NotNull(frame.Fields);
        Assert.Equal((uint)1234, frame.Fields["uptime"]);
        Assert.Equal((byte)0, frame.Fields["health"]);
        Assert.Equal((byte)42, frame.Fields["vendor_specific_status_code"]);
    }

    private byte[] CreatePacketRaw(ushort srcNodeId, ushort dstNodeId, ushort subjectId, ulong transferId, byte[] payload)
    {
        var packetBytes = new byte[UdpProtocol.HeaderLength + payload.Length];
        var span = packetBytes.AsSpan();

        // Header
        span[0] = 0; // Version
        span[1] = 4; // Priority (Nominal)
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), srcNodeId);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), dstNodeId);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), subjectId);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8), transferId);
        
        uint indexAndEot = 0 | UdpProtocol.EndOfTransferMask; // Index 0, EOT set
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), indexAndEot);
        
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20), 0); // User Data

        // Payload
        payload.CopyTo(span.Slice(UdpProtocol.HeaderLength));

        // Header CRC
        ushort headerCrc = Crc.Calculate(span.Slice(0, UdpProtocol.HeaderLength - 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22), headerCrc);
        
        return packetBytes;
    }
}
