using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestReadAfterDrop
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

        Console.WriteLine("Writing 1...");
        var r1 = await channel.WriteAsync(1);
        Console.WriteLine($"Write 1 result: {r1}");

        Console.WriteLine("Writing 2...");
        var r2 = await channel.WriteAsync(2);
        Console.WriteLine($"Write 2 result: {r2}");

        Console.WriteLine("Writing 3 (should be dropped)...");
        var r3 = await channel.WriteAsync(3);
        Console.WriteLine($"Write 3 result: {r3}");

        Console.WriteLine("Reading first item...");
        var readTask = channel.ReadAsync();
        if (await Task.WhenAny(readTask.AsTask(), Task.Delay(1000)) == readTask.AsTask())
        {
            var (success1, item1) = await readTask;
            Console.WriteLine($"Read result: success={success1}, item={item1}");
        }
        else
        {
            Console.WriteLine("Read timed out!");
        }

        Console.WriteLine("Writing 4 after reading...");
        var r4 = await channel.WriteAsync(4);
        Console.WriteLine($"Write 4 result: {r4}");

        Console.WriteLine("Done!");
    }
}