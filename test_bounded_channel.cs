using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestBoundedChannel
{
    static async Task Main()
    {
        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            BackpressureStrategy = BackpressureStrategy.Drop,
            EnableCustomBackpressure = true,
            FullMode = ChannelFullMode.DropNewest
        });

        Console.WriteLine("Writing 1...");
        var r1 = await channel.WriteAsync(1);
        Console.WriteLine($"Write 1: {r1}");

        Console.WriteLine("Writing 2...");
        var r2 = await channel.WriteAsync(2);
        Console.WriteLine($"Write 2: {r2}");

        Console.WriteLine("Writing 3 (should drop)...");
        var writeTask = channel.WriteAsync(3);

        if (await Task.WhenAny(writeTask.AsTask(), Task.Delay(1000)) == writeTask.AsTask())
        {
            var r3 = await writeTask;
            Console.WriteLine($"Write 3: {r3}");
        }
        else
        {
            Console.WriteLine("Write 3 timed out - hanging!");
        }

        // Check stats
        var stats = channel.GetStatistics();
        Console.WriteLine($"Written: {stats.TotalItemsWritten}, Dropped: {stats.TotalItemsDropped}");
    }
}