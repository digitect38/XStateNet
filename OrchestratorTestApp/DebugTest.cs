using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;

namespace OrchestratorTestApp
{
    public static class DebugTest
    {
        public static async Task Run()
        {
            Console.WriteLine("\n=== Debug State Synchronization Test ===\n");

            try
            {
                using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
                {
                    EnableLogging = true
                });

                Console.WriteLine("1. Testing state synchronization...");

                // Create 3 machines that need to stay in sync
                var syncActions = new Dictionary<string, Action<OrchestratedContext>>
                {
                    ["broadcast"] = (ctx) =>
                    {
                        Console.WriteLine("   Broadcasting sync to other machines");
                        // Broadcast to other machines
                        ctx.RequestSend("sync2", "SYNC");
                        ctx.RequestSend("sync3", "SYNC");
                    }
                };

                // Create JSON for each machine with unique id
                var sync1Json = @"{
                    ""id"": ""sync1"",
                    ""initial"": ""state1"",
                    ""states"": {
                        ""state1"": {
                            ""on"": { ""CHANGE"": ""state2"" }
                        },
                        ""state2"": {
                            ""entry"": [""broadcast""],
                            ""on"": { ""SYNC"": ""state2"", ""CHANGE"": ""state3"" }
                        },
                        ""state3"": {
                            ""on"": { ""SYNC"": ""state3"" }
                        }
                    }
                }";

                var sync2Json = @"{
                    ""id"": ""sync2"",
                    ""initial"": ""state1"",
                    ""states"": {
                        ""state1"": {
                            ""on"": { ""CHANGE"": ""state2"" }
                        },
                        ""state2"": {
                            ""on"": { ""SYNC"": ""state2"", ""CHANGE"": ""state3"" }
                        },
                        ""state3"": {
                            ""on"": { ""SYNC"": ""state3"" }
                        }
                    }
                }";

                var sync3Json = @"{
                    ""id"": ""sync3"",
                    ""initial"": ""state1"",
                    ""states"": {
                        ""state1"": {
                            ""on"": { ""CHANGE"": ""state2"" }
                        },
                        ""state2"": {
                            ""on"": { ""SYNC"": ""state2"", ""CHANGE"": ""state3"" }
                        },
                        ""state3"": {
                            ""on"": { ""SYNC"": ""state3"" }
                        }
                    }
                }";

                Console.WriteLine("2. Creating sync1 machine...");
                var sync1 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: "sync1",
                    json: sync1Json,
                    orchestrator: orchestrator,
                    orchestratedActions: syncActions,
                    guards: null,
                    services: null,
                    delays: null,
                    activities: null);

                Console.WriteLine("3. Creating sync2 machine...");
                var sync2 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: "sync2",
                    json: sync2Json,
                    orchestrator: orchestrator,
                    orchestratedActions: null,
                    guards: null,
                    services: null,
                    delays: null,
                    activities: null);

                Console.WriteLine("4. Creating sync3 machine...");
                var sync3 = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: "sync3",
                    json: sync3Json,
                    orchestrator: orchestrator,
                    orchestratedActions: null,
                    guards: null,
                    services: null,
                    delays: null,
                    activities: null);

                Console.WriteLine("5. Starting all machines...");
                await Task.WhenAll(
                    orchestrator.StartMachineAsync("sync1"),
                    orchestrator.StartMachineAsync("sync2"),
                    orchestrator.StartMachineAsync("sync3"));

                Console.WriteLine("6. Sending CHANGE event to sync1...");
                // Change state on first machine
                var result = await orchestrator.SendEventAsync("test", "sync1", "CHANGE");
                await Task.Delay(100); // Allow sync to propagate

                if (!result.Success)
                {
                    Console.WriteLine($"\n❌ FAILURE: State change failed - {result.ErrorMessage}");
                }
                else
                {
                    Console.WriteLine("\n✅ SUCCESS: State synchronization completed!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERROR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\n=== Debug Test Complete ===");
        }
    }
}