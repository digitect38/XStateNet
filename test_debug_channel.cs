using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestDebugChannel
{
    static async Task Main()
    {
        Console.WriteLine("Creating channel with capacity 2, Drop strategy...");
        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            BackpressureStrategy = BackpressureStrategy.Drop,
            EnableCustomBackpressure = true,
            FullMode = ChannelFullMode.DropNewest
        });

        Console.WriteLine("\nWriting 1...");
        var r1 = await channel.WriteAsync(1);
        Console.WriteLine($"Write 1 result: {r1}");
        var stats1 = channel.GetStatistics();
        Console.WriteLine($"After write 1 - Depth: {stats1.CurrentDepth}, Written: {stats1.TotalItemsWritten}, Dropped: {stats1.TotalItemsDropped}");

        Console.WriteLine("\nWriting 2...");
        var r2 = await channel.WriteAsync(2);
        Console.WriteLine($"Write 2 result: {r2}");
        var stats2 = channel.GetStatistics();
        Console.WriteLine($"After write 2 - Depth: {stats2.CurrentDepth}, Written: {stats2.TotalItemsWritten}, Dropped: {stats2.TotalItemsDropped}");

        Console.WriteLine("\nWriting 3 (should drop)...");
        var r3 = await channel.WriteAsync(3);
        Console.WriteLine($"Write 3 result: {r3}");
        var stats3 = channel.GetStatistics();
        Console.WriteLine($"After write 3 - Depth: {stats3.CurrentDepth}, Written: {stats3.TotalItemsWritten}, Dropped: {stats3.TotalItemsDropped}");

        Console.WriteLine("\nReading first item...");
        var readTask = channel.ReadAsync();
        if (await Task.WhenAny(readTask.AsTask(), Task.Delay(1000)) == readTask.AsTask())
        {
            var (success1, item1) = await readTask;
            Console.WriteLine($"Read result: success={success1}, item={item1}");
            var stats4 = channel.GetStatistics();
            Console.WriteLine($"After read - Depth: {stats4.CurrentDepth}, Written: {stats4.TotalItemsWritten}, Read: {stats4.TotalItemsRead}");
        }
        else
        {
            Console.WriteLine("Read timed out!");
        }

        Console.WriteLine("\nWriting 4 after reading...");
        var r4 = await channel.WriteAsync(4);
        Console.WriteLine($"Write 4 result: {r4}");
        var stats5 = channel.GetStatistics();
        Console.WriteLine($"After write 4 - Depth: {stats5.CurrentDepth}, Written: {stats5.TotalItemsWritten}");

        Console.WriteLine("\nTest completed successfully!");
    }
}