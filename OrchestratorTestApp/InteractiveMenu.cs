using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;

namespace OrchestratorTestApp
{
    public static class InteractiveMenu
    {
        private static EventBusOrchestrator? _orchestrator;
        private static readonly Dictionary<string, IPureStateMachine> _machines = new();

        public static async Task Run()
        {
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = true,
                PoolSize = 4
            });

            while (true)
            {
                Console.WriteLine("\n================================================");
                Console.WriteLine("   Orchestrator Interactive Test Menu");
                Console.WriteLine("================================================");
                Console.WriteLine("1. Run All Tests");
                Console.WriteLine("2. Create Machine");
                Console.WriteLine("3. Send Event");
                Console.WriteLine("4. Show Statistics");
                Console.WriteLine("5. Deadlock Demo");
                Console.WriteLine("6. Performance Demo");
                Console.WriteLine("7. Workflow Demo");
                Console.WriteLine("8. Clear Machines");
                Console.WriteLine("9. 🔥 HARSH TESTS - Extreme conditions");
                Console.WriteLine("10. 🔍 Monitoring Demo - Real-time observability");
                Console.WriteLine("11. 🏎️  Performance Benchmarks - Comprehensive suite");
                Console.WriteLine("12. ⚡ Quick Benchmark - Fast performance check");
                Console.WriteLine("13. 🎯 Latency Benchmark - Low-latency focused");
                Console.WriteLine("14. 🚀 Throughput Benchmark - High-throughput focused");
                Console.WriteLine("0. Exit");
                Console.Write("\nSelect option: ");

                var choice = Console.ReadLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await TestRunner.RunAllTests();
                            break;
                        case "2":
                            await CreateMachine();
                            break;
                        case "3":
                            await SendEvent();
                            break;
                        case "4":
                            ShowStatistics();
                            break;
                        case "5":
                            await DeadlockDemo();
                            break;
                        case "6":
                            await PerformanceDemo();
                            break;
                        case "7":
                            await WorkflowDemo();
                            break;
                        case "8":
                            ClearMachines();
                            break;
                        case "9":
                            await HarshTests.RunHarshTests();
                            break;
                        case "10":
                            await MonitoringDemo.Run();
                            break;
                        case "11":
                            await BenchmarkRunner.RunFullBenchmarkSuite();
                            break;
                        case "12":
                            await BenchmarkRunner.RunQuickBenchmark();
                            break;
                        case "13":
                            await BenchmarkRunner.RunLatencyFocusedBenchmark();
                            break;
                        case "14":
                            await BenchmarkRunner.RunThroughputFocusedBenchmark();
                            break;
                        case "0":
                            Console.WriteLine("Exiting...");
                            _orchestrator?.Dispose();
                            return;
                        default:
                            Console.WriteLine("Invalid option");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private static async Task CreateMachine()
        {
            Console.Write("Enter machine ID: ");
            var id = Console.ReadLine() ?? "";

            if (string.IsNullOrEmpty(id))
            {
                Console.WriteLine("Invalid ID");
                return;
            }

            if (_machines.ContainsKey(id))
            {
                Console.WriteLine("Machine already exists");
                return;
            }

            Console.WriteLine("Select machine type:");
            Console.WriteLine("1. Simple (idle/running states)");
            Console.WriteLine("2. Counter (counts events)");
            Console.WriteLine("3. Ping-Pong (responds with pong)");
            Console.WriteLine("4. Custom JSON");
            Console.Write("Choice: ");

            var typeChoice = Console.ReadLine();
            IPureStateMachine? machine = null;

            switch (typeChoice)
            {
                case "1":
                    machine = PureStateMachineFactory.CreateSimple(id);
                    break;

                case "2":
                    machine = CreateCounterMachine(id);
                    break;

                case "3":
                    machine = CreatePingPongMachine(id);
                    break;

                case "4":
                    Console.WriteLine("Enter JSON (or press Enter for default):");
                    var json = Console.ReadLine();
                    if (string.IsNullOrEmpty(json))
                    {
                        json = GetDefaultJson(id);
                    }
                    var underlying = StateMachineFactory.CreateFromScript(json, false, false);
                    machine = new PureStateMachineAdapter(id, underlying);
                    break;

                default:
                    Console.WriteLine("Invalid choice");
                    return;
            }

            if (machine != null)
            {
                _machines[id] = machine;
                _orchestrator!.RegisterMachine(id, (machine as PureStateMachineAdapter)?.GetUnderlying());
                await machine.StartAsync();
                Console.WriteLine($"✅ Machine '{id}' created and started");
            }
        }

        private static async Task SendEvent()
        {
            if (!_machines.Any())
            {
                Console.WriteLine("No machines available");
                return;
            }

            Console.WriteLine("Available machines:");
            foreach (var id in _machines.Keys)
            {
                Console.WriteLine($"  - {id}");
            }

            Console.Write("From machine (or 'external'): ");
            var from = Console.ReadLine() ?? "external";

            Console.Write("To machine: ");
            var to = Console.ReadLine() ?? "";

            if (!_machines.ContainsKey(to))
            {
                Console.WriteLine("Target machine not found");
                return;
            }

            Console.Write("Event name: ");
            var eventName = Console.ReadLine() ?? "";

            Console.Write("Timeout (ms, default 5000): ");
            var timeoutStr = Console.ReadLine();
            var timeout = string.IsNullOrEmpty(timeoutStr) ? 5000 : int.Parse(timeoutStr);

            Console.WriteLine($"\nSending {eventName} from {from} to {to}...");

            var result = await _orchestrator!.SendEventAsync(from, to, eventName, null, timeout);

            if (result.Success)
            {
                Console.WriteLine($"✅ Success! New state: {result.NewState}");
                Console.WriteLine($"   Event ID: {result.EventId}");
                Console.WriteLine($"   Processed by: {result.ProcessedBy}");
            }
            else
            {
                Console.WriteLine($"❌ Failed: {result.ErrorMessage}");
            }
        }

        private static void ShowStatistics()
        {
            var stats = _orchestrator!.GetStats();

            Console.WriteLine("\n=== Orchestrator Statistics ===");
            Console.WriteLine($"Registered Machines: {stats.RegisteredMachines}");
            Console.WriteLine($"Pending Requests: {stats.PendingRequests}");
            Console.WriteLine($"Running: {stats.IsRunning}");

            Console.WriteLine("\nEvent Bus Statistics:");
            foreach (var bus in stats.EventBusStats)
            {
                Console.WriteLine($"  Bus {bus.BusIndex}: {bus.TotalProcessed} processed, {bus.QueuedEvents} queued");
            }

            Console.WriteLine($"\nActive Machines ({_machines.Count}):");
            foreach (var (id, machine) in _machines)
            {
                Console.WriteLine($"  - {id}: {machine.CurrentState}");
            }
        }

        private static async Task DeadlockDemo()
        {
            Console.WriteLine("\n=== Deadlock Prevention Demo ===");
            Console.WriteLine("Creating two machines that send to each other...\n");

            // Create ping-pong actions
            var m1Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["pingBack"] = (ctx) =>
                {
                    Console.WriteLine("  M1: Sending PING to M2");
                    ctx.RequestSend("demo_m2", "PING");
                }
            };

            var m2Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["pingBack"] = (ctx) =>
                {
                    Console.WriteLine("  M2: Sending PING to M1");
                    ctx.RequestSend("demo_m1", "PING");
                }
            };

            var pingPongJson = @"{
                ""id"": ""pingpong"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": { ""START"": ""pinging"" }
                    },
                    ""pinging"": {
                        ""entry"": [""pingBack""],
                        ""on"": { ""PING"": ""pinged"" }
                    },
                    ""pinged"": {}
                }
            }";

            var m1 = PureStateMachineFactory.CreateFromScript("demo_m1", pingPongJson, _orchestrator!, m1Actions);
            var m2 = PureStateMachineFactory.CreateFromScript("demo_m2", pingPongJson, _orchestrator!, m2Actions);

            _orchestrator!.RegisterMachine("demo_m1", (m1 as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator!.RegisterMachine("demo_m2", (m2 as PureStateMachineAdapter)?.GetUnderlying());

            await m1.StartAsync();
            await m2.StartAsync();

            Console.WriteLine("Sending START to both machines simultaneously...");
            Console.WriteLine("In traditional architecture, this would DEADLOCK!");
            Console.WriteLine("With orchestrator, no deadlock occurs.\n");

            var task1 = _orchestrator!.SendEventAsync("demo", "demo_m1", "START");
            var task2 = _orchestrator!.SendEventAsync("demo", "demo_m2", "START");

            var results = await Task.WhenAll(task1, task2);

            if (results.All(r => r.Success))
            {
                Console.WriteLine("\n✅ Both machines completed successfully!");
                Console.WriteLine("   NO DEADLOCK - Orchestrator prevents circular wait");
            }
            else
            {
                Console.WriteLine("\n❌ Some operations failed");
            }

            // Cleanup
            _machines.Remove("demo_m1");
            _machines.Remove("demo_m2");
        }

        private static async Task PerformanceDemo()
        {
            Console.WriteLine("\n=== Performance Demo ===");
            Console.Write("Number of events to send (default 1000): ");
            var countStr = Console.ReadLine();
            var count = string.IsNullOrEmpty(countStr) ? 1000 : int.Parse(countStr);

            // Create performance test machine
            var machine = PureStateMachineFactory.CreateSimple("perf_test");
            _orchestrator!.RegisterMachine("perf_test", (machine as PureStateMachineAdapter)?.GetUnderlying());
            await machine.StartAsync();

            Console.WriteLine($"\nSending {count} events...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, count)
                .Select(i => _orchestrator!.SendEventAsync($"sender{i}", "perf_test", "START"))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            var successCount = results.Count(r => r.Success);
            var throughput = count * 1000.0 / stopwatch.ElapsedMilliseconds;

            Console.WriteLine($"\n✅ Performance Test Complete");
            Console.WriteLine($"   Events sent: {count}");
            Console.WriteLine($"   Successful: {successCount}");
            Console.WriteLine($"   Failed: {count - successCount}");
            Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"   Throughput: {throughput:F2} events/sec");

            // Show bus distribution
            var stats = _orchestrator!.GetStats();
            Console.WriteLine($"\nEvent Bus Distribution:");
            foreach (var bus in stats.EventBusStats)
            {
                Console.WriteLine($"   Bus {bus.BusIndex}: {bus.TotalProcessed} events");
            }

            // Cleanup
            _machines.Remove("perf_test");
        }

        private static async Task WorkflowDemo()
        {
            Console.WriteLine("\n=== Workflow Demo (Order Processing) ===");
            Console.WriteLine("Creating order processing workflow...\n");

            // Create workflow actions
            var orderActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["validateOrder"] = (ctx) =>
                {
                    Console.WriteLine("  📋 Validating order...");
                    ctx.RequestSend("payment", "PROCESS_PAYMENT");
                },
                ["prepareShipment"] = (ctx) =>
                {
                    Console.WriteLine("  📦 Preparing shipment...");
                    ctx.RequestSend("shipping", "SHIP");
                }
            };

            var paymentActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["processPayment"] = (ctx) =>
                {
                    Console.WriteLine("  💳 Processing payment...");
                    System.Threading.Thread.Sleep(500); // Simulate processing
                    ctx.RequestSend("order", "PAYMENT_COMPLETE");
                }
            };

            var shippingActions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["ship"] = (ctx) =>
                {
                    Console.WriteLine("  🚚 Shipping order...");
                    System.Threading.Thread.Sleep(300); // Simulate processing
                    ctx.RequestSend("order", "SHIPPED");
                }
            };

            // Create state machines
            var orderJson = @"{
                ""id"": ""order"",
                ""initial"": ""new"",
                ""states"": {
                    ""new"": {
                        ""on"": { ""SUBMIT"": ""validating"" }
                    },
                    ""validating"": {
                        ""entry"": [""validateOrder""],
                        ""on"": { ""PAYMENT_COMPLETE"": ""paid"" }
                    },
                    ""paid"": {
                        ""entry"": [""prepareShipment""],
                        ""on"": { ""SHIPPED"": ""complete"" }
                    },
                    ""complete"": {}
                }
            }";

            var paymentJson = @"{
                ""id"": ""payment"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": { ""PROCESS_PAYMENT"": ""processing"" }
                    },
                    ""processing"": {
                        ""entry"": [""processPayment""],
                        ""on"": { ""DONE"": ""complete"" }
                    },
                    ""complete"": {}
                }
            }";

            var shippingJson = @"{
                ""id"": ""shipping"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": { ""SHIP"": ""shipping"" }
                    },
                    ""shipping"": {
                        ""entry"": [""ship""],
                        ""on"": { ""DONE"": ""shipped"" }
                    },
                    ""shipped"": {}
                }
            }";

            var orderMachine = PureStateMachineFactory.CreateFromScript("order", orderJson, _orchestrator!, orderActions);
            var paymentMachine = PureStateMachineFactory.CreateFromScript("payment", paymentJson, _orchestrator!, paymentActions);
            var shippingMachine = PureStateMachineFactory.CreateFromScript("shipping", shippingJson, _orchestrator!, shippingActions);

            _orchestrator!.RegisterMachine("order", (orderMachine as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator!.RegisterMachine("payment", (paymentMachine as PureStateMachineAdapter)?.GetUnderlying());
            _orchestrator!.RegisterMachine("shipping", (shippingMachine as PureStateMachineAdapter)?.GetUnderlying());

            await orderMachine.StartAsync();
            await paymentMachine.StartAsync();
            await shippingMachine.StartAsync();

            Console.WriteLine("Submitting order...\n");

            var result = await _orchestrator!.SendEventAsync("customer", "order", "SUBMIT");

            if (result.Success)
            {
                Console.WriteLine($"\n✅ Order submitted successfully!");

                // Wait for workflow to complete
                await Task.Delay(2000);

                Console.WriteLine("\nWorkflow states:");
                Console.WriteLine($"  Order: {orderMachine.CurrentState}");
                Console.WriteLine($"  Payment: {paymentMachine.CurrentState}");
                Console.WriteLine($"  Shipping: {shippingMachine.CurrentState}");
            }
            else
            {
                Console.WriteLine($"\n❌ Order submission failed: {result.ErrorMessage}");
            }

            // Cleanup
            _machines.Remove("order");
            _machines.Remove("payment");
            _machines.Remove("shipping");
        }

        private static void ClearMachines()
        {
            foreach (var machine in _machines.Values)
            {
                machine.Stop();
            }
            _machines.Clear();

            Console.WriteLine("✅ All machines cleared");
        }

        private static IPureStateMachine CreateCounterMachine(string id)
        {
            var json = $@"{{
                ""id"": ""{id}"",
                ""initial"": ""zero"",
                ""states"": {{
                    ""zero"": {{
                        ""on"": {{ ""INC"": ""one"", ""RESET"": ""zero"" }}
                    }},
                    ""one"": {{
                        ""on"": {{ ""INC"": ""two"", ""RESET"": ""zero"" }}
                    }},
                    ""two"": {{
                        ""on"": {{ ""INC"": ""many"", ""RESET"": ""zero"" }}
                    }},
                    ""many"": {{
                        ""on"": {{ ""INC"": ""many"", ""RESET"": ""zero"" }}
                    }}
                }}
            }}";

            var machine = StateMachineFactory.CreateFromScript(json, false, false);
            return new PureStateMachineAdapter(id, machine);
        }

        private static IPureStateMachine CreatePingPongMachine(string id)
        {
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["sendPong"] = (ctx) =>
                {
                    Console.WriteLine($"  {id}: PONG!");
                    // In real scenario, would send PONG back to sender
                }
            };

            var json = $@"{{
                ""id"": ""{id}"",
                ""initial"": ""idle"",
                ""states"": {{
                    ""idle"": {{
                        ""on"": {{ ""PING"": ""ponging"" }}
                    }},
                    ""ponging"": {{
                        ""entry"": [""sendPong""],
                        ""on"": {{ ""RESET"": ""idle"" }}
                    }}
                }}
            }}";

            return PureStateMachineFactory.CreateFromScript(id, json, _orchestrator!, actions);
        }

        private static string GetDefaultJson(string id)
        {
            return $@"{{
                ""id"": ""{id}"",
                ""initial"": ""idle"",
                ""states"": {{
                    ""idle"": {{
                        ""on"": {{
                            ""START"": ""active"",
                            ""PING"": ""ponging""
                        }}
                    }},
                    ""active"": {{
                        ""on"": {{
                            ""STOP"": ""idle"",
                            ""PAUSE"": ""paused""
                        }}
                    }},
                    ""paused"": {{
                        ""on"": {{
                            ""RESUME"": ""active"",
                            ""STOP"": ""idle""
                        }}
                    }},
                    ""ponging"": {{
                        ""on"": {{
                            ""DONE"": ""idle""
                        }}
                    }}
                }}
            }}";
        }
    }
}