using CyphalSharp.Enums;

namespace CyphalSharp.Tests;

public class UdpFrameTests : IDisposable
{
    public UdpFrameTests()
    {
        Cyphal.Reset();
        // Initialize with the standard DSDLs we added
        string dsdlPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "DSDL"));
        Cyphal.Initialize(dsdlPath);
    }

    public void Dispose()
    {
        Cyphal.Reset();
    }

    [Fact]
    public void Heartbeat_Serialization_RoundTrip()
    {
        // Find Heartbeat message
        var msg = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name.Contains("Heartbeat"));
        Assert.NotNull(msg);

        var frame = new UdpFrame
        {
            SourceNodeId = 100,
            DestinationNodeId = 0xFFFF, // Broadcast
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 1,
            EndOfTransfer = true,
            Message = msg
        };

        var values = new Dictionary<string, object>
        {
            { "uptime", 123456U },
            { "health", (byte)0 },
            { "mode", (byte)0 },
            { "vendor_specific_status_code", (byte)42 }
        };
        frame.SetFields(values);

        byte[] bytes = frame.ToBytes();
        Assert.NotEmpty(bytes);

        // Parse back
        var parsedFrame = new UdpFrame();
        bool result = parsedFrame.TryParse(bytes);

        Assert.True(result, $"Parse failed: {parsedFrame.ErrorReason}");
        Assert.Equal(123456U, parsedFrame.Fields["uptime"]);
        Assert.Equal((byte)0, parsedFrame.Fields["health"]);
        Assert.Equal((byte)42, parsedFrame.Fields["vendor_specific_status_code"]);
    }

    [Fact]
    public void Service_RequestResponse_Handling()
    {
        // ExecuteCommand.1.1.dsdl
        var msg = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name.Contains("ExecuteCommand"));
        Assert.NotNull(msg);
        Assert.True(msg.IsServiceDefinition);

        // 1. Test Request
        var requestFrame = new UdpFrame
        {
            SourceNodeId = 100,
            DestinationNodeId = 0,
            DataSpecifierId = (ushort)msg.PortId, // Service bit is already set in PortId by parser
            TransferId = 50,
            EndOfTransfer = true,
            Message = msg
        };

        var reqValues = new Dictionary<string, object>
        {
            { "command", (ushort)0x1234 },
            { "parameter", new byte[] { 1, 2, 3, 4, 5 } }
        };
        requestFrame.SetFields(reqValues);

        byte[] reqBytes = requestFrame.ToBytes();
        var parsedReq = new UdpFrame();
        Assert.True(parsedReq.TryParse(reqBytes));
        Assert.False(parsedReq.IsResponse);
        Assert.Equal((ushort)0x1234, parsedReq.Fields["command"]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, (byte[])parsedReq.Fields["parameter"]);

        // 2. Test Response
        var responseFrame = new UdpFrame
        {
            SourceNodeId = 123,
            DestinationNodeId = 100, // Non-broadcast indicates response in this library's IsResponse logic
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 50,
            EndOfTransfer = true,
            Message = msg
        };

        var respValues = new Dictionary<string, object>
        {
            { "status", (byte)7 }
        };
        responseFrame.SetFields(respValues);

        byte[] respBytes = responseFrame.ToBytes();
        var parsedResp = new UdpFrame();
        Assert.True(parsedResp.TryParse(respBytes));
        Assert.True(parsedResp.IsResponse);
        Assert.Equal((byte)7, parsedResp.Fields["status"]);
    }

    [Fact]
    public void PrimitiveString_Handling()
    {
        var msg = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name.Contains("primitive.String"));
        Assert.NotNull(msg);

        var frame = new UdpFrame
        {
            SourceNodeId = 10,
            DataSpecifierId = (ushort)msg.PortId,
            Message = msg
        };

        // String.1.0 has uint8[<=256] value
        // Our Field.cs currently treats it as a fixed-size array of 256 bytes based on BitLength
        byte[] stringData = new byte[256];
        "Hello Cyphal".Select((c, i) => (byte)c).ToArray().CopyTo(stringData, 0);

        frame.SetFields(new Dictionary<string, object> { { "value", stringData } });

        byte[] bytes = frame.ToBytes();
        var parsed = new UdpFrame();
        Assert.True(parsed.TryParse(bytes));
        
        byte[] recovered = (byte[])parsed.Fields["value"];
        Assert.Equal((byte)'H', recovered[0]);
        Assert.Equal((byte)'e', recovered[1]);
    }

    [Fact]
    public void TryParse_SeverelyShortPayload_SetsPayloadLengthInvalid()
    {
        var msg = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name.Contains("Heartbeat"));
        Assert.NotNull(msg);

        var frame = new UdpFrame
        {
            SourceNodeId = 100,
            DestinationNodeId = 0xFFFF,
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 1,
            EndOfTransfer = true,
            Message = msg
        };

        byte[] bytes = frame.ToBytes();
        // Truncate payload to be severely short (< 50% of expected)
        byte[] truncated = bytes.Take(UdpProtocol.HeaderLength + 1).ToArray();
        
        var parsed = new UdpFrame();
        bool result = parsed.TryParse(truncated);
        
        Assert.True(result); // Should still parse (with truncation)
        Assert.Equal(Enums.ErrorReason.PayloadLengthInvalid, parsed.ErrorReason);
    }

    [Fact]
    public void TryParse_ValidPayload_HasNoError()
    {
        var msg = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name.Contains("Heartbeat"));
        Assert.NotNull(msg);

        var frame = new UdpFrame
        {
            SourceNodeId = 100,
            DestinationNodeId = 0xFFFF,
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 1,
            EndOfTransfer = true,
            Message = msg
        };
        frame.SetFields(new Dictionary<string, object>
        {
            { "uptime", 100U },
            { "health", (byte)0 },
            { "mode", (byte)0 },
            { "vendor_specific_status_code", (byte)0 }
        });

        var parsed = new UdpFrame();
        bool result = parsed.TryParse(frame.ToBytes());
        
        Assert.True(result);
        Assert.Equal(Enums.ErrorReason.None, parsed.ErrorReason);
    }
}
