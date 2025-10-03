// Simple C# test to verify Named Pipe works
// Compile: csc SimpleNamedPipeTest.cs
// Run: SimpleNamedPipeTest.exe

using System;
using System.IO.Pipes;
using System.Threading.Tasks;

class SimpleNamedPipeTest
{
    static async Task Main()
    {
        Console.WriteLine("Simple Named Pipe Connection Test");
        Console.WriteLine("==================================");
        Console.WriteLine();

        var pipeName = "XStateNet.MessageBus";

        Console.WriteLine($"Attempting to connect to pipe: {pipeName}");
        Console.WriteLine("Press Ctrl+C to cancel...");
        Console.WriteLine();

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            Console.WriteLine("Created pipe client object");
            Console.WriteLine("Calling ConnectAsync(5000)...");

            await client.ConnectAsync(5000);

            Console.WriteLine("✓ SUCCESS! Connected to pipe!");
            Console.WriteLine($"  IsConnected: {client.IsConnected}");
            Console.WriteLine($"  CanRead: {client.CanRead}");
            Console.WriteLine($"  CanWrite: {client.CanWrite}");

            Console.WriteLine();
            Console.WriteLine("Press any key to disconnect...");
            Console.ReadKey();
        }
        catch (TimeoutException)
        {
            Console.WriteLine("✗ TIMEOUT: Could not connect within 5 seconds");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ ERROR: {ex.GetType().Name}");
            Console.WriteLine($"  Message: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Stack trace:");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
