using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestNoDrop
{
    static async Task Main()
    {
        try
        {
            // Same but without drop
            var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
            {
                Capacity = 3,  // Increased capacity so no drop
                BackpressureStrategy = BackpressureStrategy.Drop,
                EnableCustomBackpressure = true
            });

            // Act - Fill channel
            Console.WriteLine("Write 1...");
            var r1 = await channel.WriteAsync(1);
            Console.WriteLine($"Result: {r1}");

            Console.WriteLine("Write 2...");
            var r2 = await channel.WriteAsync(2);
            Console.WriteLine($"Result: {r2}");

            Console.WriteLine("Write 3 (should succeed)...");
            var r3 = await channel.WriteAsync(3);
            Console.WriteLine($"Result: {r3}");

            // Read from channel
            Console.WriteLine("Reading...");
            var readTask = channel.ReadAsync();
            if (await Task.WhenAny(readTask.AsTask(), Task.Delay(1000)) == readTask.AsTask())
            {
                var (success1, item1) = await readTask;
                Console.WriteLine($"Read success: {success1}, item: {item1}");
            }
            else
            {
                Console.WriteLine("READ TIMED OUT!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TEST FAILED: {ex.Message}");
        }
    }
}