using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestCheckDepth
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

        var stats0 = channel.GetStatistics();
        Console.WriteLine($"Initial: Depth={stats0.CurrentDepth}, Written={stats0.TotalItemsWritten}");

        Console.WriteLine("Writing 1...");
        var r1 = await channel.WriteAsync(1);
        var stats1 = channel.GetStatistics();
        Console.WriteLine($"After write 1: result={r1}, Depth={stats1.CurrentDepth}, Written={stats1.TotalItemsWritten}");

        Console.WriteLine("Writing 2...");
        var r2 = await channel.WriteAsync(2);
        var stats2 = channel.GetStatistics();
        Console.WriteLine($"After write 2: result={r2}, Depth={stats2.CurrentDepth}, Written={stats2.TotalItemsWritten}");

        Console.WriteLine("Writing 3 (should be dropped)...");
        var r3 = await channel.WriteAsync(3);
        var stats3 = channel.GetStatistics();
        Console.WriteLine($"After write 3: result={r3}, Depth={stats3.CurrentDepth}, Written={stats3.TotalItemsWritten}, Dropped={stats3.TotalItemsDropped}");

        // Try to read synchronously to see if items are there
        Console.WriteLine("Trying TryRead...");
        if (channel.TryRead(out var item))
        {
            Console.WriteLine($"TryRead succeeded: {item}");
        }
        else
        {
            Console.WriteLine("TryRead failed - no items!");
        }

        Console.WriteLine("Done!");
    }
}