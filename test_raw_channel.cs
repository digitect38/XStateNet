using System;
using System.Threading.Channels;
using System.Threading.Tasks;

class TestRawChannel
{
    static async Task Main()
    {
        Console.WriteLine("Creating raw .NET channel...");
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropNewest
        });

        Console.WriteLine("Write 1...");
        await channel.Writer.WriteAsync(1);
        Console.WriteLine("Write 1 done");

        Console.WriteLine("Write 2...");
        await channel.Writer.WriteAsync(2);
        Console.WriteLine("Write 2 done");

        Console.WriteLine("Write 3...");
        await channel.Writer.WriteAsync(3);
        Console.WriteLine("Write 3 done");

        Console.WriteLine($"Channel has items: {channel.Reader.Count}");

        Console.WriteLine("Reading...");
        var item = await channel.Reader.ReadAsync();
        Console.WriteLine($"Read: {item}");

        Console.WriteLine("TEST PASSED!");
    }
}