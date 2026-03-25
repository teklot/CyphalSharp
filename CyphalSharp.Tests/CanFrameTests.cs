using CyphalSharp.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace CyphalSharp.Tests;

public class CanFrameTests : IDisposable
{
    public CanFrameTests()
    {
        Cyphal.Reset();
        // Initialize with the standard DSDLs
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

        var frame = new CanFrame
        {
            SourceNodeId = 42,
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 1,
            StartOfTransfer = true,
            EndOfTransfer = true,
            Toggle = true,
            Message = msg
        };

        var values = new Dictionary<string, object>
        {
            { "uptime", 1000U },
            { "health", (byte)1 },
            { "mode", (byte)2 },
            { "vendor_specific_status_code", (byte)0 }
        };
        frame.SetFields(values);

        byte[] bytes = frame.ToBytes();
        Assert.NotEmpty(bytes);

        // Parse back
        var parsedFrame = new CanFrame();
        bool result = parsedFrame.TryParse(bytes);

        Assert.True(result, $"Parse failed: {parsedFrame.ErrorReason}");
        Assert.Equal(42, parsedFrame.SourceNodeId);
        Assert.Equal(1000U, parsedFrame.Fields["uptime"]);
        Assert.Equal((byte)1, parsedFrame.Fields["health"]);
        Assert.Equal((byte)2, parsedFrame.Fields["mode"]);
        Assert.True(parsedFrame.StartOfTransfer);
        Assert.True(parsedFrame.EndOfTransfer);
        Assert.True(parsedFrame.Toggle);
    }

    [Fact]
    public void Service_RequestResponse_Handling()
    {
        // ExecuteCommand.1.1.dsdl
        var msg = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name.Contains("ExecuteCommand"));
        Assert.NotNull(msg);
        Assert.True(msg.IsServiceDefinition);

        // 1. Test Request
        var requestFrame = new CanFrame
        {
            SourceNodeId = 10,
            DestinationNodeId = 50,
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 20,
            StartOfTransfer = true,
            EndOfTransfer = true,
            Toggle = true,
            Message = msg
        };

        var reqValues = new Dictionary<string, object>
        {
            { "command", (ushort)0xAAAA },
            { "parameter", new byte[] { 1, 2, 3 } }
        };
        requestFrame.SetFields(reqValues);

        byte[] reqBytes = requestFrame.ToBytes();
        var parsedReq = new CanFrame();
        Assert.True(parsedReq.TryParse(reqBytes));
        Assert.False(parsedReq.IsResponse);
        Assert.Equal(10, parsedReq.SourceNodeId);
        Assert.Equal(50, parsedReq.DestinationNodeId);
        Assert.Equal((ushort)0xAAAA, parsedReq.Fields["command"]);

        // 2. Test Response
        var responseFrame = new CanFrame
        {
            SourceNodeId = 50,
            DestinationNodeId = 10,
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 20,
            StartOfTransfer = true,
            EndOfTransfer = true,
            Toggle = true,
            Message = msg
        };
        // Set IsResponse via CAN ID bit 13
        responseFrame.CanId |= CanProtocol.ServiceRequestResponseMask;

        var respValues = new Dictionary<string, object>
        {
            { "status", (byte)3 }
        };
        responseFrame.SetFields(respValues);

        byte[] respBytes = responseFrame.ToBytes();
        var parsedResp = new CanFrame();
        Assert.True(parsedResp.TryParse(respBytes));
        Assert.True(parsedResp.IsResponse);
        Assert.Equal(50, parsedResp.SourceNodeId);
        Assert.Equal(10, parsedResp.DestinationNodeId);
        Assert.Equal((byte)3, parsedResp.Fields["status"]);
    }

    [Fact]
    public void MultiFrame_Reassembly_Test()
    {
        var transport = new CanTransport();
        IFrame? receivedFrame = null;
        transport.FrameReceived += (s, f) => receivedFrame = f;

        var msg = Cyphal.RegisteredMessages.Values.FirstOrDefault(m => m.Name.Contains("Heartbeat"));
        Assert.NotNull(msg);

        // Create a 2-frame transfer for Heartbeat (7 bytes payload)
        // Frame 1: SOT=1, EOT=0, Toggle=0, payload=4 bytes
        // Frame 2: SOT=0, EOT=1, Toggle=1, payload=3 bytes + 2 bytes CRC = 5 bytes
        // Total reassembled payload = 4 + 3 = 7 bytes.
        
        byte[] fullPayload = new byte[7];
        fullPayload[0] = 0x01; // uptime byte 0
        fullPayload[1] = 0x02; // uptime byte 1
        fullPayload[2] = 0x03; // uptime byte 2
        fullPayload[3] = 0x04; // uptime byte 3
        fullPayload[4] = 0x00; // health
        fullPayload[5] = 0x00; // mode
        fullPayload[6] = 0x00; // vendor_specific_status_code

        ushort crc = Crc.Calculate(fullPayload);

        // Frame 1
        var f1 = new CanFrame
        {
            SourceNodeId = 1,
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 5,
            StartOfTransfer = true,
            EndOfTransfer = false,
            Toggle = false,
            Message = msg
        };
        f1.SetPayload(fullPayload.AsSpan(0, 4));
        byte[] b1 = f1.ToBytes();
        uint id1 = BitConverter.ToUInt32(b1, 0);
        byte[] p1 = b1.Skip(4).ToArray();

        // Frame 2
        var f2 = new CanFrame
        {
            SourceNodeId = 1,
            DataSpecifierId = (ushort)msg.PortId,
            TransferId = 5,
            StartOfTransfer = false,
            EndOfTransfer = true,
            Toggle = true,
            Message = msg
        };
        byte[] p2_with_crc = new byte[3 + 2];
        fullPayload.AsSpan(4, 3).CopyTo(p2_with_crc);
        p2_with_crc[3] = (byte)(crc & 0xFF);
        p2_with_crc[4] = (byte)((crc >> 8) & 0xFF);
        f2.SetPayload(p2_with_crc);
        byte[] b2 = f2.ToBytes();
        uint id2 = BitConverter.ToUInt32(b2, 0);
        byte[] p2 = b2.Skip(4).ToArray();

        transport.ProcessRawFrame(id1, p1);
        Assert.Null(receivedFrame);

        transport.ProcessRawFrame(id2, p2);
        Assert.NotNull(receivedFrame);

        Assert.Equal(1U, receivedFrame.SourceNodeId);
        Assert.Equal(0x04030201U, receivedFrame.Fields["uptime"]);
    }
}
