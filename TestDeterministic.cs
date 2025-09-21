using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.Testing;

class TestDeterministic
{
    static async Task Main()
    {
        Console.WriteLine("Starting deterministic test...");

        using (DeterministicTestMode.Enable())
        {
            Console.WriteLine("Deterministic mode enabled");
            var processor = DeterministicTestMode.Processor;

            var eventBus = new OptimizedInMemoryEventBus();
            Console.WriteLine("Event bus created");

            await eventBus.ConnectAsync();
            Console.WriteLine("Event bus connected");

            var receivedEvents = new List<string>();

            // Subscribe without using processor inside handler
            await eventBus.SubscribeToMachineAsync("machine1", (evt) =>
            {
                Console.WriteLine($"Handler invoked for event: {evt.EventName}");
                receivedEvents.Add(evt.EventName);
            });
            Console.WriteLine("Subscription created");

            // Publish event
            Console.WriteLine("Publishing EVENT1...");
            await eventBus.PublishEventAsync("machine1", "EVENT1");
            Console.WriteLine("EVENT1 published");

            // Check results
            Console.WriteLine($"Received events count: {receivedEvents.Count}");
            foreach (var evt in receivedEvents)
            {
                Console.WriteLine($"  - {evt}");
            }
        }

        Console.WriteLine("Test completed");
    }
}