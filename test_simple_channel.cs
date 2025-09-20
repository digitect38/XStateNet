using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestSimpleChannel
{
    static async Task Main()
    {
        try
        {
            Console.WriteLine("Starting test...");
            Console.Out.Flush();

            var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
            {
                Capacity = 2,
                BackpressureStrategy = BackpressureStrategy.Drop,
                EnableCustomBackpressure = true,
                FullMode = ChannelFullMode.DropNewest
            });
            Console.WriteLine("Channel created");
            Console.Out.Flush();

            Console.WriteLine("About to write 1...");
            Console.Out.Flush();

            var writeTask = channel.WriteAsync(1);
            if (await Task.WhenAny(writeTask.AsTask(), Task.Delay(500)) == writeTask.AsTask())
            {
                var r1 = await writeTask;
                Console.WriteLine($"Write 1 result: {r1}");
            }
            else
            {
                Console.WriteLine("Write 1 timed out!");
            }
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
        }
    }
}