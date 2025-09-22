using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using XStateNet.GPU;

namespace XStateNet.GPU.Examples
{
    /// <summary>
    /// Example demonstrating how to execute actions with GPU-accelerated state machines
    /// </summary>
    public class ActionExample
    {
        private static ConcurrentBag<string> _actionLog = new ConcurrentBag<string>();

        public static async Task RunWithActions()
        {
            Console.WriteLine("=== GPU XStateNet with Actions Example ===");
            Console.WriteLine("Demonstrating action execution from GPU state transitions\n");

            // Define state machine with actions
            string machineWithActions = @"{
                ""id"": ""MachineWithActions"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": {
                                ""target"": ""processing"",
                                ""action"": ""onStart""
                            }
                        }
                    },
                    ""processing"": {
                        ""entry"": ""onEnterProcessing"",
                        ""on"": {
                            ""PROGRESS"": {
                                ""target"": ""processing"",
                                ""action"": ""onProgress""
                            },
                            ""COMPLETE"": {
                                ""target"": ""completed"",
                                ""action"": ""onComplete""
                            },
                            ""ERROR"": {
                                ""target"": ""failed"",
                                ""action"": ""onError""
                            }
                        },
                        ""exit"": ""onExitProcessing""
                    },
                    ""completed"": {
                        ""entry"": ""onSuccess"",
                        ""on"": {
                            ""RESET"": {
                                ""target"": ""idle"",
                                ""action"": ""onReset""
                            }
                        }
                    },
                    ""failed"": {
                        ""entry"": ""onFailure"",
                        ""on"": {
                            ""RETRY"": {
                                ""target"": ""processing"",
                                ""action"": ""onRetry""
                            },
                            ""RESET"": {
                                ""target"": ""idle"",
                                ""action"": ""onReset""
                            }
                        }
                    }
                }
            }";

            // Create GPU-accelerated state machine
            using var gpuMachine = new GPUAcceleratedStateMachine(machineWithActions);

            // Register action handlers
            gpuMachine.RegisterAction("onStart", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Started processing");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onProgress", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Progress update");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onComplete", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Completed successfully");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onError", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Error occurred");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onRetry", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Retrying");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onReset", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Reset to idle");
                await Task.CompletedTask;
            });

            // Entry/Exit actions
            gpuMachine.RegisterAction("onEnterProcessing", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Entered processing state");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onExitProcessing", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] Exited processing state");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onSuccess", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] SUCCESS!");
                await Task.CompletedTask;
            });

            gpuMachine.RegisterAction("onFailure", async (context) =>
            {
                _actionLog.Add($"[Instance {((dynamic)context).InstanceId}] FAILURE!");
                await Task.CompletedTask;
            });

            // Create pool of 1000 instances (will use GPU)
            var pool = await gpuMachine.CreatePoolAsync(1000);

            Console.WriteLine($"Created {pool.Count} instances (GPU: {pool.IsGPUAccelerated})\n");

            // Simulate workflow
            Console.WriteLine("Phase 1: Starting all instances");
            await pool.BroadcastAsync("START");

            Console.WriteLine("Phase 2: Some progress updates");
            for (int i = 0; i < 100; i++)
            {
                await pool.SendAsync(i, "PROGRESS");
            }

            Console.WriteLine("Phase 3: Mixed outcomes");
            // 60% complete successfully
            for (int i = 0; i < 600; i++)
            {
                await pool.SendAsync(i, "COMPLETE");
            }

            // 30% fail
            for (int i = 600; i < 900; i++)
            {
                await pool.SendAsync(i, "ERROR");
            }

            // 10% still processing
            Console.WriteLine("Phase 4: Retry failures");
            for (int i = 600; i < 900; i++)
            {
                await pool.SendAsync(i, "RETRY");
            }

            // Complete remaining
            for (int i = 100; i < 1000; i++)
            {
                if (pool.GetState(i) == "processing")
                {
                    await pool.SendAsync(i, "COMPLETE");
                }
            }

            Console.WriteLine("Phase 5: Reset all to idle");
            await pool.BroadcastAsync("RESET");

            // Display action log summary
            Console.WriteLine($"\nAction Log Summary:");
            Console.WriteLine($"Total actions executed: {_actionLog.Count}");

            var actionCounts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var log in _actionLog)
            {
                var actionType = log.Substring(log.LastIndexOf(']') + 2);
                if (!actionCounts.ContainsKey(actionType))
                    actionCounts[actionType] = 0;
                actionCounts[actionType]++;
            }

            foreach (var kvp in actionCounts)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }

            // Verify final states
            int idleCount = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (pool.GetState(i) == "idle")
                    idleCount++;
            }
            Console.WriteLine($"\nFinal state check: {idleCount} instances in idle state");

            // Performance metrics
            var metrics = gpuMachine.GetMetrics();
            Console.WriteLine($"\nPerformance Metrics:");
            Console.WriteLine(metrics.ToString());
        }

        public static async Task RunHybridExecution()
        {
            Console.WriteLine("\n=== Hybrid CPU/GPU Action Execution ===");
            Console.WriteLine("Critical instances with complex actions on CPU");
            Console.WriteLine("Bulk instances with simple actions on GPU\n");

            string hybridMachine = @"{
                ""id"": ""HybridActionMachine"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {
                        ""on"": {
                            ""EXECUTE"": {
                                ""target"": ""running"",
                                ""action"": ""executeTask""
                            }
                        }
                    },
                    ""running"": {
                        ""on"": {
                            ""COMPLETE"": {
                                ""target"": ""done"",
                                ""action"": ""saveResults""
                            }
                        }
                    },
                    ""done"": {
                        ""on"": {
                            ""RESET"": ""ready""
                        }
                    }
                }
            }";

            using var machine = new GPUAcceleratedStateMachine(hybridMachine, gpuThreshold: 50);

            // Complex action for critical instances (would run on CPU)
            machine.RegisterAction("executeTask", async (context) =>
            {
                var ctx = (dynamic)context;
                if (ctx.InstanceId < 10) // Critical instances
                {
                    // Complex computation
                    await Task.Delay(10);
                    Console.WriteLine($"Critical instance {ctx.InstanceId}: Complex execution");
                }
                else
                {
                    // Simple logging for bulk instances
                    _actionLog.Add($"Instance {ctx.InstanceId}: Simple execution");
                }
            });

            machine.RegisterAction("saveResults", async (context) =>
            {
                var ctx = (dynamic)context;
                if (ctx.InstanceId < 10)
                {
                    Console.WriteLine($"Critical instance {ctx.InstanceId}: Saved to database");
                }
                await Task.CompletedTask;
            });

            // Create small pool (CPU) for critical instances
            var criticalPool = await machine.CreatePoolAsync(10);
            Console.WriteLine($"Critical pool: {criticalPool.Count} instances on CPU");

            // Create large pool (GPU) for bulk processing
            var bulkMachine = new GPUAcceleratedStateMachine(hybridMachine, gpuThreshold: 50);
            bulkMachine.RegisterAction("executeTask", async (context) =>
            {
                _actionLog.Add($"Bulk execution");
                await Task.CompletedTask;
            });
            bulkMachine.RegisterAction("saveResults", async (context) =>
            {
                _actionLog.Add($"Bulk save");
                await Task.CompletedTask;
            });

            var bulkPool = await bulkMachine.CreatePoolAsync(10000);
            Console.WriteLine($"Bulk pool: {bulkPool.Count} instances on GPU");

            // Execute workflow
            await criticalPool.BroadcastAsync("EXECUTE");
            await bulkPool.BroadcastAsync("EXECUTE");

            await Task.Delay(100); // Let critical instances complete

            await criticalPool.BroadcastAsync("COMPLETE");
            await bulkPool.BroadcastAsync("COMPLETE");

            Console.WriteLine($"\nExecution complete");
            Console.WriteLine($"Actions logged: {_actionLog.Count}");

            bulkMachine.Dispose();
        }
    }
}