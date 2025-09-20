using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestThirdWrite
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
        var r3Task = channel.WriteAsync(3);
        if (await Task.WhenAny(r3Task.AsTask(), Task.Delay(1000)) == r3Task.AsTask())
        {
            var r3 = await r3Task;
            Console.WriteLine($"Write 3 result: {r3}");
        }
        else
        {
            Console.WriteLine("Write 3 timed out!");
        }

        Console.WriteLine("Done!");
    }
}