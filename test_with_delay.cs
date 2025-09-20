using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestWithDelay
{
    static async Task Main()
    {
        Console.WriteLine("Creating channel...");
        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            FullMode = ChannelFullMode.DropNewest
        });

        Console.WriteLine("Write 1...");
        await channel.WriteAsync(1);
        Console.WriteLine("Write 1 done");

        Console.WriteLine("Write 2...");
        await channel.WriteAsync(2);
        Console.WriteLine("Write 2 done");

        Console.WriteLine("Write 3...");
        await channel.WriteAsync(3);
        Console.WriteLine("Write 3 done");

        // Add a small delay
        Console.WriteLine("Waiting 100ms...");
        await Task.Delay(100);

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