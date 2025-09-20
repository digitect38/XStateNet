using System;

class TestUltraSimple
{
    static void Main()
    {
        Console.WriteLine("Starting program...");
        Console.Out.Flush();

        try
        {
            Console.WriteLine("About to create options...");
            var options = new XStateNet.Distributed.Channels.CustomBoundedChannelOptions
            {
                Capacity = 2
            };
            Console.WriteLine($"Options created with capacity: {options.Capacity}");

            Console.WriteLine("About to create channel...");
            var channel = new XStateNet.Distributed.Channels.BoundedChannelManager<int>("test", options);
            Console.WriteLine("Channel created successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
        }

        Console.WriteLine("Program finished");
    }
}