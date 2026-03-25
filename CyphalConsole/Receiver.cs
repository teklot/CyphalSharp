using CyphalSharp;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;

namespace CyphalConsole;

static class Receiver
{
    public static async Task RunAsync(UdpClient udpClient, CancellationToken cancellationToken = default)
    {
        // 1. Create a Pipe for the byte stream
        var pipe = new Pipe();
        
        // 2. Create a Channel for parsed Frames (decouple IO from logic)
        var channel = Channel.CreateBounded<IFrame>(new BoundedChannelOptions(100)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var fillTask = FillPipeAsync(udpClient, pipe.Writer, cancellationToken);
        var parseTask = ParsePipeAsync(pipe.Reader, channel.Writer, cancellationToken);
        var processTask = ProcessChannelAsync(channel.Reader, cancellationToken);

        await Task.WhenAll(fillTask, parseTask, processTask);
    }

    public static async Task RunCanAsync(UdpClient udpClient, CanTransport transport, CancellationToken cancellationToken = default)
    {
        transport.FrameReceived += (sender, frame) =>
        {
            TerminalLayout.WriteRx($"Rx [CAN] => " +
                $"TID: {frame.TransferId}, " +
                $"Src: {frame.SourceNodeId}, " +
                $"Subj: {frame.DataSpecifierId}, " +
                $"Name: {frame.Message?.Name ?? "Unknown"}");
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync();
                var data = result.Buffer;
                
                // Simulated CAN over UDP: [4 bytes CAN ID][Payload]
                if (data.Length >= 4)
                {
                    uint canId = BitConverter.ToUInt32(data, 0);
                    byte[] payload = new byte[data.Length - 4];
                    Array.Copy(data, 4, payload, 0, payload.Length);
                    
                    // Pass to transport for processing/reassembly
                    transport.ProcessRawFrame(canId, payload);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TerminalLayout.WriteRx($"Rx Error: {ex.Message}");
            }
        }
    }

    private static async Task FillPipeAsync(UdpClient udpClient, PipeWriter writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync();
                await writer.WriteAsync(result.Buffer, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TerminalLayout.WriteRx($"FillPipe Error: {ex.Message}");
            }
        }
        await writer.CompleteAsync();
    }

    private static async Task ParsePipeAsync(PipeReader reader, ChannelWriter<IFrame> writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ReadResult result;
            try
            {
                result = await reader.ReadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            ReadOnlySequence<byte> buffer = result.Buffer;

            while (true)
            {
                var frame = new UdpFrame();
                
                // Use streaming TryParse
                if (frame.TryParse(buffer, out SequencePosition consumed, out SequencePosition examined))
                {
                    await writer.WriteAsync(frame, ct);
                    buffer = buffer.Slice(consumed);
                }
                else
                {
                    // For UDP, we consume the whole packet if it fails or succeeds,
                    // but since TryParse handles streams, if it returns false it means "not enough data"
                    reader.AdvanceTo(consumed, examined);
                    break;
                }
            }

            if (result.IsCompleted) break;
        }
        writer.Complete();
    }

    private static async Task ProcessChannelAsync(ChannelReader<IFrame> reader, CancellationToken ct)
    {
        await foreach (var frame in reader.ReadAllAsync(ct))
        {
            TerminalLayout.WriteRx($"Rx [UDP] => " +
                $"TID: {frame.TransferId}, " +
                $"Src: {frame.SourceNodeId}, " +
                $"Subj: {frame.DataSpecifierId}, " +
                $"Name: {Cyphal.RegisteredMessages[frame.DataSpecifierId].Name}");
        }
    }

    // Keep the old Run method for compatibility if needed, but redirected to the async one
    public static void Run(UdpClient udpClient)
    {
        RunAsync(udpClient).GetAwaiter().GetResult();
    }
}
