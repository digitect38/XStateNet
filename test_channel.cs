using System;
using System.Threading.Channels;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var options = new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.DropNewest
        };

        var channel = Channel.CreateBounded<int>(options);

        // Write 6 items to capacity-5 channel
        for (int i = 1; i <= 6; i++)
        {
            var result = channel.Writer.TryWrite(i);
            Console.WriteLine($"Write {i}: {result}");
        }

        // Check how many items are in the channel
        int count = 0;
        while (channel.Reader.TryRead(out var item))
        {
            Console.WriteLine($"Read: {item}");
            count++;
        }

        Console.WriteLine($"Total items in channel: {count}");
    }
}