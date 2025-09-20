using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestDropNewestOnly
{
    static async Task Main()
    {
        Console.WriteLine("Creating channel with DropNewest only...");
        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            FullMode = ChannelFullMode.DropNewest
        });

        Console.WriteLine("Write 1...");
        var r1 = await channel.WriteAsync(1);
        Console.WriteLine($"Result: {r1}");

        Console.WriteLine("Write 2...");
        var r2 = await channel.WriteAsync(2);
        Console.WriteLine($"Result: {r2}");

        Console.WriteLine("Write 3 (should succeed with drop)...");
        var r3 = await channel.WriteAsync(3);
        Console.WriteLine($"Result: {r3}");

        Console.WriteLine("Reading...");
        var readTask = channel.ReadAsync();
        if (await Task.WhenAny(readTask.AsTask(), Task.Delay(1000)) == readTask.AsTask())
        {
            var (success, item) = await readTask;
            Console.WriteLine($"Read success: {success}, item: {item}");
        }
        else
        {
            Console.WriteLine("READ TIMED OUT!");
        }
    }
}