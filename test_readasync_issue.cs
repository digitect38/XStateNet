using System;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestReadAsyncIssue
{
    static async Task Main()
    {
        Console.WriteLine("Creating channel WITH custom backpressure...");
        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            EnableCustomBackpressure = true,
            BackpressureStrategy = BackpressureStrategy.Drop,
            FullMode = ChannelFullMode.Wait
        });

        Console.WriteLine("Writing 1 and 2...");
        await channel.WriteAsync(1);
        await channel.WriteAsync(2);
        await channel.WriteAsync(3);  // Dropped

        var stats = channel.GetStatistics();
        Console.WriteLine($"Stats: Depth={stats.CurrentDepth}, Written={stats.TotalItemsWritten}, Dropped={stats.TotalItemsDropped}");

        // Try synchronous read first
        Console.WriteLine("\nTrying TryRead...");
        if (channel.TryRead(out var item))
        {
            Console.WriteLine($"TryRead succeeded: {item}");
        }
        else
        {
            Console.WriteLine("TryRead failed!");
        }

        // Now try async read with cancellation
        Console.WriteLine("\nTrying ReadAsync with timeout...");
        using var cts = new CancellationTokenSource(1000);
        try
        {
            var (success, value) = await channel.ReadAsync(cts.Token);
            Console.WriteLine($"ReadAsync succeeded: success={success}, value={value}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("ReadAsync timed out!");
        }

        Console.WriteLine("\nDone!");
    }
}