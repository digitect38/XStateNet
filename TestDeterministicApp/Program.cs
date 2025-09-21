using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet.Distributed.Testing;
using XStateNet.Distributed.EventBus.Optimized;

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

            // Subscribe with processor.EnqueueEventAsync pattern (like failing test)
            await eventBus.SubscribeToMachineAsync("machine1", (evt) =>
            {
                Console.WriteLine($"Handler invoked for event: {evt.EventName}");

                // This is what the failing test does - calls .Wait() on EnqueueEventAsync
                processor.EnqueueEventAsync(
                    $"machine1:{evt.EventName}",
                    evt.Payload,
                    () =>
                    {
                        Console.WriteLine($"Processing event: {evt.EventName}");
                        receivedEvents.Add(evt.EventName);
                        return Task.CompletedTask;
                    }).Wait();

                Console.WriteLine($"Handler completed for event: {evt.EventName}");
            });
            Console.WriteLine("Subscription created");

            // Publish event
            Console.WriteLine("Publishing EVENT1...");
            await eventBus.PublishEventAsync("machine1", "EVENT1");
            Console.WriteLine("EVENT1 published");

            // Process all queued events
            Console.WriteLine("Processing pending events...");
            await processor.ProcessAllPendingEventsAsync();
            Console.WriteLine("Processing completed");

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