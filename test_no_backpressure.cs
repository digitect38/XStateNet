using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestNoBackpressure
{
    static async Task Main()
    {
        Console.WriteLine("Creating channel WITHOUT custom backpressure...");
        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            // NO EnableCustomBackpressure
            FullMode = ChannelFullMode.Wait
        });

        Console.WriteLine("Writing 1...");
        var r1 = await channel.WriteAsync(1);
        Console.WriteLine($"Write 1 result: {r1}");

        Console.WriteLine("Writing 2...");
        var r2 = await channel.WriteAsync(2);
        Console.WriteLine($"Write 2 result: {r2}");

        Console.WriteLine("Done!");
    }
}