using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestSimpleWrite
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

        Console.WriteLine("Done!");
    }
}