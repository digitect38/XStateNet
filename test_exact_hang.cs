using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestExactHang
{
    static async Task Main()
    {
        Console.WriteLine("Starting test...");

        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            BackpressureStrategy = BackpressureStrategy.Drop,
            EnableCustomBackpressure = true
        });

        await channel.WriteAsync(1);
        Console.WriteLine("Write 1 done");
        await channel.WriteAsync(2);
        Console.WriteLine("Write 2 done");
        await channel.WriteAsync(3);  // Should drop
        Console.WriteLine("Write 3 done");

        Console.WriteLine("About to create ReadAsync task...");
        var readTask = channel.ReadAsync();
        Console.WriteLine("ReadAsync task created, about to await with timeout...");

        if (await Task.WhenAny(readTask.AsTask(), Task.Delay(1000)) == readTask.AsTask())
        {
            Console.WriteLine("ReadAsync completed within timeout");
            var (success, item) = await readTask;
            Console.WriteLine($"Read result: success={success}, item={item}");
        }
        else
        {
            Console.WriteLine("ReadAsync TIMED OUT - This is the bug!");

            // Now try TryRead to see if items are there
            Console.WriteLine("Trying TryRead...");
            if (channel.TryRead(out var item))
            {
                Console.WriteLine($"TryRead succeeded: {item}");
                Console.WriteLine("So items ARE in the channel, but ReadAsync hangs!");
            }
            else
            {
                Console.WriteLine("TryRead also failed - channel is empty?");
            }
        }
    }
}