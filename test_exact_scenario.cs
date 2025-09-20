using System;
using System.Threading.Tasks;
using XStateNet.Distributed.Channels;

class TestExactScenario
{
    static async Task Main()
    {
        try
        {
            // Exact same configuration as the test
            var channel = new BoundedChannelManager<int>("test", new CustomBoundedChannelOptions
            {
                Capacity = 2,
                BackpressureStrategy = BackpressureStrategy.Drop,
                EnableCustomBackpressure = true  // Required for BackpressureStrategy to work
                // Note: Don't use DropNewest with custom Drop strategy - they conflict
            });

            // Act - Fill channel
            Console.WriteLine("Write 1...");
            var r1 = await channel.WriteAsync(1);
            Console.WriteLine($"Result: {r1}");
            if (!r1) throw new Exception("Expected true for write 1");

            Console.WriteLine("Write 2...");
            var r2 = await channel.WriteAsync(2);
            Console.WriteLine($"Result: {r2}");
            if (!r2) throw new Exception("Expected true for write 2");

            Console.WriteLine("Write 3 (should drop)...");
            var r3 = await channel.WriteAsync(3); // Dropped
            Console.WriteLine($"Result: {r3}");
            if (r3) throw new Exception("Expected false for write 3");

            // Read from channel
            Console.WriteLine("Reading...");
            var readTask = channel.ReadAsync();
            if (await Task.WhenAny(readTask.AsTask(), Task.Delay(1000)) == readTask.AsTask())
            {
                var (success1, item1) = await readTask;
                Console.WriteLine($"Read success: {success1}, item: {item1}");
                if (!success1) throw new Exception("Expected read to succeed");
                if (item1 != 1) throw new Exception($"Expected 1 but got {item1}");
            }
            else
            {
                Console.WriteLine("READ TIMED OUT! This is the bug!");
                return;
            }

            // Can write again after reading
            Console.WriteLine("Write 4 after reading...");
            var r4 = await channel.WriteAsync(4);
            Console.WriteLine($"Result: {r4}");
            if (!r4) throw new Exception("Expected true for write 4");

            Console.WriteLine("TEST PASSED!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TEST FAILED: {ex.Message}");
        }
    }
}