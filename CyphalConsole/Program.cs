using CyphalSharp;
using System.Net;
using System.Net.Sockets;

namespace CyphalConsole;

class Program
{
    private const int CyphalUdpPort = 14550; // Standard Cyphal UDP port
    private const string TargetIpAddress = "127.0.0.1"; // Localhost (assuming same machine for Tx/Rx)

    static async Task Main(string[] args)
    {
        TerminalLayout.Initialize();

        // Initialize CyphalSharp with the DSDL directory
        Cyphal.Initialize("DSDL");

        // Create a single UDP client for both sending and receiving
        // It's crucial to bind it for receiving first.
        using (var udpClient = new UdpClient(CyphalUdpPort))
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse(TargetIpAddress), CyphalUdpPort);

            // Run Tx and Rx tasks concurrently
            var txTask = Task.Run(() => Transmitter.Run(udpClient, remoteEndPoint));
            var rxTask = Receiver.RunAsync(udpClient);

            // Keep the application alive until both tasks complete (which will be never in this case)
            // or a cancellation token is used. For this example, we just await them.
            await Task.WhenAll(txTask, rxTask); 
        }
    }
}
