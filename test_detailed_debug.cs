using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestDetailedDebug
{
    static async Task Main()
    {
        Console.WriteLine("Starting test...");
        Console.Out.Flush();

        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            BackpressureStrategy = BackpressureStrategy.Drop,
            EnableCustomBackpressure = true
        });
        Console.WriteLine("Channel created");
        Console.Out.Flush();

        Console.WriteLine("About to WriteAsync(1)...");
        Console.Out.Flush();

        var writeTask1 = channel.WriteAsync(1);
        Console.WriteLine("WriteAsync(1) task created");
        Console.Out.Flush();

        if (await Task.WhenAny(writeTask1.AsTask(), Task.Delay(500)) == writeTask1.AsTask())
        {
            var r1 = await writeTask1;
            Console.WriteLine($"WriteAsync(1) completed: {r1}");
        }
        else
        {
            Console.WriteLine("WriteAsync(1) TIMED OUT!");
            return;
        }
        Console.Out.Flush();

        Console.WriteLine("About to WriteAsync(2)...");
        Console.Out.Flush();

        var writeTask2 = channel.WriteAsync(2);
        if (await Task.WhenAny(writeTask2.AsTask(), Task.Delay(500)) == writeTask2.AsTask())
        {
            var r2 = await writeTask2;
            Console.WriteLine($"WriteAsync(2) completed: {r2}");
        }
        else
        {
            Console.WriteLine("WriteAsync(2) TIMED OUT!");
            return;
        }
        Console.Out.Flush();

        Console.WriteLine("Test completed successfully");
    }
}