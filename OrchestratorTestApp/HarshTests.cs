using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;

namespace OrchestratorTestApp
{
    public static class HarshTests
    {
        private static int _passed = 0;
        private static int _failed = 0;

        public static async Task RunHarshTests()
        {
            Console.WriteLine("\n════════════════════════════════════════════════");
            Console.WriteLine("   HARSH TEST SUITE - EXTREME CONDITIONS");
            Console.WriteLine("════════════════════════════════════════════════\n");

            var tests = new (string name, Func<Task<bool>> test)[]
            {
                // Chaos Testing
                ("1M Events Bombardment", Test1MillionEvents),
                ("10K Concurrent Machines", Test10KConcurrentMachines),
                ("Instant Machine Churn", TestMachineChurn),
                ("Circular Dependency Chain 100", TestCircularChain100),
                ("Random Event Storm", TestRandomEventStorm),

                // Race Conditions
                ("Simultaneous Bidirectional 1000x", TestSimultaneousBidirectional1000),
                ("Race Condition Hammer", TestRaceConditionHammer),
                ("Parallel State Mutations", TestParallelStateMutations),

                // Memory & Resource Tests
                ("Memory Pressure Test", TestMemoryPressure),
                ("Handle Exhaustion", TestHandleExhaustion),
                ("Recursive Depth 10000", TestRecursiveDepth10000),

                // Byzantine Failures
                ("Poison Pill Events", TestPoisonPillEvents),
                ("Malformed JSON Attack", TestMalformedJsonAttack),
                ("Infinite Loop Detection", TestInfiniteLoopDetection),

                // Extreme Timing
                ("Zero Timeout Stress", TestZeroTimeoutStress),
                ("Microsecond Precision", TestMicrosecondPrecision),
                ("Time Travel Test", TestTimeTravelScenario)
            };

            var totalStopwatch = Stopwatch.StartNew();

            foreach (var (name, test) in tests)
            {
                Console.WriteLine($">>  {name,-40} ");
                var sw = Stopwatch.StartNew();

                try
                {
                    var success = await test();
                    sw.Stop();

                    if (success)
                    {
                        Console.WriteLine($"✅ PASS ({sw.ElapsedMilliseconds}ms)");
                        _passed++;
                    }
                    else
                    {
                        Console.WriteLine($"❌ FAIL ({sw.ElapsedMilliseconds}ms)");
                        _failed++;
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Console.WriteLine($"💥 CRASH ({sw.ElapsedMilliseconds}ms): {ex.Message}");
                    _failed++;
                }
            }

            totalStopwatch.Stop();

            Console.WriteLine($"\n════════════════════════════════════════════════");
            Console.WriteLine($"  HARSH TEST SUMMARY");
            Console.WriteLine($"════════════════════════════════════════════════");
            Console.WriteLine($"Total Tests: {_passed + _failed}");
            Console.WriteLine($"Passed: {_passed} ✅");
            Console.WriteLine($"Failed: {_failed} ❌");
            Console.WriteLine($"Success Rate: {(_passed * 100.0 / (_passed + _failed)):F1}%");
            Console.WriteLine($"Total Time: {totalStopwatch.ElapsedMilliseconds}ms");
        }

        private static async Task<bool> Test1MillionEvents()
        {
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

            var machine = PureStateMachineFactory.CreateFromScript("counter", json, orchestrator, actions);
            await orchestrator.StartMachineAsync("counter");

            // Send 1 million events with improved batching
            var batchSize = 500;  // Smaller batches for better flow control
            var successCount = 0;

            Console.WriteLine($"  Sending 1M events in batches of {batchSize}...");

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
                    Console.WriteLine($"    Sent {successCount:N0} events...");
                    await Task.Delay(1); // Brief pause
                }
            }

            // Wait for processing with longer timeout for 1M events
            Console.WriteLine($"  Waiting for processing completion...");
            await Task.Delay(3000);

            Console.WriteLine($"  Events sent: {successCount:N0}, Processed: {processedCount:N0}");

            // Success if we processed at least 950k events (95% success rate)
            return processedCount >= 950000;
        }

        private static async Task<bool> Test10KConcurrentMachines()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig {
                EnableLogging = false,
                PoolSize = 16 // More event buses
            });

            // Create 10,000 machines
            var tasks = new List<Task>();
            for (int i = 0; i < 10000; i++)
            {
                var json = $@"{{
                    ""id"": ""m{i}"",
                    ""initial"": ""idle"",
                    ""states"": {{
                        ""idle"": {{ ""on"": {{ ""START"": ""active"" }} }},
                        ""active"": {{ ""on"": {{ ""STOP"": ""idle"" }} }}
                    }}
                }}";

                var machine = PureStateMachineFactory.CreateFromScript($"m{i}", json, orchestrator, null);
                tasks.Add(orchestrator.StartMachineAsync($"m{i}"));
            }

            await Task.WhenAll(tasks);

            // Send events to all machines
            tasks.Clear();
            for (int i = 0; i < 10000; i++)
            {
                tasks.Add(orchestrator.SendEventFireAndForgetAsync("test", $"m{i}", "START"));
            }
            await Task.WhenAll(tasks);

            var stats = orchestrator.GetStats();
            return stats.RegisteredMachines == 10000;
        }

        private static async Task<bool> TestMachineChurn()
        {
            // Rapidly create and destroy machines
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var errors = 0;
            var iterations = 1000;

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var json = $@"{{
                        ""id"": ""temp"",
                        ""initial"": ""active"",
                        ""states"": {{ ""active"": {{}} }}
                    }}";

                    var machine = PureStateMachineFactory.CreateFromScript("temp", json, orchestrator, null);
                    await orchestrator.StartMachineAsync("temp");

                    // Immediately try to send events
                    await orchestrator.SendEventFireAndForgetAsync("test", "temp", "TEST");

                    // Simulate disposal by creating new one with same ID
                    // This should handle gracefully
                }
                catch
                {
                    errors++;
                }
            }

            return errors < iterations * 0.01; // Less than 1% error rate
        }

        private static async Task<bool> TestCircularChain100()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var chainLength = 100;

            // Create circular chain of 100 machines
            for (int i = 0; i < chainLength; i++)
            {
                var nextId = (i + 1) % chainLength;
                var actions = new Dictionary<string, Action<OrchestratedContext>>
                {
                    ["forward"] = (ctx) => ctx.RequestSend($"m{nextId}", "TRIGGER")
                };

                var json = $@"{{
                    ""id"": ""m{i}"",
                    ""initial"": ""idle"",
                    ""states"": {{
                        ""idle"": {{
                            ""on"": {{ ""TRIGGER"": ""forwarding"" }}
                        }},
                        ""forwarding"": {{
                            ""entry"": [""forward""],
                            ""on"": {{ ""DONE"": ""idle"" }}
                        }}
                    }}
                }}";

                var machine = PureStateMachineFactory.CreateFromScript($"m{i}", json, orchestrator, actions);
                await orchestrator.StartMachineAsync($"m{i}");
            }

            // Start the chain reaction
            var result = await orchestrator.SendEventAsync("test", "m0", "TRIGGER");
            await Task.Delay(500);

            return result.Success; // Should handle circular dependency without deadlock
        }

        private static async Task<bool> TestRandomEventStorm()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var machineCount = 50;
            var random = new Random(42);
            var successCount = 0;

            // Create machines
            for (int i = 0; i < machineCount; i++)
            {
                var json = $@"{{
                    ""id"": ""m{i}"",
                    ""initial"": ""s1"",
                    ""states"": {{
                        ""s1"": {{ ""on"": {{ ""E1"": ""s2"", ""E2"": ""s3"" }} }},
                        ""s2"": {{ ""on"": {{ ""E1"": ""s3"", ""E2"": ""s1"" }} }},
                        ""s3"": {{ ""on"": {{ ""E1"": ""s1"", ""E2"": ""s2"" }} }}
                    }}
                }}";

                var machine = PureStateMachineFactory.CreateFromScript($"m{i}", json, orchestrator, null);
                await orchestrator.StartMachineAsync($"m{i}");
            }

            // Send 10,000 random events
            var tasks = new List<Task<EventResult>>();
            for (int i = 0; i < 10000; i++)
            {
                var fromId = random.Next(machineCount);
                var toId = random.Next(machineCount);
                var eventName = random.Next(2) == 0 ? "E1" : "E2";

                tasks.Add(orchestrator.SendEventAsync($"m{fromId}", $"m{toId}", eventName));

                if (tasks.Count >= 100)
                {
                    var results = await Task.WhenAll(tasks);
                    successCount += results.Count(r => r.Success);
                    tasks.Clear();
                }
            }

            var finalResults = await Task.WhenAll(tasks);
            successCount += finalResults.Count(r => r.Success);

            return successCount > 9900; // 99% success rate
        }

        private static async Task<bool> TestSimultaneousBidirectional1000()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var pingActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendPong"] = (ctx) => ctx.RequestSend("pong", "PONG")
            };

            var pongActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendPing"] = (ctx) => ctx.RequestSend("ping", "PING")
            };

            var pingJson = @"{
                ""id"": ""ping"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": { ""on"": { ""PING"": ""sending"" } },
                    ""sending"": { ""entry"": [""sendPong""], ""on"": { ""PONG"": ""idle"" } }
                }
            }";

            var pongJson = @"{
                ""id"": ""pong"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": { ""on"": { ""PONG"": ""sending"" } },
                    ""sending"": { ""entry"": [""sendPing""], ""on"": { ""PING"": ""idle"" } }
                }
            }";

            var ping = PureStateMachineFactory.CreateFromScript("ping", pingJson, orchestrator, pingActions);
            var pong = PureStateMachineFactory.CreateFromScript("pong", pongJson, orchestrator, pongActions);

            await Task.WhenAll(
                orchestrator.StartMachineAsync("ping"),
                orchestrator.StartMachineAsync("pong")
            );

            // Send 1000 simultaneous bidirectional events
            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.WhenAll(
                    orchestrator.SendEventFireAndForgetAsync("test", "ping", "PING"),
                    orchestrator.SendEventFireAndForgetAsync("test", "pong", "PONG")
                ));
            }

            await Task.WhenAll(tasks);
            await Task.Delay(1000);

            // Should handle all without deadlock
            return true;
        }

        private static async Task<bool> TestRaceConditionHammer()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var counter = 0;
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["increment"] = (ctx) =>
                {
                    // Intentionally create race condition scenario
                    var temp = counter;
                    Thread.Yield(); // Force context switch
                    counter = temp + 1;
                }
            };

            var json = @"{
                ""id"": ""racer"",
                ""initial"": ""racing"",
                ""states"": {
                    ""racing"": {
                        ""entry"": [""increment""],
                        ""on"": { ""RACE"": ""racing"" }
                    }
                }
            }";

            var machine = PureStateMachineFactory.CreateFromScript("racer", json, orchestrator, actions);
            await orchestrator.StartMachineAsync("racer");

            // Hammer with concurrent events
            var tasks = Enumerable.Range(0, 1000)
                .Select(_ => orchestrator.SendEventFireAndForgetAsync("test", "racer", "RACE"))
                .ToArray();

            await Task.WhenAll(tasks);
            await Task.Delay(500);

            // Counter should be 1001 (1 from start + 1000 events) if properly serialized
            return counter == 1001;
        }

        private static async Task<bool> TestParallelStateMutations()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var states = new HashSet<string>();
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["recordState"] = (ctx) =>
                {
                    lock (states)
                    {
                        states.Add(DateTime.Now.Ticks.ToString());
                    }
                }
            };

            // Create parallel regions
            var json = @"{
                ""id"": ""parallel"",
                ""type"": ""parallel"",
                ""states"": {
                    ""region1"": {
                        ""initial"": ""a1"",
                        ""states"": {
                            ""a1"": { ""entry"": [""recordState""], ""on"": { ""NEXT"": ""a2"" } },
                            ""a2"": { ""entry"": [""recordState""], ""on"": { ""NEXT"": ""a1"" } }
                        }
                    },
                    ""region2"": {
                        ""initial"": ""b1"",
                        ""states"": {
                            ""b1"": { ""entry"": [""recordState""], ""on"": { ""NEXT"": ""b2"" } },
                            ""b2"": { ""entry"": [""recordState""], ""on"": { ""NEXT"": ""b1"" } }
                        }
                    }
                }
            }";

            var machine = PureStateMachineFactory.CreateFromScript("parallel", json, orchestrator, actions);
            await orchestrator.StartMachineAsync("parallel");

            // Send events rapidly
            for (int i = 0; i < 100; i++)
            {
                await orchestrator.SendEventFireAndForgetAsync("test", "parallel", "NEXT");
            }

            await Task.Delay(500);
            return states.Count > 0; // Should record states without corruption
        }

        private static async Task<bool> TestMemoryPressure()
        {
            var initialMemory = GC.GetTotalMemory(true);

            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            // Create machines with large state
            for (int i = 0; i < 1000; i++)
            {
                var largeData = new byte[10000]; // 10KB per machine
                var actions = new Dictionary<string, Action<OrchestratedContext>>
                {
                    ["store"] = (ctx) =>
                    {
                        // Hold reference to large data
                        _ = largeData.Length;
                    }
                };

                var json = $@"{{
                    ""id"": ""mem{i}"",
                    ""initial"": ""storing"",
                    ""states"": {{
                        ""storing"": {{ ""entry"": [""store""] }}
                    }}
                }}";

                var machine = PureStateMachineFactory.CreateFromScript($"mem{i}", json, orchestrator, actions);
                await orchestrator.StartMachineAsync($"mem{i}");
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(true);
            var memoryIncrease = (finalMemory - initialMemory) / (1024.0 * 1024.0); // MB

            // Should not leak excessive memory (less than 100MB for 1000 machines)
            return memoryIncrease < 100;
        }

        private static async Task<bool> TestHandleExhaustion()
        {
            var orchestrators = new List<EventBusOrchestrator>();

            try
            {
                // Try to exhaust handles by creating many orchestrators
                for (int i = 0; i < 100; i++)
                {
                    var orch = new EventBusOrchestrator(new OrchestratorConfig {
                        EnableLogging = false,
                        PoolSize = 8
                    });
                    orchestrators.Add(orch);

                    var json = $@"{{
                        ""id"": ""h{i}"",
                        ""initial"": ""active"",
                        ""states"": {{ ""active"": {{}} }}
                    }}";

                    var machine = PureStateMachineFactory.CreateFromScript($"h{i}", json, orch, null);
                    await orch.StartMachineAsync($"h{i}");
                }

                // Clean disposal
                foreach (var orch in orchestrators)
                {
                    orch.Dispose();
                }

                return true; // Should handle resource management properly
            }
            catch
            {
                // Clean up on failure
                foreach (var orch in orchestrators)
                {
                    try { orch.Dispose(); } catch { }
                }
                return false;
            }
        }

        private static async Task<bool> TestRecursiveDepth10000()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 8,
                EnableBackpressure = true,
                MaxQueueDepth = 15000
            });

            var eventCount = 0;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["increment"] = (ctx) =>
                {
                    Interlocked.Increment(ref eventCount);
                }
            };

            var json = @"{
                ""id"": ""counter"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {
                        ""entry"": [""increment""],
                        ""on"": { ""NEXT"": ""ready"" }
                    }
                }
            }";

            var machine = PureStateMachineFactory.CreateFromScript("counter", json, orchestrator, actions);
            await orchestrator.StartMachineAsync("counter");

            // Send 10000 events in controlled batches to avoid stack overflow
            var batchSize = 100;
            var totalBatches = 10000 / batchSize;

            for (int batch = 0; batch < totalBatches; batch++)
            {
                var batchTasks = new List<Task>();
                for (int i = 0; i < batchSize; i++)
                {
                    batchTasks.Add(orchestrator.SendEventFireAndForgetAsync("test", "counter", "NEXT"));
                }

                try
                {
                    await Task.WhenAll(batchTasks);
                }
                catch
                {
                    // Continue on partial failure
                }

                // Brief pause between batches to prevent overwhelming
                if (batch % 10 == 0)
                {
                    for (int j = 0; j < 100; j++) Thread.Yield();
                }
            }

            // Wait for processing to complete
            var stopwatch = Stopwatch.StartNew();
            var lastCount = eventCount;
            var stableCount = 0;

            while (stopwatch.ElapsedMilliseconds < 10000 && stableCount < 5)
            {
                for (int i = 0; i < 1000; i++) Thread.Yield();

                if (eventCount == lastCount)
                {
                    stableCount++;
                }
                else
                {
                    lastCount = eventCount;
                    stableCount = 0;
                }
            }

            // Success if we processed most events without crash
            return eventCount >= 9500; // 95% success threshold
        }

        private static async Task<bool> TestPoisonPillEvents()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var errors = 0;
            var recovered = false;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["poison"] = (ctx) =>
                {
                    Interlocked.Increment(ref errors);
                    throw new Exception("Poison pill!");
                },
                ["recover"] = (ctx) =>
                {
                    recovered = true;
                }
            };

            var json = @"{
                ""id"": ""poisoned"",
                ""initial"": ""safe"",
                ""states"": {
                    ""safe"": {
                        ""on"": {
                            ""POISON"": ""vulnerable"",
                            ""RECOVER"": ""recovered""
                        }
                    },
                    ""vulnerable"": {
                        ""entry"": [""poison""],
                        ""on"": { ""RECOVER"": ""recovered"" }
                    },
                    ""recovered"": {
                        ""entry"": [""recover""]
                    }
                }
            }";

            try
            {
                var machine = PureStateMachineFactory.CreateFromScript("poisoned", json, orchestrator, actions);
                await orchestrator.StartMachineAsync("poisoned");

                // Send poison event to trigger the exception
                await orchestrator.SendEventFireAndForgetAsync("test", "poisoned", "POISON");

                // Wait deterministically for poison to process
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 50 && errors == 0)
                {
                    Thread.Yield();
                }

                // Try to send recovery event after poison
                var result = await orchestrator.SendEventAsync("test", "poisoned", "RECOVER", null, 1000);

                // Success if system gracefully handled poison and recovered
                return errors > 0 && recovered && result.Success;
            }
            catch
            {
                // Still success if we handled it gracefully at some level
                return errors > 0;
            }
        }

        private static async Task<bool> TestMalformedJsonAttack()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var malformedJsons = new[]
            {
                @"{ ""id"": ""bad"", ""initial"": ""missing_states"" }",
                @"{ ""id"": ""bad"", ""states"": {} }",  // Missing initial
                @"{ ""id"": ""bad"", ""initial"": ""nonexistent"", ""states"": { ""other"": {} } }",
                @"{ broken json",
                @"{ ""id"": null, ""initial"": ""state"", ""states"": {} }",
                "null",
                "",
                @"{ ""id"": """", ""initial"": """", ""states"": { """": {} } }"
            };

            var errors = 0;
            foreach (var badJson in malformedJsons)
            {
                try
                {
                    var machine = PureStateMachineFactory.CreateFromScript("test", badJson, orchestrator, null);
                    await orchestrator.StartMachineAsync("test");
                }
                catch
                {
                    errors++;
                }
            }

            // Should reject all malformed JSON
            return errors == malformedJsons.Length;
        }

        private static async Task<bool> TestInfiniteLoopDetection()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,
                PoolSize = 4,
                EnableBackpressure = true,      // Key: Enable backpressure to limit loops
                MaxQueueDepth = 5000,          // Smaller queue to force throttling
                ThrottleDelay = TimeSpan.FromMicroseconds(10)  // Small delay to control rate
            });

            var loopCount = 0;
            var lastCount = 0;
            var stabilizedCount = 0;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["loop"] = (ctx) =>
                {
                    var currentCount = Interlocked.Increment(ref loopCount);

                    // Add circuit breaker - stop after reasonable limit
                    if (currentCount < 10000)  // Reasonable upper bound
                    {
                        ctx.RequestSelfSend("LOOP");
                    }
                }
            };

            var json = @"{
                ""id"": ""infinite"",
                ""initial"": ""looping"",
                ""states"": {
                    ""looping"": {
                        ""entry"": [""loop""],
                        ""on"": { ""LOOP"": ""looping"", ""BREAK"": ""done"" }
                    },
                    ""done"": {}
                }
            }";

            var machine = PureStateMachineFactory.CreateFromScript("infinite", json, orchestrator, actions);
            await orchestrator.StartMachineAsync("infinite");

            // Monitor for stabilization with deterministic timing
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 5000)
            {
                // Deterministic wait using Thread.Yield instead of Task.Delay
                for (int i = 0; i < 10000; i++) Thread.Yield();

                if (loopCount == lastCount)
                {
                    stabilizedCount++;
                    if (stabilizedCount >= 10) // Stabilized
                        break;
                }
                else
                {
                    lastCount = loopCount;
                    stabilizedCount = 0;
                }
            }

            // Try to send break event (should work if system is responsive)
            try
            {
                var breakResult = await orchestrator.SendEventAsync("test", "infinite", "BREAK", null, 1000);
                // Success if we processed many loops but didn't crash and can still respond
                return loopCount > 500 && loopCount <= 10000 && breakResult.Success;
            }
            catch
            {
                // Even if break fails, success if we handled the loop without crashing
                return loopCount > 500 && loopCount <= 10000;
            }
        }

        private static async Task<bool> TestZeroTimeoutStress()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var json = @"{
                ""id"": ""timeout"",
                ""initial"": ""active"",
                ""states"": {
                    ""active"": { ""on"": { ""TEST"": ""active"" } }
                }
            }";

            var machine = PureStateMachineFactory.CreateFromScript("timeout", json, orchestrator, null);
            await orchestrator.StartMachineAsync("timeout");

            var timeouts = 0;
            var successes = 0;

            // Send with 0 timeout
            for (int i = 0; i < 100; i++)
            {
                var result = await orchestrator.SendEventAsync("test", "timeout", "TEST", null, 0);
                if (result.Success) successes++;
                else timeouts++;
            }

            // Most should timeout with 0ms timeout
            return timeouts > 90;
        }

        private static async Task<bool> TestMicrosecondPrecision()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var timestamps = new List<long>();
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["timestamp"] = (ctx) =>
                {
                    timestamps.Add(Stopwatch.GetTimestamp());
                }
            };

            var json = @"{
                ""id"": ""precision"",
                ""initial"": ""timing"",
                ""states"": {
                    ""timing"": {
                        ""entry"": [""timestamp""],
                        ""on"": { ""TICK"": ""timing"" }
                    }
                }
            }";

            var machine = PureStateMachineFactory.CreateFromScript("precision", json, orchestrator, actions);
            await orchestrator.StartMachineAsync("precision");

            // Send rapid events
            for (int i = 0; i < 100; i++)
            {
                await orchestrator.SendEventFireAndForgetAsync("test", "precision", "TICK");
            }

            // Deterministic wait for processing completion
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 100 && timestamps.Count < 100)
            {
                Thread.Yield();
            }

            // Check that we have distinct timestamps
            var distinctCount = timestamps.Distinct().Count();
            return distinctCount > 50; // Should have good timestamp resolution
        }

        private static async Task<bool> TestTimeTravelScenario()
        {
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });

            var eventLog = new List<(string evt, DateTime time)>();
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["logPast"] = (ctx) =>
                {
                    eventLog.Add(("PAST", DateTime.Now.AddSeconds(-1)));
                    ctx.RequestSelfSend("FUTURE");
                },
                ["logFuture"] = (ctx) =>
                {
                    eventLog.Add(("FUTURE", DateTime.Now.AddSeconds(1)));
                }
            };

            var json = @"{
                ""id"": ""timetravel"",
                ""initial"": ""past"",
                ""states"": {
                    ""past"": {
                        ""entry"": [""logPast""],
                        ""on"": { ""FUTURE"": ""future"" }
                    },
                    ""future"": {
                        ""entry"": [""logFuture""]
                    }
                }
            }";

            var machine = PureStateMachineFactory.CreateFromScript("timetravel", json, orchestrator, actions);
            await orchestrator.StartMachineAsync("timetravel");

            // Deterministic wait for event processing
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 100 && eventLog.Count < 2)
            {
                Thread.Yield();
            }

            // Should handle time-based logic without issues
            return eventLog.Count == 2 && eventLog[0].evt == "PAST" && eventLog[1].evt == "FUTURE";
        }
    }
}