using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Orchestration;

namespace OrchestratorTestApp
{
    public static class Test1MEvents
    {
        public static async Task Run()
        {
            Console.WriteLine("🔥 Testing improved 1M event handling...\n");

            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 16,                    // More event buses for parallel processing
                EnableBackpressure = true,        // Use bounded channels with backpressure
                MaxQueueDepth = 50000,           // Large queue depth for high throughput
                ThrottleDelay = TimeSpan.FromMicroseconds(1)  // Minimal throttling
            });

            var processedCount = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["count"] = (ctx) => Interlocked.Increment(ref processedCount)
            };

            var json = @"{
                ""id"": ""counter"",
                ""initial"": ""active"",
                ""states"": {
                    ""active"": {
                        ""entry"": [""count""],
                        ""on"": { ""TICK"": ""active"" }
                    }
                }
            }";

            var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "counter",
                json: json,
                orchestrator: orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
            await orchestrator.StartMachineAsync("counter");

            // Send 1 million events with improved batching
            var batchSize = 500;  // Smaller batches for better flow control
            var successCount = 0;

            Console.WriteLine($"Sending 1M events in batches of {batchSize}...");

            var startTime = DateTime.Now;

            for (int batch = 0; batch < 1000000 / batchSize; batch++)
            {
                var batchTasks = new List<Task>();

                for (int i = 0; i < batchSize; i++)
                {
                    batchTasks.Add(orchestrator.SendEventFireAndForgetAsync("test", "counter", "TICK"));
                }

                try
                {
                    await Task.WhenAll(batchTasks);
                    successCount += batchSize;
                }
                catch
                {
                    // Count partial success in case of throttling
                    successCount += batchTasks.Count(t => t.IsCompletedSuccessfully);
                }

                // Progress indicator and brief pause to prevent overwhelming
                if (batch % 200 == 0)
                {
                    Console.WriteLine($"  Sent {successCount:N0} events... ({(DateTime.Now - startTime).TotalSeconds:F1}s elapsed)");
                    await Task.Delay(1); // Brief pause
                }
            }

            var sendTime = DateTime.Now - startTime;

            // Wait for processing with longer timeout for 1M events
            Console.WriteLine($"Waiting for processing completion...");
            await Task.Delay(3000);

            var totalTime = DateTime.Now - startTime;

            Console.WriteLine($"\n📊 Results:");
            Console.WriteLine($"Events sent: {successCount:N0}");
            Console.WriteLine($"Events processed: {processedCount:N0}");
            Console.WriteLine($"Send time: {sendTime.TotalSeconds:F1}s");
            Console.WriteLine($"Total time: {totalTime.TotalSeconds:F1}s");
            Console.WriteLine($"Send rate: {successCount / sendTime.TotalSeconds:N0} events/sec");
            Console.WriteLine($"Process rate: {processedCount / totalTime.TotalSeconds:N0} events/sec");

            // Success if we processed at least 950k events (95% success rate)
            if (processedCount >= 950000)
            {
                Console.WriteLine($"\n✅ SUCCESS! 1M event test passed with {processedCount:N0} events processed");
            }
            else
            {
                Console.WriteLine($"\n❌ FAILED! Expected 950K+ events, got {processedCount:N0}");
            }
        }
    }
}