using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestThreeWrites
{
    static async Task Main()
    {
        Console.WriteLine("Creating channel...");
        var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
        {
            Capacity = 2,
            FullMode = ChannelFullMode.DropNewest
        });

        Console.WriteLine("\nWriting 1...");
        await channel.WriteAsync(1);
        Console.WriteLine("Write 1 completed\n");

        Console.WriteLine("Writing 2...");
        await channel.WriteAsync(2);
        Console.WriteLine("Write 2 completed\n");

        Console.WriteLine("Writing 3 (should drop)...");
        await channel.WriteAsync(3);
        Console.WriteLine("Write 3 completed\n");

        Console.WriteLine("All writes done!");
    }
}