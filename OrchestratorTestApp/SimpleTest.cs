using System;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;

namespace OrchestratorTestApp
{
    public static class SimpleTest
    {
        public static async Task Run()
        {
            Console.WriteLine("\n=== Simple Orchestrator Test ===\n");

            // Create orchestrator
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = true
            });

            // Create simple machines using JSON
            var machine1Json = @"{
                ""id"": ""m1"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""running"",
                            ""PING"": ""ponging""
                        }
                    },
                    ""running"": {
                        ""on"": { ""STOP"": ""idle"" }
                    },
                    ""ponging"": {
                        ""on"": { ""PONG_DONE"": ""idle"" }
                    }
                }
            }";

            var machine2Json = @"{
                ""id"": ""m2"",
                ""initial"": ""waiting"",
                ""states"": {
                    ""waiting"": {
                        ""on"": { ""PONG"": ""responded"" }
                    },
                    ""responded"": {
                        ""on"": { ""RESET"": ""waiting"" }
                    }
                }
            }";

            // Create state machines
            var machine1 = StateMachineFactory.CreateFromScript(machine1Json, false, false);
            var machine2 = StateMachineFactory.CreateFromScript(machine2Json, false, false);

            // Register with orchestrator
            orchestrator.RegisterMachine("m1", machine1);
            orchestrator.RegisterMachine("m2", machine2);

            // Start machines
            await machine1.StartAsync();
            await machine2.StartAsync();

            Console.WriteLine("\n1. Testing basic send from external source:");
            var result1 = await orchestrator.SendEventAsync("external", "m1", "START");
            Console.WriteLine($"   Result: {(result1.Success ? "SUCCESS" : "FAILED")} - New state: {result1.NewState}");

            Console.WriteLine("\n2. Testing bidirectional communication:");
            // M1 sends PING to itself
            var result2 = await orchestrator.SendEventAsync("m1", "m1", "PING");
            Console.WriteLine($"   M1 self-ping: {(result2.Success ? "SUCCESS" : "FAILED")} - New state: {result2.NewState}");

            // Now M1 could send PONG to M2 (simulated)
            var result3 = await orchestrator.SendEventAsync("m1", "m2", "PONG");
            Console.WriteLine($"   M1 -> M2 PONG: {(result3.Success ? "SUCCESS" : "FAILED")} - New state: {result3.NewState}");

            Console.WriteLine("\n3. Testing fire-and-forget:");
            await orchestrator.SendEventFireAndForgetAsync("external", "m1", "PONG_DONE");
            await Task.Delay(50); // Give it time to process
            Console.WriteLine($"   Fire-and-forget sent (no response expected)");

            Console.WriteLine("\n4. Testing timeout:");
            // Create a slow machine
            var slowMachineJson = @"{
                ""id"": ""slow"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": { ""SLOW"": ""processing"" }
                    },
                    ""processing"": {
                        ""after"": { ""2000"": ""done"" }
                    },
                    ""done"": {}
                }
            }";

            var slowMachine = StateMachineFactory.CreateFromScript(slowMachineJson, false, false);
            orchestrator.RegisterMachine("slow", slowMachine);
            await slowMachine.StartAsync();

            var result4 = await orchestrator.SendEventAsync("external", "slow", "SLOW", null, 100);
            Console.WriteLine($"   Timeout test: {(result4.Success ? "SUCCESS" : "SHOULD HAVE TIMED OUT")}");
            if (!result4.Success)
                Console.WriteLine($"   Error: {result4.ErrorMessage}");

            // Get statistics
            Console.WriteLine("\n5. Orchestrator Statistics:");
            var stats = orchestrator.GetStats();
            Console.WriteLine($"   Registered machines: {stats.RegisteredMachines}");
            Console.WriteLine($"   Pending requests: {stats.PendingRequests}");
            Console.WriteLine($"   Is running: {stats.IsRunning}");
            Console.WriteLine($"   Event buses:");
            foreach (var bus in stats.EventBusStats)
            {
                Console.WriteLine($"     Bus {bus.BusIndex}: {bus.TotalProcessed} events processed, {bus.QueuedEvents} queued");
            }

            Console.WriteLine("\n=== Test Complete ===");
        }
    }
}