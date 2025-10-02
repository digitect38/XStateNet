using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Debugging;
using XStateNet.Visualization;
using XStateNet.Profiling;
using XStateNet.Configuration;
using XStateNet.Orchestration;
using System.IO;

namespace OrchestratorTestApp
{
    /// <summary>
    /// Demonstrates advanced XStateNet features including debugging, visualization, profiling, and configuration management
    /// </summary>
    public static class AdvancedFeaturesDemo
    {
        public static async Task RunAsync()
        {
            Console.WriteLine("🚀 XStateNet Advanced Features Demo");
            Console.WriteLine("====================================");
            Console.WriteLine();

            // Create output directory for demos
            var outputDir = Path.Combine(Environment.CurrentDirectory, "AdvancedFeaturesOutput");
            Directory.CreateDirectory(outputDir);

            try
            {
                // 1. Configuration Management Demo
                await RunConfigurationDemo(outputDir);
                await Task.Delay(1000);

                // 2. State Machine Visualization Demo
                await RunVisualizationDemo(outputDir);
                await Task.Delay(1000);

                // 3. Performance Profiling Demo
                await RunProfilingDemo(outputDir);
                await Task.Delay(1000);

                // 4. Advanced Debugging Demo
                await RunDebuggingDemo(outputDir);
                await Task.Delay(1000);

                // 5. Integrated Advanced Features Demo
                await RunIntegratedDemo(outputDir);

                Console.WriteLine("\n🎉 All advanced features demos completed successfully!");
                Console.WriteLine($"📁 Output files created in: {outputDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Demo failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static async Task RunConfigurationDemo(string outputDir)
        {
            Console.WriteLine("⚙️ Configuration Management Demo");
            Console.WriteLine("=================================");

            var configManager = new ConfigurationManager(Path.Combine(outputDir, "config"));

            // Register configuration types
            configManager.RegisterConfiguration<DemoConfiguration>();
            configManager.RegisterConfiguration<AdvancedOrchestratorConfig>();

            // Load configurations
            var demoConfig = configManager.LoadConfiguration<DemoConfiguration>();
            var orchConfig = configManager.LoadConfiguration<AdvancedOrchestratorConfig>();

            Console.WriteLine($"📋 Demo Config - Name: {demoConfig.ApplicationName}, Debug: {demoConfig.EnableDebugMode}");
            Console.WriteLine($"🎯 Orchestrator Config - Pool Size: {orchConfig.PoolSize}, Max Queue: {orchConfig.MaxQueueDepth}");

            // Update configuration
            configManager.UpdateConfiguration<DemoConfiguration>(config =>
            {
                config.EnableDebugMode = true;
                config.LogLevel = "DEBUG";
            });

            // Create templates
            configManager.CreateTemplate<DemoConfiguration>();
            configManager.CreateTemplate<AdvancedOrchestratorConfig>();

            // Generate report
            var report = configManager.GenerateReport();
            Console.WriteLine($"📊 Configuration Report: {report.RegisteredTypes} types, {report.LoadedConfigurations} loaded");

            // Export configurations
            var exportPath = Path.Combine(outputDir, "config_export.json");
            configManager.ExportAllConfigurations(exportPath);

            Console.WriteLine("✅ Configuration management demo completed");
            Console.WriteLine();
        }

        private static async Task RunVisualizationDemo(string outputDir)
        {
            Console.WriteLine("📊 State Machine Visualization Demo");
            Console.WriteLine("====================================");

            // Create a sample state machine JSON
            var stateMachineJson = @"{
                ""id"": ""advancedOrderProcessor"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": { ""START_ORDER"": ""validating"" }
                    },
                    ""validating"": {
                        ""entry"": [""validateOrder""],
                        ""on"": {
                            ""VALIDATION_SUCCESS"": ""processing"",
                            ""VALIDATION_FAILED"": ""rejected""
                        }
                    },
                    ""processing"": {
                        ""type"": ""compound"",
                        ""initial"": ""checkingInventory"",
                        ""states"": {
                            ""checkingInventory"": {
                                ""entry"": [""checkInventory""],
                                ""on"": {
                                    ""INVENTORY_AVAILABLE"": ""processPayment"",
                                    ""INVENTORY_UNAVAILABLE"": ""#rejected""
                                }
                            },
                            ""processPayment"": {
                                ""entry"": [""processPayment""],
                                ""on"": {
                                    ""PAYMENT_SUCCESS"": ""#completed"",
                                    ""PAYMENT_FAILED"": ""#rejected""
                                }
                            }
                        }
                    },
                    ""completed"": {
                        ""type"": ""final"",
                        ""entry"": [""completeOrder""]
                    },
                    ""rejected"": {
                        ""type"": ""final"",
                        ""entry"": [""rejectOrder""]
                    }
                }
            }";

            var visualizationOptions = new VisualizationOptions
            {
                IncludeDescriptions = true,
                IncludeGuards = true,
                IncludeActions = true,
                UseColorCoding = true,
                Theme = "modern"
            };

            // Generate various visualization formats
            Console.WriteLine("🖼️  Generating visualizations...");

            var mermaidDiagram = StateMachineVisualizer.GenerateMermaidDiagram(stateMachineJson, visualizationOptions);
            var dotDiagram = StateMachineVisualizer.GenerateDotDiagram(stateMachineJson, visualizationOptions);
            var plantUmlDiagram = StateMachineVisualizer.GeneratePlantUmlDiagram(stateMachineJson, visualizationOptions);
            var htmlDiagram = StateMachineVisualizer.GenerateHtmlDiagram(stateMachineJson, visualizationOptions);

            // Export all visualization formats
            var vizOutputDir = Path.Combine(outputDir, "visualizations");
            StateMachineVisualizer.ExportVisualization(stateMachineJson, vizOutputDir, visualizationOptions);

            // Generate comprehensive report
            var vizReport = StateMachineVisualizer.GenerateVisualizationReport(stateMachineJson, visualizationOptions);
            Console.WriteLine($"📈 State Machine Analysis:");
            Console.WriteLine($"   Total States: {vizReport.Analysis.TotalStates}");
            Console.WriteLine($"   Total Transitions: {vizReport.Analysis.TotalTransitions}");
            Console.WriteLine($"   Complexity Score: {vizReport.Analysis.ComplexityScore}");
            Console.WriteLine($"   Unique Events: {vizReport.Analysis.Events.Count}");

            Console.WriteLine("✅ Visualization demo completed");
            Console.WriteLine();
        }

        private static async Task RunProfilingDemo(string outputDir)
        {
            Console.WriteLine("🔥 Performance Profiling Demo");
            Console.WriteLine("=============================");

            var profilerConfig = new ProfilingConfiguration
            {
                SlowMethodThresholdMs = 10.0,
                SlowEventThresholdMs = 5.0,
                MaxSamples = 10000,
                EnableMemoryTracking = true
            };

            using var profiler = new PerformanceProfiler(profilerConfig);

            // Set up performance alert handling
            profiler.PerformanceAlert += (sender, args) =>
            {
                Console.WriteLine($"⚠️ Performance Alert: {args.Alert.Message}");
            };

            Console.WriteLine("🎯 Running profiled operations...");

            // Profile various operations
            await SimulateProfiledOperations(profiler);

            // Generate performance report
            var report = profiler.GenerateReport();
            Console.WriteLine($"📊 Profiling Results:");
            Console.WriteLine($"   Total Samples: {report.Statistics.TotalSamples}");
            Console.WriteLine($"   Average Execution: {report.Statistics.AverageExecutionTime:F2}ms");
            Console.WriteLine($"   P95 Execution: {report.Statistics.P95ExecutionTime:F2}ms");
            Console.WriteLine($"   Performance Hotspots: {report.PerformanceHotspots.Count}");

            // Show top hotspots
            if (report.PerformanceHotspots.Count > 0)
            {
                Console.WriteLine("🔥 Top Performance Hotspots:");
                foreach (var hotspot in report.PerformanceHotspots.Take(3))
                {
                    Console.WriteLine($"   • {hotspot.Name}: {hotspot.AverageTime:F2}ms avg ({hotspot.CallCount} calls)");
                }
            }

            // Export profiling data
            var profilingOutputDir = Path.Combine(outputDir, "profiling");
            await profiler.ExportDataAsync(profilingOutputDir, ExportFormat.All);

            Console.WriteLine("✅ Profiling demo completed");
            Console.WriteLine();
        }

        private static async Task SimulateProfiledOperations(PerformanceProfiler profiler)
        {
            var random = new Random();

            // Simulate various operations with different performance characteristics
            for (int i = 0; i < 100; i++)
            {
                // Fast operation
                profiler.Profile("FastOperation", () =>
                {
                    Thread.Sleep(random.Next(1, 5));
                    return $"FastResult_{i}";
                });

                // Medium operation
                await profiler.ProfileAsync("MediumOperation", async () =>
                {
                    await Task.Delay(random.Next(5, 20));
                    return $"MediumResult_{i}";
                });

                // Occasionally slow operation
                if (i % 20 == 0)
                {
                    profiler.Profile("SlowOperation", () =>
                    {
                        Thread.Sleep(random.Next(50, 100));
                        return $"SlowResult_{i}";
                    });
                }

                // Custom metrics
                profiler.RecordMetric("ProcessedItems", i, "count");
                profiler.RecordMetric("MemoryUsage", GC.GetTotalMemory(false) / 1024.0 / 1024.0, "MB");

                if (i % 10 == 0)
                {
                    await Task.Delay(1); // Small delay to make it realistic
                }
            }
        }

        private static async Task RunDebuggingDemo(string outputDir)
        {
            Console.WriteLine("🔍 Advanced Debugging Demo");
            Console.WriteLine("===========================");

            // Create orchestrator for unified pattern
            var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                PoolSize = 2,
                EnableMetrics = false,
                EnableLogging = false
            });

            var debugger = new StateMachineDebugger();

            // Set up event handlers
            debugger.DebugEvent += (sender, args) =>
            {
                Console.WriteLine($"🐛 Debug Event: {args.EventName} in {args.MachineId} ({args.ProcessingTime.TotalMilliseconds:F2}ms)");
            };

            debugger.StateTransition += (sender, args) =>
            {
                Console.WriteLine($"🔄 Transition: {args.MachineId} {args.FromState} → {args.ToState} [{args.EventName}]");
            };

            // Create test state machines using orchestrated pattern
            var machine1 = CreateTestMachine("debugMachine1", orchestrator);
            var machine2 = CreateTestMachine("debugMachine2", orchestrator);

            // Get underlying StateMachines for debugger registration
            var underlying1 = (machine1 as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;
            var underlying2 = (machine2 as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

            // Register machines for debugging
            debugger.RegisterMachine("debugMachine1", underlying1!);
            debugger.RegisterMachine("debugMachine2", underlying2!);

            // Start machines
            await machine1.StartAsync();
            await machine2.StartAsync();

            Console.WriteLine("🎯 Simulating machine activity for debugging...");

            // Simulate activity
            await SimulateMachineActivity(orchestrator, debugger);

            // Get debugging statistics
            var machineStates = debugger.GetMachineStates();
            Console.WriteLine($"📊 Debug Statistics:");
            foreach (var (machineId, info) in machineStates)
            {
                Console.WriteLine($"   • {machineId}: {info.EventCount} events, {info.TransitionCount} transitions");
                Console.WriteLine($"     Current State: {info.CurrentState}, Running: {info.IsRunning}");
            }

            // Generate debug reports
            foreach (var machineId in machineStates.Keys)
            {
                var report = debugger.GenerateDebugReport(machineId);
                Console.WriteLine($"📈 {machineId} Report: {report.TotalEvents} events, avg {report.AverageEventProcessingTime:F2}ms");
            }

            // Export debug data
            var debugOutputPath = Path.Combine(outputDir, "debug_data.json");
            debugger.ExportDebugData(debugOutputPath);

            Console.WriteLine("✅ Debugging demo completed");
            Console.WriteLine();
        }

        private static async Task RunIntegratedDemo(string outputDir)
        {
            Console.WriteLine("🌟 Integrated Advanced Features Demo");
            Console.WriteLine("====================================");

            // Set up all advanced features together
            var configManager = new ConfigurationManager(Path.Combine(outputDir, "integrated_config"));
            configManager.RegisterConfiguration<IntegratedDemoConfig>();
            var config = configManager.LoadConfiguration<IntegratedDemoConfig>();

            var profilerConfig = new ProfilingConfiguration
            {
                SlowMethodThresholdMs = config.ProfilingThresholdMs,
                MaxSamples = config.MaxProfilingSamples
            };

            using var profiler = new PerformanceProfiler(profilerConfig);
            var debugger = new StateMachineDebugger();

            // Create orchestrator with advanced monitoring
            var orchestratorConfig = new OrchestratorConfig
            {
                PoolSize = config.OrchestratorPoolSize,
                EnableMetrics = true,
                EnableBackpressure = config.EnableBackpressure,
                MaxQueueDepth = config.MaxQueueDepth
            };

            using var orchestrator = new EventBusOrchestrator(orchestratorConfig);

            // Create and register machines
            var machines = new List<IPureStateMachine>();
            for (int i = 1; i <= config.NumberOfMachines; i++)
            {
                var machine = CreateAdvancedTestMachine($"integrated_{i}", profiler, orchestrator);
                machines.Add(machine);

                // Get underlying for debugger registration
                var underlying = (machine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;
                debugger.RegisterMachine($"integrated_{i}", underlying!);
            }

            // Start monitoring dashboard
            var dashboard = orchestrator.CreateDashboard();
            dashboard.StartMonitoring(TimeSpan.FromSeconds(1));

            // Start machines
            foreach (var machine in machines)
            {
                await machine.StartAsync();
            }

            Console.WriteLine($"🚀 Running integrated demo with {config.NumberOfMachines} machines...");

            // Run comprehensive test scenario
            var testTasks = new List<Task>();
            for (int i = 0; i < config.TotalEvents; i++)
            {
                var machineId = $"integrated_{(i % config.NumberOfMachines) + 1}";

                var task = profiler.ProfileAsync("IntegratedEventProcessing", async () =>
                {
                    await orchestrator.SendEventFireAndForgetAsync($"integrated_event_{i}", machineId, "PROCESS",
                        new { EventId = i, Data = $"Test data {i}" });
                    return true;
                }, $"Machine: {machineId}");

                testTasks.Add(task);

                if (i % 100 == 0)
                {
                    await Task.Delay(10); // Small delay to prevent overwhelming
                }
            }

            await Task.WhenAll(testTasks);
            await Task.Delay(2000); // Allow processing to complete

            // Stop monitoring and generate reports
            dashboard.StopMonitoring();

            Console.WriteLine("📊 Integrated Demo Results:");
            Console.WriteLine("===========================");

            // Orchestrator metrics
            var orchMetrics = orchestrator.Metrics;
            Console.WriteLine($"🎯 Orchestrator: metrics collected");

            // Profiling results
            var profilingReport = profiler.GenerateReport();
            Console.WriteLine($"🔥 Profiling: {profilingReport.Statistics.TotalSamples} samples, {profilingReport.Statistics.AverageExecutionTime:F2}ms avg");

            // Debug statistics
            var debugStates = debugger.GetMachineStates();
            var totalEvents = debugStates.Values.Sum(info => info.EventCount);
            var totalTransitions = debugStates.Values.Sum(info => info.TransitionCount);
            Console.WriteLine($"🔍 Debugging: {totalEvents} events tracked, {totalTransitions} transitions");

            // Export all results
            var integratedOutputDir = Path.Combine(outputDir, "integrated");
            Directory.CreateDirectory(integratedOutputDir);

            await profiler.ExportDataAsync(integratedOutputDir, ExportFormat.All);
            debugger.ExportDebugData(Path.Combine(integratedOutputDir, "integrated_debug.json"));
            dashboard.DisplaySummaryReport();

            Console.WriteLine($"✅ Integrated demo completed - results in {integratedOutputDir}");
            Console.WriteLine();
        }

        private static IPureStateMachine CreateTestMachine(string machineId, EventBusOrchestrator orchestrator)
        {
            var localMachineId = machineId; // Capture for use in actions
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["startProcess"] = ctx =>
                {
                    Thread.Sleep(Random.Shared.Next(5, 20));
                    Console.WriteLine($"   Processing started in {localMachineId}");
                },
                ["validateInput"] = ctx =>
                {
                    Thread.Sleep(Random.Shared.Next(10, 30));
                    var success = Random.Shared.NextDouble() > 0.1; // 90% success rate
                    // Note: Would need machine reference to send events
                    Console.WriteLine($"   Validation {(success ? "succeeded" : "failed")} in {localMachineId}");
                },
                ["processData"] = ctx =>
                {
                    Thread.Sleep(Random.Shared.Next(20, 50));
                    Console.WriteLine($"   Data processed in {localMachineId}");
                },
                ["finishProcess"] = ctx =>
                {
                    Thread.Sleep(Random.Shared.Next(5, 15));
                    Console.WriteLine($"   Process finished in {localMachineId}");
                }
            };

            var json = @"{
                ""id"": ""testMachine"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": { ""START"": ""starting"" }
                    },
                    ""starting"": {
                        ""entry"": [""startProcess""],
                        ""on"": { ""PROCESS"": ""validating"" }
                    },
                    ""validating"": {
                        ""entry"": [""validateInput""],
                        ""on"": {
                            ""VALIDATION_SUCCESS"": ""processing"",
                            ""VALIDATION_FAILED"": ""failed""
                        }
                    },
                    ""processing"": {
                        ""entry"": [""processData""],
                        ""on"": { ""COMPLETE"": ""finishing"" }
                    },
                    ""finishing"": {
                        ""entry"": [""finishProcess""],
                        ""on"": { ""DONE"": ""idle"" }
                    },
                    ""failed"": {
                        ""on"": { ""RETRY"": ""starting"" }
                    }
                }
            }";

            return ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: machineId,
                json: json,
                orchestrator: orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
        }

        private static IPureStateMachine CreateAdvancedTestMachine(string machineId, PerformanceProfiler profiler, EventBusOrchestrator orchestrator)
        {
            var localMachineId = machineId; // Capture for use in actions
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["profiledProcess"] = ctx =>
                {
                    using (profiler.ProfileMethod("ProcessAction", localMachineId))
                    {
                        Thread.Sleep(Random.Shared.Next(1, 10));
                        profiler.RecordMetric($"EventsProcessed_{localMachineId}", 1, "count");
                    }
                },
                ["profiledValidation"] = ctx =>
                {
                    using (profiler.ProfileMethod("ValidationAction", localMachineId))
                    {
                        Thread.Sleep(Random.Shared.Next(2, 8));
                        Console.WriteLine($"[{localMachineId}] Validation performed");
                    }
                }
            };

            var json = @"{
                ""id"": ""advancedTestMachine"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {
                        ""on"": { ""PROCESS"": ""processing"" }
                    },
                    ""processing"": {
                        ""entry"": [""profiledProcess""],
                        ""on"": { ""VALIDATE"": ""validating"" }
                    },
                    ""validating"": {
                        ""entry"": [""profiledValidation""],
                        ""after"": { ""50"": ""ready"" }
                    }
                }
            }";

            return ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: machineId,
                json: json,
                orchestrator: orchestrator,
                orchestratedActions: actions,
                guards: null,
                services: null,
                delays: null,
                activities: null);
        }

        private static async Task SimulateMachineActivity(EventBusOrchestrator orchestrator, StateMachineDebugger debugger)
        {
            var tasks = new List<Task>();

            // Simulate activity on both machines using orchestrator
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(orchestrator.SendEventAsync("demo", "debugMachine1", "START"));
                tasks.Add(orchestrator.SendEventAsync("demo", "debugMachine2", "START"));

                if (i % 5 == 0)
                {
                    tasks.Add(orchestrator.SendEventAsync("demo", "debugMachine1", "PROCESS"));
                    tasks.Add(orchestrator.SendEventAsync("demo", "debugMachine2", "PROCESS"));
                }

                await Task.Delay(50);
            }

            await Task.WhenAll(tasks);
        }
    }

    // Configuration classes for demos
    public class DemoConfiguration
    {
        public string ApplicationName { get; set; } = "XStateNet Demo";
        public bool EnableDebugMode { get; set; } = false;
        public string LogLevel { get; set; } = "Information";
        public int MaxRetries { get; set; } = 3;
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    public class AdvancedOrchestratorConfig
    {
        public int PoolSize { get; set; } = 4;
        public bool EnableBackpressure { get; set; } = true;
        public int MaxQueueDepth { get; set; } = 10000;
        public bool EnableMetrics { get; set; } = true;
        public bool EnableCircuitBreaker { get; set; } = false;
        public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromSeconds(5);
    }

    public class IntegratedDemoConfig
    {
        public int NumberOfMachines { get; set; } = 5;
        public int TotalEvents { get; set; } = 1000;
        public int OrchestratorPoolSize { get; set; } = 8;
        public bool EnableBackpressure { get; set; } = true;
        public int MaxQueueDepth { get; set; } = 5000;
        public double ProfilingThresholdMs { get; set; } = 10.0;
        public int MaxProfilingSamples { get; set; } = 50000;
    }
}