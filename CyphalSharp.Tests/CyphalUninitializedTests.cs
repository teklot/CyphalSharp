namespace CyphalSharp.Tests;

public class CyphalUninitializedTests
{
    [Fact]
    public void TryParse_BeforeInitialize_ThrowsException()
    {
        // Arrange
        Cyphal.Reset();
        // Just a dummy 24-byte header
        var packet = new byte[24]; 

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new UdpFrame().TryParse(packet));
    }
}
