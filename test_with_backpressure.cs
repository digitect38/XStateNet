using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestWithBackpressure
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
        var r1Task = channel.WriteAsync(1);
        if (await Task.WhenAny(r1Task.AsTask(), Task.Delay(1000)) == r1Task.AsTask())
        {
            var r1 = await r1Task;
            Console.WriteLine($"Write 1 result: {r1}");
        }
        else
        {
            Console.WriteLine("Write 1 timed out!");
            return;
        }

        Console.WriteLine("Writing 2...");
        var r2Task = channel.WriteAsync(2);
        if (await Task.WhenAny(r2Task.AsTask(), Task.Delay(1000)) == r2Task.AsTask())
        {
            var r2 = await r2Task;
            Console.WriteLine($"Write 2 result: {r2}");
        }
        else
        {
            Console.WriteLine("Write 2 timed out!");
            return;
        }

        Console.WriteLine("Done!");
    }
}