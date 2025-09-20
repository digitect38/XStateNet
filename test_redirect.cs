using System;
using System.Threading.Tasks;

class TestRedirect
{
    static async Task Main()
    {
        var redirectChannel = new XStateNet.Distributed.Channels.BoundedChannelManager<object>("redirect",
            new XStateNet.Distributed.Channels.CustomBoundedChannelOptions { Capacity = 10 });

        var options = new XStateNet.Distributed.Channels.CustomBoundedChannelOptions
        {
            Capacity = 2,
            BackpressureStrategy = XStateNet.Distributed.Channels.BackpressureStrategy.Redirect,
            OverflowChannel = redirectChannel,
            EnableCustomBackpressure = true
        };
        var channel = new XStateNet.Distributed.Channels.BoundedChannelManager<int>("main", options);

        Console.WriteLine("Writing 1...");
        await channel.WriteAsync(1);
        Console.WriteLine("Wrote 1");

        Console.WriteLine("Writing 2...");
        await channel.WriteAsync(2);
        Console.WriteLine("Wrote 2");

        Console.WriteLine("Writing 3 (should redirect)...");
        var writeTask = channel.WriteAsync(3);

        // Wait with timeout
        if (await Task.WhenAny(writeTask.AsTask(), Task.Delay(2000)) == writeTask.AsTask())
        {
            Console.WriteLine("Wrote 3 (redirected)");

            // Check redirect channel
            var (success, item) = await redirectChannel.ReadAsync();
            Console.WriteLine($"Read from redirect channel: success={success}, item={item}");
        }
        else
        {
            Console.WriteLine("WriteAsync(3) timed out!");
        }
    }
}