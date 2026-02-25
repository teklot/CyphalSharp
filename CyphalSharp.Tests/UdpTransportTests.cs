using System.Net;
using System.Reflection;

namespace CyphalSharp.Tests;

public class UdpTransportTests
{
    [Theory]
    [InlineData(7509, "239.0.29.85")]  // Heartbeat: 7509 = 0x1D55 => 0x1D (29), 0x55 (85)
    [InlineData(1234, "239.0.4.210")] // 1234 = 0x04D2 => 0x04 (4), 0xD2 (210)
    public void GetMulticastAddress_ReturnsCorrectMapping(ushort subjectId, string expectedIp)
    {
        var transport = new UdpTransport(42);
        
        // Access private method via reflection for testing
        var method = typeof(UdpTransport).GetMethod("GetMulticastAddress", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var result = (IPAddress)method.Invoke(transport, new object[] { subjectId })!;

        Assert.Equal(expectedIp, result.ToString());
    }

    [Fact]
    public void Reassembly_CorrectlyCombinesMultipleFrames()
    {
        // Arrange
        var transport = new UdpTransport(42);
        var receivedFrames = new List<IFrame>();
        transport.FrameReceived += (s, e) => receivedFrames.Add(e);

        var msg = new Message { Name = "test.MultiFrame", PayloadLength = 10 };
        
        // Frame 1
        var f1 = new UdpFrame
        {
            SourceNodeId = 100,
            DataSpecifierId = 1000,
            TransferId = 1,
            FrameIndex = 0,
            EndOfTransfer = false,
            Message = msg
        };
        f1.SetPayload(new byte[] { 1, 2, 3 });

        // Frame 2
        var f2 = new UdpFrame
        {
            SourceNodeId = 100,
            DataSpecifierId = 1000,
            TransferId = 1,
            FrameIndex = 1,
            EndOfTransfer = true,
            Message = msg
        };
        f2.SetPayload(new byte[] { 4, 5, 6 });

        // Act - Inject frames using reflection to simulate ReceiveLoop
        var handleMethod = typeof(UdpTransport).GetMethod("HandleIncomingFrame", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handleMethod);
        
        handleMethod.Invoke(transport, new object[] { f1 });
        Assert.Empty(receivedFrames); // Should not be complete yet

        handleMethod.Invoke(transport, new object[] { f2 });

        // Assert
        Assert.Single(receivedFrames);
        var result = receivedFrames[0];
        Assert.Equal(6, result.PayloadLength);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, result.Payload.Take(6).ToArray());
    }

    [Fact]
    public void Reassembly_OutofOrder_HandlesCorrectly()
    {
        // Arrange
        var transport = new UdpTransport(42);
        var receivedFrames = new List<IFrame>();
        transport.FrameReceived += (s, e) => receivedFrames.Add(e);

        var msg = new Message { Name = "test.MultiFrame" };
        
        var f1 = new UdpFrame { SourceNodeId = 5, DataSpecifierId = 100, TransferId = 9, FrameIndex = 0, EndOfTransfer = false, Message = msg };
        f1.SetPayload(new byte[] { 0xAA });

        var f2 = new UdpFrame { SourceNodeId = 5, DataSpecifierId = 100, TransferId = 9, FrameIndex = 1, EndOfTransfer = true, Message = msg };
        f2.SetPayload(new byte[] { 0xBB });

        var handleMethod = typeof(UdpTransport).GetMethod("HandleIncomingFrame", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handleMethod);
        
        // Act: Receive Last frame first
        handleMethod.Invoke(transport, new object[] { f2 });
        Assert.Empty(receivedFrames); 

        // Receive First frame
        handleMethod.Invoke(transport, new object[] { f1 });

        // Assert
        Assert.Single(receivedFrames);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, receivedFrames[0].Payload.Take(2).ToArray());
    }

    [Fact]
    public void Constructor_WithCustomTimeout_SetsTimeout()
    {
        var transport = new UdpTransport(42, 5000);
        // Just verify it can be constructed with custom timeout without error
        Assert.NotNull(transport);
    }

    [Fact]
    public void Reassembly_MissingFrame_DoesNotComplete()
    {
        // Arrange
        var transport = new UdpTransport(42);
        var receivedFrames = new List<IFrame>();
        transport.FrameReceived += (s, e) => receivedFrames.Add(e);

        var msg = new Message { Name = "test.MultiFrame" };
        
        // Frame 0
        var f0 = new UdpFrame { SourceNodeId = 1, DataSpecifierId = 200, TransferId = 5, FrameIndex = 0, EndOfTransfer = false, Message = msg };
        f0.SetPayload(new byte[] { 1 });
        
        // Frame 2 (Frame 1 is missing)
        var f2 = new UdpFrame { SourceNodeId = 1, DataSpecifierId = 200, TransferId = 5, FrameIndex = 2, EndOfTransfer = true, Message = msg };
        f2.SetPayload(new byte[] { 3 });

        var handleMethod = typeof(UdpTransport).GetMethod("HandleIncomingFrame", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handleMethod);
        
        handleMethod.Invoke(transport, new object[] { f0 });
        handleMethod.Invoke(transport, new object[] { f2 });

        // Assert - should not complete because frame 1 is missing
        Assert.Empty(receivedFrames);
    }
}
