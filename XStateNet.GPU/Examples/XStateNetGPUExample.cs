using System;
using System.IO;
using System.Threading.Tasks;
using XStateNet.GPU.Integration;

namespace XStateNet.GPU.Examples
{
    /// <summary>
    /// Example showing how to run thousands of XStateNet machines on GPU
    /// </summary>
    public class XStateNetGPUExample
    {
        private static int GetOrDefault(System.Collections.Generic.Dictionary<string, int> dict, string key)
        {
            return dict.ContainsKey(key) ? dict[key] : 0;
        }
        public static async Task RunTrafficLightSimulation()
        {
            Console.WriteLine("=== XStateNet GPU Traffic Light Simulation ===");
            Console.WriteLine("Running 10,000 traffic lights in parallel on GPU");
            Console.WriteLine();

            // Use actual XStateNet traffic light definition
            string trafficLightJson = @"{
                ""id"": ""TrafficLight"",
                ""initial"": ""red"",
                ""states"": {
                    ""red"": {
                        ""on"": {
                            ""TIMER"": ""yellow""
                        }
                    },
                    ""yellow"": {
                        ""on"": {
                            ""TIMER"": ""green""
                        }
                    },
                    ""green"": {
                        ""on"": {
                            ""TIMER"": ""red""
                        }
                    }
                }
            }";

            using var bridge = new XStateNetGPUBridge(trafficLightJson);

            // Initialize 10,000 traffic lights on GPU
            await bridge.InitializeAsync(10_000);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Simulate 100 timer ticks
            for (int tick = 0; tick < 100; tick++)
            {
                // All traffic lights receive timer event
                await bridge.BroadcastAsync("TIMER");
                await bridge.ProcessEventsAsync();

                if (tick % 10 == 0)
                {
                    var distribution = await bridge.GetStateDistributionAsync();
                    Console.WriteLine($"Tick {tick}: Red={GetOrDefault(distribution, "red")}, " +
                                    $"Yellow={GetOrDefault(distribution, "yellow")}, " +
                                    $"Green={GetOrDefault(distribution, "green")}");
                }
            }

            sw.Stop();

            Console.WriteLine($"\nProcessed 1,000,000 events in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Throughput: {1_000_000 * 1000.0 / sw.ElapsedMilliseconds:N0} events/sec");

            // Validate consistency with CPU implementation
            bool isConsistent = await bridge.ValidateConsistencyAsync();
            Console.WriteLine($"GPU/CPU Consistency Check: {(isConsistent ? "PASSED" : "FAILED")}");
        }

        public static async Task RunE40ProcessJobSimulation()
        {
            Console.WriteLine("\n=== XStateNet GPU E40 Process Job Simulation ===");
            Console.WriteLine("Running 5,000 SEMI E40 Process Jobs on GPU");
            Console.WriteLine();

            // Load actual E40 Process Job definition
            string e40Json = File.ReadAllText(@"SemiStandard\XStateScripts\E40ProcessJob.json");

            using var bridge = new XStateNetGPUBridge(e40Json);

            // Initialize 5,000 process jobs on GPU
            await bridge.InitializeAsync(5_000);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Simulate job lifecycle
            Console.WriteLine("Phase 1: Creating jobs...");
            await bridge.BroadcastAsync("CREATE");
            await bridge.ProcessEventsAsync();

            Console.WriteLine("Phase 2: Setting up jobs...");
            await bridge.BroadcastAsync("SETUP");
            await bridge.ProcessEventsAsync();

            Console.WriteLine("Phase 3: Starting jobs...");
            await bridge.BroadcastAsync("START");
            await bridge.ProcessEventsAsync();

            // Simulate some jobs completing, some aborting
            Console.WriteLine("Phase 4: Mixed outcomes...");
            for (int i = 0; i < 2500; i++)
            {
                bridge.Send(i, "COMPLETE");
            }
            for (int i = 2500; i < 3500; i++)
            {
                bridge.Send(i, "ABORT");
            }
            for (int i = 3500; i < 5000; i++)
            {
                bridge.Send(i, "ERROR");
            }
            await bridge.ProcessEventsAsync();

            sw.Stop();

            var distribution = await bridge.GetStateDistributionAsync();
            Console.WriteLine("\nFinal state distribution:");
            foreach (var kvp in distribution)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value} ({kvp.Value * 100.0 / 5000:F1}%)");
            }

            Console.WriteLine($"\nSimulation completed in {sw.ElapsedMilliseconds}ms");
        }

        public static async Task RunHybridSimulation()
        {
            Console.WriteLine("\n=== Hybrid CPU/GPU Execution ===");
            Console.WriteLine("Critical instances on CPU, bulk on GPU");
            Console.WriteLine();

            string machineJson = @"{
                ""id"": ""HybridMachine"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""running"",
                            ""PRIORITY"": ""critical""
                        }
                    },
                    ""running"": {
                        ""on"": {
                            ""PAUSE"": ""paused"",
                            ""COMPLETE"": ""done"",
                            ""ERROR"": ""failed""
                        }
                    },
                    ""paused"": {
                        ""on"": {
                            ""RESUME"": ""running"",
                            ""STOP"": ""idle""
                        }
                    },
                    ""critical"": {
                        ""on"": {
                            ""HANDLE"": ""running"",
                            ""ESCALATE"": ""failed""
                        }
                    },
                    ""done"": {
                        ""on"": {
                            ""RESET"": ""idle""
                        }
                    },
                    ""failed"": {
                        ""on"": {
                            ""RETRY"": ""running"",
                            ""RESET"": ""idle""
                        }
                    }
                }
            }";

            using var bridge = new XStateNetGPUBridge(machineJson);
            await bridge.InitializeAsync(100_000);

            // Mark first 100 as critical (would run on CPU in production)
            for (int i = 0; i < 100; i++)
            {
                bridge.Send(i, "PRIORITY");
            }

            // Rest run normal workflow
            for (int i = 100; i < 100_000; i++)
            {
                bridge.Send(i, "START");
            }

            await bridge.ProcessEventsAsync();

            // Simulate various events
            var random = new Random();
            for (int round = 0; round < 10; round++)
            {
                for (int i = 0; i < 100_000; i++)
                {
                    var evt = random.Next(6) switch
                    {
                        0 => "PAUSE",
                        1 => "RESUME",
                        2 => "COMPLETE",
                        3 => "ERROR",
                        4 => "RETRY",
                        _ => "HANDLE"
                    };

                    bridge.Send(i, evt);
                }

                await bridge.ProcessEventsAsync();
            }

            var distribution = await bridge.GetStateDistributionAsync();
            Console.WriteLine("Final distribution across 100,000 instances:");
            foreach (var kvp in distribution)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value:N0}");
            }
        }
    }
}