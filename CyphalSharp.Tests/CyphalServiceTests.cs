using System.Buffers.Binary;

namespace CyphalSharp.Tests;

public class CyphalServiceTests
{
    public CyphalServiceTests()
    {
        // Re-initialize to pick up the new DSDL
        Cyphal.Reset();
        Cyphal.Initialize("DSDL");
    }

    [Fact]
    public void Parse_ServiceDSDL_CorrectlySplitsRequestAndResponse()
    {
        // Arrange & Act
        var service = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name == "uavcan.node.ExecuteCommand");

        // Assert
        Assert.NotNull(service);
        Assert.True(service.IsServiceDefinition);
        Assert.Equal(2, service.Fields.Count); // command, parameter
        Assert.Single(service.ResponseFields); // status
        
        Assert.Equal("command", service.Fields[0].Name);
        Assert.Equal("parameter", service.Fields[1].Name);
        Assert.Equal("status", service.ResponseFields[0].Name);
    }

    [Fact]
    public void Parse_ServiceRequest_UsesCorrectFields()
    {
        // Arrange
        var service = Cyphal.RegisteredMessages.Values.First(m => m.Name == "uavcan.node.ExecuteCommand");
        ushort dataSpecifierId = (ushort)service.PortId; // Already has 0x8000 set by DsdlParser

        var payload = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0, 2), 0x1234); // command
        for(int i=0; i<5; i++) payload[2+i] = (byte)i;

        var frame = new UdpFrame();
        // DestinationNodeId = 0 for Request
        var packet = CreatePacketRaw(10, 0, dataSpecifierId, 1, payload);

        // Act
        var result = frame.TryParse(packet);

        // Assert
        Assert.True(result, $"Parse failed: {frame.ErrorReason}");
        Assert.False(frame.IsResponse);
        Assert.Equal((ushort)0x1234, frame.Fields["command"]);
        var parameter = (byte[])frame.Fields["parameter"];
        Assert.Equal(5, parameter.Length);
        Assert.Equal(0, parameter[0]);
        Assert.Equal(4, parameter[4]);
    }

    [Fact]
    public void Parse_ServiceResponse_UsesCorrectFields()
    {
        // Arrange
        var service = Cyphal.RegisteredMessages.Values.First(m => m.Name == "uavcan.node.ExecuteCommand");
        ushort dataSpecifierId = (ushort)service.PortId; // Already has 0x8000 set by DsdlParser

        var payload = new byte[1];
        payload[0] = 42; // status

        // In UdpFrame: IsResponse => IsService && DestinationNodeId != 0xFFFF
        var frame = new UdpFrame();
        var packet = CreatePacketRaw(10, 20, dataSpecifierId, 1, payload);

        // Act
        var result = frame.TryParse(packet);

        // Assert
        Assert.True(result, $"Parse failed: {frame.ErrorReason}");
        Assert.True(frame.IsResponse);
        Assert.Equal((byte)42, frame.Fields["status"]);
        Assert.False(frame.Fields.ContainsKey("command"));
    }

    [Fact]
    public void ToBytes_ServiceRequest_EncodesCorrectly()
    {
        // Arrange
        var service = Cyphal.RegisteredMessages.Values.First(m => m.Name == "uavcan.node.ExecuteCommand");
        var frame = new UdpFrame
        {
            SourceNodeId = 10,
            DestinationNodeId = 0,
            DataSpecifierId = (ushort)service.PortId,
            TransferId = 1,
            EndOfTransfer = true,
            Message = service
        };

        var values = new Dictionary<string, object>
        {
            { "command", (ushort)0xABCD },
            { "parameter", new byte[] { 1, 2, 3, 4, 5 } }
        };
        frame.SetFields(values);

        // Act
        byte[] packet = frame.ToBytes();

        // Assert
        Assert.False(frame.IsResponse);
        Assert.Equal(UdpProtocol.HeaderLength + 7, packet.Length); // 2 (cmd) + 5 (data)
        Assert.Equal(0xCD, packet[UdpProtocol.HeaderLength]);
        Assert.Equal(0xAB, packet[UdpProtocol.HeaderLength + 1]);
        Assert.Equal(1, packet[UdpProtocol.HeaderLength + 2]);
    }

    [Fact]
    public void ToBytes_ServiceResponse_EncodesCorrectly()
    {
        // Arrange
        var service = Cyphal.RegisteredMessages.Values.First(m => m.Name == "uavcan.node.ExecuteCommand");
        var frame = new UdpFrame
        {
            SourceNodeId = 20,
            DestinationNodeId = 10,
            DataSpecifierId = (ushort)service.PortId,
            TransferId = 1,
            EndOfTransfer = true,
            Message = service
        };

        var values = new Dictionary<string, object>
        {
            { "status", (byte)123 }
        };
        frame.SetFields(values);

        // Act
        byte[] packet = frame.ToBytes();

        // Assert
        Assert.True(frame.IsResponse);
        Assert.Equal(UdpProtocol.HeaderLength + 1, packet.Length);
        Assert.Equal(123, packet[UdpProtocol.HeaderLength]);
    }

    private byte[] CreatePacketRaw(ushort srcNodeId, ushort dstNodeId, ushort dataSpecifierId, ulong transferId, byte[] payload)
    {
        var packetBytes = new byte[UdpProtocol.HeaderLength + payload.Length];
        var span = packetBytes.AsSpan();

        span[0] = 0; // Version
        span[1] = 4; // Priority
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), srcNodeId);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), dstNodeId);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), dataSpecifierId);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(8), transferId);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0 | UdpProtocol.EndOfTransferMask);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20), 0);

        payload.CopyTo(span.Slice(UdpProtocol.HeaderLength));

        ushort headerCrc = Crc.Calculate(span.Slice(0, UdpProtocol.HeaderLength - 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22), headerCrc);
        
        return packetBytes;
    }
}
