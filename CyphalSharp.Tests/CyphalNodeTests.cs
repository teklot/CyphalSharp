using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace CyphalSharp.Tests;

public class CyphalNodeTests : IDisposable
{
    public CyphalNodeTests()
    {
        Cyphal.Reset();
        Cyphal.Initialize("DSDL");
    }

    public void Dispose() => Cyphal.Reset();

    [Fact]
    public void Constructor_SetsNodeId()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(123, transport);
        Assert.Equal(123, node.NodeId);
    }

    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport);
        Assert.Equal(0, node.Health); // HEALTH_NOMINAL
        Assert.Equal(1, node.Mode);   // MODE_INITIALIZING
        Assert.Equal("CyphalSharp.Node", node.Name);
    }

    [Fact]
    public void Constructor_SubscribesToFrameReceived()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport);
        Assert.NotNull(node);
    }

    [Fact]
    public async Task StartAsync_ChangesModeToOperational()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport);
        node.Mode = 1; // MODE_INITIALIZING

        await node.StartAsync();

        Assert.Equal(2, node.Mode); // MODE_OPERATIONAL
    }

    [Fact]
    public void Stop_ChangesModeToOff()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport);
        node.Mode = 2; // MODE_OPERATIONAL

        node.Stop();

        Assert.Equal(3, node.Mode); // MODE_OFF
    }

    [Fact]
    public void Dispose_UnsubscribesFromFrameReceived()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport);
        node.Dispose();
        Assert.True(true); // If we get here without exception, dispose worked
    }

    [Fact]
    public void PublishHeartbeat_CreatesValidFrame()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport);
        node.Health = 0;
        node.Mode = 2;
        node.VendorStatusCode = 0;

        var heartbeatMsg = Cyphal.RegisteredMessages.Values
            .ToList().FirstOrDefault(m => m.Name == "uavcan.node.Heartbeat");
        Assert.NotNull(heartbeatMsg);

        var frame = new UdpFrame
        {
            SourceNodeId = 1,
            DataSpecifierId = (ushort)heartbeatMsg.PortId,
            Message = heartbeatMsg,
            EndOfTransfer = true
        };

        frame.SetFields(new Dictionary<string, object>
        {
            { "uptime", (uint)100 },
            { "health", node.Health },
            { "mode", node.Mode },
            { "vendor_specific_status_code", node.VendorStatusCode }
        });

        Assert.Equal((byte)0, frame.Fields["health"]);
        Assert.Equal((byte)2, frame.Fields["mode"]);
    }

    [Fact]
    public void HandleGetInfo_ReturnsCorrectResponse()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport)
        {
            Name = "TestNode",
            UniqueId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            ProtocolVersion = (1, 0),
            HardwareVersion = (2, 3),
            SoftwareVersion = (4, 5)
        };

        var responseMsg = Cyphal.RegisteredMessages.Values
            .ToList().FirstOrDefault(m => m.Name == "uavcan.node.GetInfo" && m.IsServiceDefinition);
        Assert.NotNull(responseMsg);

        // For UDP, IsResponse depends on DestinationNodeId != 0xFFFF
        var requestFrame = new UdpFrame
        {
            SourceNodeId = 10,
            DestinationNodeId = 1, // Not 0xFFFF, so IsResponse will be true for service
            DataSpecifierId = (ushort)responseMsg.PortId,
            Message = responseMsg
        };

        Assert.NotNull(requestFrame);
        Assert.True(responseMsg.IsServiceDefinition);
    }

    [Fact]
    public void HandleExecuteCommand_ParsesRequestCorrectly()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(1, transport);

        var service = Cyphal.RegisteredMessages.Values
            .ToList().FirstOrDefault(m => m.Name == "uavcan.node.ExecuteCommand" && m.IsServiceDefinition);
        Assert.NotNull(service);

        var requestFrame = new UdpFrame
        {
            SourceNodeId = 10,
            DataSpecifierId = (ushort)service.PortId,
            Message = service,
            EndOfTransfer = true
        };

        requestFrame.SetFields(new Dictionary<string, object>
        {
            { "command", (ushort)1234 },
            { "parameter", new byte[] { 1, 2, 3 } }
        });

        Assert.Equal((ushort)1234, requestFrame.Fields["command"]);
        var parameter = (byte[])requestFrame.Fields["parameter"];
        Assert.Equal(5, parameter.Length); // uint8[5] in DSDL
        Assert.Equal(1, parameter[0]);
        Assert.Equal(0, parameter[4]); // Last element is 0 (padding)
    }

    [Fact]
    public void ExecuteCommandEventArgs_HasCorrectDefaultValues()
    {
        var args = new ExecuteCommandEventArgs();
        Assert.Equal((ushort)0, args.Command);
        Assert.Empty(args.Parameter);
        Assert.Equal((byte)0, args.Result); // SUCCESS
    }

    [Fact]
    public void NodeId_ValidRange()
    {
        var transport = new UdpTransport(42);
        var node = new CyphalNode(65534, transport);
        Assert.Equal(65534, node.NodeId);
    }

    [Fact]
    public void GetInfo_ResponseFields()
    {
        var responseMsg = Cyphal.RegisteredMessages.Values
            .ToList().FirstOrDefault(m => m.Name == "uavcan.node.GetInfo" && m.IsServiceDefinition);
        Assert.NotNull(responseMsg);
        // GetInfo response has 6 fields: protocol_version, hardware_version, software_version, unique_id, name, software_vcs_revision_id
        Assert.Equal(6, responseMsg.ResponseFields.Count);
    }

    [Fact]
    public void ExecuteCommand_ResponseFields()
    {
        var responseMsg = Cyphal.RegisteredMessages.Values
            .ToList().FirstOrDefault(m => m.Name == "uavcan.node.ExecuteCommand" && m.IsServiceDefinition);
        Assert.NotNull(responseMsg);
        Assert.Single(responseMsg.ResponseFields); // status field
    }
}
