using CyphalSharp;
using System.Net;
using System.Net.Sockets;

namespace CyphalConsole;

static class Transmitter
{
    private static readonly Random random = new();
    private static ulong transferIdCounter = 0;

    public static void RunCan(UdpClient udpClient, IPEndPoint remoteEndPoint, CanTransport transport)
    {
        // Filter to only messages that fit in a single CAN FD frame (63 bytes payload max)
        var messageIds = Cyphal.RegisteredMessages
            .Where(kvp => kvp.Value.PayloadLength <= 63)
            .Select(kvp => kvp.Key)
            .ToList();

        if (!messageIds.Any())
        {
            TerminalLayout.WriteTx("Tx: No Cyphal messages fit in single CAN frame. Exiting Tx thread.");
            return;
        }

        transport.RawFrameSent += (sender, rawFrame) =>
        {
            try
            {
                byte[] packet = new byte[4 + rawFrame.Payload.Length];
                BitConverter.GetBytes(rawFrame.CanId).CopyTo(packet, 0);
                rawFrame.Payload.CopyTo(packet, 4);
                udpClient.Send(packet, packet.Length, remoteEndPoint);
            }
            catch (Exception ex)
            {
                TerminalLayout.WriteTx($"Tx Send Error: {ex.Message}");
            }
        };

        while (true)
        {
            try
            {
                // Select a random message ID (Subject ID)
                uint randomMessageId = messageIds[random.Next(messageIds.Count)];
                if (Cyphal.RegisteredMessages.TryGetValue(randomMessageId, out var message))
                {
                    var frame = new CanFrame
                    {
                        Priority = 3,
                        SourceNodeId = (ushort)random.Next(1, 100),
                        DataSpecifierId = (ushort)message.PortId,
                        TransferId = transferIdCounter++,
                        Message = message
                    };

                    frame.SetFields(GenerateFieldValues(message, random));
                    
                    // Trigger RawFrameSent via transport
                    transport.SendAsync(frame).GetAwaiter().GetResult();

                    TerminalLayout.WriteTx($"Tx [CAN] => " +
                        $"TID: {frame.TransferId}, " +
                        $"Src: {frame.SourceNodeId}, " +
                        $"Subj: {message.PortId}, " +
                        $"Name: {message.Name}");
                }
            }
            catch (Exception ex)
            {
                TerminalLayout.WriteTx($"Tx Loop Error: {ex.Message}");
            }
            
            Thread.Sleep(500); 
        }
    }

    public static void Run(UdpClient udpClient, IPEndPoint remoteEndPoint)
    {
        var messageIds = Cyphal.RegisteredMessages.Keys.ToList();
        if (!messageIds.Any())
        {
            TerminalLayout.WriteTx("Tx: No Cyphal messages found in DSDL directory. Exiting Tx thread.");
            return;
        }

        while (true)
        {
            try
            {
                // Select a random message ID (Subject ID)
                uint randomMessageId = messageIds[random.Next(messageIds.Count)];

                if (Cyphal.RegisteredMessages.TryGetValue(randomMessageId, out var message))
                {
                    ushort sourceNodeId = (ushort)random.Next(1, 100);
                    
                    var fieldValues = GenerateFieldValues(message, random);

                    var frame = new UdpFrame
                    {
                        Version = 0,
                        Priority = 3,
                        SourceNodeId = sourceNodeId,
                        DestinationNodeId = 0xFFFF, // Broadcast
                        DataSpecifierId = (ushort)message.PortId,
                        TransferId = transferIdCounter++,
                        FrameIndex = 0,
                        EndOfTransfer = true,
                        Message = message
                    };

                    frame.SetFields(fieldValues);

                    byte[] packet = frame.ToBytes();

                    udpClient.Send(packet, packet.Length, remoteEndPoint);

                    TerminalLayout.WriteTx($"Tx => " +
                        $"TID: {frame.TransferId}, " +
                        $"Src: {frame.SourceNodeId}, " +
                        $"Subj: {message.PortId}, " +
                        $"Name: {message.Name}");
                }
            }
            catch (Exception ex)
            {
                TerminalLayout.WriteTx($"Tx UDP Error: {ex.Message}");
            }

            Thread.Sleep(500); 
        }
    }

    static Dictionary<string, object> GenerateFieldValues(Message messageDefinition, Random random)
    {
        var values = new Dictionary<string, object>();
        foreach (var field in messageDefinition.Fields)
        {
            values[field.Name] = GenerateRandomValue(field, random);
        }
        return values;
    }

    static object GenerateRandomValue(Field field, Random random)
    {
        if (field.DataType.IsArray)
        {
            // Handle char arrays (strings)
            if (field.ElementType == typeof(char))
            {
                char[] charArray = new char[field.ArrayLength];
                for (int i = 0; i < field.ArrayLength; i++)
                {
                    charArray[i] = (char)random.Next(32, 127); // Printable ASCII characters
                }
                return charArray;
            }
            else // Other array types
            {
                Array array = Array.CreateInstance(field.ElementType, field.ArrayLength);
                for (int i = 0; i < field.ArrayLength; i++)
                {
                    array.SetValue(GenerateSingleRandomValue(field.ElementType, random), i);
                }
                return array;
            }
        }
        else
        {
            return GenerateSingleRandomValue(field.DataType, random);
        }
    }

    static object GenerateSingleRandomValue(Type type, Random random)
    {
        if (type == typeof(byte)) return (byte)random.Next(256);
        if (type == typeof(sbyte)) return (sbyte)random.Next(-128, 128);
        if (type == typeof(ushort)) return (ushort)random.Next(65536);
        if (type == typeof(short)) return (short)random.Next(-32768, 32768);
        if (type == typeof(uint)) return (uint)random.Next();
        if (type == typeof(int)) return random.Next();
        if (type == typeof(ulong)) return (ulong)(random.NextDouble() * ulong.MaxValue);
        if (type == typeof(long)) return (long)(random.NextDouble() * long.MaxValue * (random.Next(2) == 0 ? 1 : -1));
        if (type == typeof(float)) return (float)(random.NextDouble() * 1000.0f);
        if (type == typeof(double)) return random.NextDouble() * 1000.0;
        if (type == typeof(char)) return (char)random.Next('a', 'z' + 1);

        throw new InvalidOperationException($"Unsupported type for random generation: {type.FullName}");
    }
}
