# CyphalSharp

CyphalSharp is a lightweight .NET library for parsing Cyphal messages using DSDL (Data Structure Description Language) definitions as specified by [OpenCyphal](https://opencyphal.org/). It is extremely fast, flexible, and easy to use, and also provides tools for constructing and encoding Cyphal packets for transmission over any communication protocol.

## Features
 - **Runtime DSDL Parsing:** Consumes Cyphal `.dsdl` files at runtime. No code generation required.
 - **Service Support:** Full support for Cyphal Service (RPC) patterns, correctly handling separate Request and Response parts defined with the `---` separator.
 - **Transport Agnostic:** Designed with a flexible interface (`IFrame`, `ITransport`) to support various Cyphal transports (UDP, CAN, Serial, etc.).
 - **Cyphal/UDP Multicast & Reassembly:** Robust UDP transport implementation that handles standard multicast address mapping and transparent multi-frame transfer reassembly.
 - **High Performance:** Designed for speed and low allocation to handle high-throughput Cyphal streams.
 - **Streaming Ready:** Built-in support for asynchronous, zero-allocation streaming using `ReadOnlySequence<byte>`.
 - **Cross-Platform:** Can be used on any platform that supports .NET Standard 2.0 (Windows, Linux, macOS, etc.).
 - **Minimal Dependencies:** Only requires `System.Memory` and `System.Buffers` for modern memory-efficient parsing.

## Supported Frameworks

`CyphalSharp` targets **.NET Standard 2.0**, providing broad compatibility across modern and legacy .NET platforms.

### Compatible Platforms:

*   **.NET Core / .NET (5+):** All versions.
*   **.NET Framework:** 4.6.1 and later (4.7.2+ recommended).
*   **Mono:** 5.4 and later.
*   **Xamarin.iOS / Android**
*   **UWP:** 10.0.16299 and later.

## Getting Started

Using the library involves four main steps:

1.  **Add the NuGet Package:** Install the `CyphalSharp` package from NuGet into your .NET project.
    ```powershell
    Install-Package CyphalSharp
    ```
2.  **Initialize the Library:** At application startup, call the static `Cyphal.Initialize()` method. You must specify the directory containing your DSDL files (e.g., `DSDL`).
    > **Important:** This step is mandatory. Calling `frame.TryParse()` before `Cyphal.Initialize()` will result in an `InvalidOperationException`.
3.  **Parse Incoming Data:** Create a transport-specific frame object (e.g., `UdpFrame`) once and reuse it. As you receive data from a Cyphal stream, pass the raw `byte[]` or `Span<byte>` packet to the `frame.TryParse()` method.
4.  **Use the Result:** If `TryParse()` returns `true`, the `frame` object will be populated with the decoded message and its fields.

## Transports

CyphalSharp is designed to be extensible. Currently, it includes built-in support for:
- **Cyphal/UDP:** via `UdpFrame` and the high-level `UdpTransport` which handles:
    - Automatic IGMP/Multicast group joining based on Subject IDs.
    - Transparent reassembly of multi-frame transfers.
    - Port-to-Address mapping according to the Cyphal/UDP specification.

To support a new transport, you can implement the `IFrame` and `ITransport` interfaces or derive from the `Frame` base class.

## DSDL Handling

The `CyphalSharp` library utilizes a **runtime parsing mechanism** for Cyphal DSDL files. This means message definitions are loaded and processed dynamically when your application starts, rather than requiring code generation. It correctly handles both Subject (Message) and Service definitions, including those with the `---` Request/Response separator.

### DSDL Format

Cyphal DSDL files are plain text files with the `.dsdl` extension. They follow the naming convention: `<TypeName>.<major>.<minor>.dsdl`.

Example structure:
```
DSDL/
  uavcan/
    Heartbeat.1.0.dsdl
    NodeStatus.1.0.dsdl
```

### Initializing the Library

```cs
// Initialize with your DSDL directory
Cyphal.Initialize("DSDL"); 
```

## Filtering Messages

The `CyphalSharp` library offers flexible control over which Cyphal messages are parsed, allowing you to optimize for performance by only processing messages relevant to your application.

### Initializing Message Parsing

Message filtering begins with the `Cyphal.Initialize()` method. This method loads message definitions from your specified DSDL directory and simultaneously defines the initial set of messages that will be processed.

The `Cyphal.Initialize()` method has the following signature:
```cs
public static void Initialize(string dsdlPath, params uint[] portIds)
```
*   **`dsdlPath`**: (Required) The path to the directory containing your DSDL files.
*   **`portIds`**: (Optional) A list of specific Cyphal Port IDs (`uint`) you wish to enable for parsing immediately upon initialization.
    *   If **`portIds` are provided**, only those specified messages will be marked for parsing.
    *   If **`portIds` are omitted (or an empty array is passed)**, then *all messages* found in the DSDL directory will be initially marked for parsing.

### Fine-Tuning Message Parsing (Include/Exclude)

After initialization, you can further fine-tune which messages are parsed using `Cyphal.IncludeMessages()` and `Cyphal.ExcludeMessages()`. These static methods allow you to dynamically enable or disable parsing for specific messages.

*   **`Cyphal.IncludeMessages(params uint[] messageIds)`**:
    *   **Purpose**: To enable parsing for specific Cyphal message ID(s) that were previously disabled, or to ensure they are enabled.
    *   **Behavior**: If no `messageIds` are provided then *all currently loaded message definitions* will be marked for parsing.
    ```cs
    // After initialization, enable parsing for a specific message
    Cyphal.IncludeMessages(7509);
    ```

*   **`Cyphal.ExcludeMessages(params uint[] messageIds)`**:
    *   **Purpose**: To disable parsing for specific Cyphal message ID(s).
    *   **Behavior**: The specified messages will be marked as excluded and will be ignored by `frame.TryParse()`.
    ```cs
    // Disable parsing for another message
    Cyphal.ExcludeMessages(390);
    ```

**Example Workflow:**

1.  **Scenario A: Parse only specific messages from the start:**
    ```cs
    // Initialize, loading definitions and enabling only specific message IDs
    Cyphal.Initialize("DSDL", 7509, 390); 
    ```
2.  **Scenario B: Parse all messages from the start:**
    ```cs
    // Initialize, loading definitions and enabling ALL messages
    Cyphal.Initialize("DSDL");
    ```
3.  **Scenario C: Parse most messages, but exclude a few:**
    ```cs
    Cyphal.Initialize("DSDL"); // All messages enabled initially
    Cyphal.ExcludeMessages(390); // Exclude a specific message
    ```

## Code Example (High-Level UDP Transport)

The `UdpTransport` class provides a high-level API that handles networking and reassembly for you.

```cs
using CyphalSharp;

// 1. Initialize
Cyphal.Initialize("DSDL");

// 2. Create and start the transport
var transport = new UdpTransport(nodeId: 42);
await transport.StartAsync();

// 3. Subscribe to a subject (joins multicast group automatically)
transport.SubscribeToSubject(7509); // Heartbeat Subject ID

// 4. Handle received messages
transport.FrameReceived += (sender, frame) => 
{
    var msgName = frame.Message?.Name ?? "Unknown";
    Console.WriteLine($"Received {msgName} from {frame.SourceNodeId}");
    
    foreach(var field in frame.Fields)
    {
        Console.WriteLine($"  {field.Key}: {field.Value}");
    }
};
```

## Code Example (Low-Level UDP)
```cs
using CyphalSharp;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

// 1. Initialize the library with the DSDL directory.
Cyphal.Initialize("DSDL");

// 2. Create a UdpFrame object once and reuse it for high performance (zero allocation).
var frame = new UdpFrame();

// Example: Listen for Cyphal packets on a local UDP port.
var endpoint = new IPEndPoint(IPAddress.Loopback, 14550);
using var udpClient = new UdpClient(endpoint);

Console.WriteLine($"Listening for Cyphal packets on {endpoint}...\n");

while (true)
{
    var packet = udpClient.Receive(ref endpoint);

    // 3. Try to parse the packet into the existing frame object.
    if (frame.TryParse(packet))
    {
        // 4. If successful, use the data.
        var fields = string.Join(", ", frame.Fields.Select(f => $"{f.Key}: {f.Value}"));
        Console.WriteLine($"Received: {Cyphal.RegisteredMessages[frame.DataSpecifierId].Name} => {fields}");
    }
}
```

## Constructing and Sending Messages
`CyphalSharp` makes it easy to construct Cyphal packets for transmission.

```cs
// 1. Get the message definition you want to send.
var heartbeatDef = Cyphal.RegisteredMessages.Values.First(m => m.Name == "uavcan.Heartbeat");

// 2. Create a UdpFrame and set the header information.
var frame = new UdpFrame
{
    Version = 0,
    Priority = 3, // Nominal
    SourceNodeId = 42,
    DestinationNodeId = 0xFFFF, // Broadcast
    DataSpecifierId = (ushort)heartbeatDef.PortId,
    TransferId = 12345,
    FrameIndex = 0,
    EndOfTransfer = true,
    Message = heartbeatDef
};

// 3. Set the field values.
var values = new Dictionary<string, object>
{
    { "uptime", (uint)1234 },
    { "health", (byte)0 },
    { "mode", (byte)0 },
    { "vendor_specific_status_code", (byte)42 }
};
frame.SetFields(values);

// 4. Serialize to a byte array.
byte[] packet = frame.ToBytes();

// 5. Send over your transport (e.g., UDP).
udpClient.Send(packet, packet.Length, remoteEndPoint);
```

## Advanced: Asynchronous Streaming
For high-bandwidth or fragmented streams, `CyphalSharp` supports `ReadOnlySequence<byte>`. This allows for efficient, asynchronous parsing without manual buffer management.

```cs
public void ProcessStream(ReadOnlySequence<byte> buffer)
{
    var frame = new UdpFrame();
    while (frame.TryParse(buffer, out SequencePosition consumed, out SequencePosition examined))
    {
        Console.WriteLine($"Parsed Message ID: {frame.DataSpecifierId}");
        buffer = buffer.Slice(consumed);
    }
}
```

## Example Project: CyphalConsole

The `CyphalConsole` project serves as a practical example demonstrating how to use the `CyphalSharp` library for both sending and receiving Cyphal messages over UDP, all within a single console application. It's particularly useful for testing, development, and quickly observing Cyphal communication.

*   **`CyphalConsole` (Transmitter & Receiver):** This console application runs two concurrent tasks:
    *   **Transmitter (Tx):** Generates and sends synthetic Cyphal messages (e.g., Heartbeat) over UDP to the default Cyphal port (UDP 14550). It showcases how to construct Cyphal `UdpFrame` objects and serialize them into byte arrays for transmission.
    *   **Receiver (Rx):** Listens for incoming Cyphal UDP packets on the default Cyphal port (UDP 14550). It demonstrates how to parse raw byte arrays into `UdpFrame` objects using `frame.TryParse()` and access the decoded message fields. Tx and Rx are displayed in separate halves of the screen.

This example provides a quick way to:
*   **Test your CyphalSharp integration:** Verify that your application can correctly send and receive messages.
*   **Debug Cyphal communication:** Simulate both a Cyphal source and a listener within one application.
*   **Understand basic usage:** See concrete implementations of Cyphal message handling.

**To run this example:**

1.  Navigate to the `CyphalConsole` project directory in your terminal.
2.  Run the project using `dotnet run`.
    *   You will see both `Tx =>` (transmitted) and `Rx =>` (received) messages in the same terminal.

## Benchmark Project: CyphalSharp.Benchmark

The `CyphalSharp.Benchmark` project is a dedicated suite for measuring the performance characteristics of the `CyphalSharp` library. It leverages **BenchmarkDotNet** to provide accurate and reliable performance metrics for critical operations.

Benchmarks currently included:
*   **CRC Calculation:** Measures the speed of `Crc.Calculate()`.
*   **Message Parsing:** Evaluates the performance of `frame.TryParse()`.
*   **Cyphal Initialization:** Measures the loading and parsing time of Cyphal DSDL files.

**To run the benchmarks:**

1.  Navigate to the `CyphalSharp.Benchmark` project directory in your terminal.
2.  Run the project in Release mode (essential for accurate results) using the following command:
    ```bash
    dotnet run -c Release --project CyphalSharp.Benchmark.csproj -- --filter *
    ```
    The `--filter *` argument ensures all benchmarks within the project are executed. BenchmarkDotNet will produce detailed reports in the `BenchmarkDotNet.Artifacts/results` directory.
