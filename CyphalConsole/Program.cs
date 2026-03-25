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
        string transportType = args.Length > 0 ? args[0].ToUpper() : "UDP";

        TerminalLayout.Initialize();

        // Initialize CyphalSharp with the DSDL directory
        Cyphal.Initialize("DSDL");

        if (transportType == "CAN")
        {
            await RunCanMode();
        }
        else
        {
            await RunUdpMode();
        }
    }

    static async Task RunUdpMode()
    {
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

    static async Task RunCanMode()
    {
        // Use a different port for simulated CAN to avoid conflict with standard UDP
        const int CanSimPort = 14551;
        using (var udpClient = new UdpClient(CanSimPort))
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse(TargetIpAddress), CanSimPort);

            // In CAN mode, we use the CanTransport reassembly logic
            var canTransport = new CanTransport();
            
            // Run Tx and Rx tasks concurrently
            var txTask = Task.Run(() => Transmitter.RunCan(udpClient, remoteEndPoint, canTransport));
            var rxTask = Receiver.RunCanAsync(udpClient, canTransport);

            // Keep the application alive
            await Task.WhenAll(txTask, rxTask);
        }
    }
}
