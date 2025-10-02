using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Orchestration;

namespace OrchestratorTestApp
{
    public static class MonitoringDemo
    {
        public static async Task Run()
        {
            Console.WriteLine("🔍 Starting Orchestrator Monitoring Demo...\n");

            // Create orchestrator with monitoring enabled
            using var orchestrator = new EventBusOrchestrator(new OrchestratorConfig
            {
                EnableLogging = false,  // Disable console spam for cleaner demo
                EnableMetrics = true,
                EnableStructuredLogging = true,
                LogLevel = "INFO",
                PoolSize = 4,
                EnableBackpressure = true,
                MaxQueueDepth = 5000
            });

            // Create monitoring dashboard
            var dashboard = orchestrator.CreateDashboard();

            Console.WriteLine("📊 Monitoring Dashboard Created");
            Console.WriteLine("Starting background monitoring...\n");

            // Start real-time dashboard (updates every 2 seconds)
            dashboard.StartMonitoring(TimeSpan.FromSeconds(2));

            // Create test machines for monitoring
            await SetupTestMachines(orchestrator);

            Console.WriteLine("🎯 Running monitoring scenarios...\n");

            // Scenario 1: Normal operations
            await RunNormalOperations(orchestrator);

            // Wait to see metrics
            await Task.Delay(3000);

            // Scenario 2: High load testing
            await RunHighLoadScenario(orchestrator);

            // Wait to see metrics
            await Task.Delay(3000);

            // Scenario 3: Error conditions
            await RunErrorScenario(orchestrator);

            // Wait to see metrics
            await Task.Delay(3000);

            // Stop dashboard and show final summary
            dashboard.StopMonitoring();
            Console.Clear();

            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                          MONITORING DEMO COMPLETED                          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝\n");

            // Display final metrics summary
            dashboard.DisplaySummaryReport();

            // Show health status
            var health = orchestrator.GetHealthStatus();
            Console.WriteLine($"\n🏥 Final Health Status: {health.Level}");
            if (health.Issues.Count > 0)
            {
                Console.WriteLine("   Issues detected:");
                foreach (var issue in health.Issues)
                {
                    Console.WriteLine($"   • {issue}");
                }
            }

            Console.WriteLine("\n✅ Monitoring demo completed! The orchestrator provides comprehensive");
            Console.WriteLine("   observability into system performance, health, and operational metrics.");

            dashboard.Dispose();
        }

        private static async Task SetupTestMachines(EventBusOrchestrator orchestrator)
        {
            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["process"] = (ctx) =>
                {
                    // Simulate processing work
                    Thread.Sleep(10);
                },
                ["forward"] = (ctx) =>
                {
                    // Forward to another machine
                    ctx.RequestSend("processor2", "PROCESS");
                },
                ["error"] = (ctx) =>
                {
                    throw new Exception("Simulated error for monitoring");
                }
            };

            // Create multiple test machines
            for (int i = 1; i <= 5; i++)
            {
                var json = $@"{{
                    ""id"": ""processor{i}"",
                    ""initial"": ""ready"",
                    ""states"": {{
                        ""ready"": {{
                            ""entry"": [""process""],
                            ""on"": {{
                                ""PROCESS"": ""ready"",
                                ""FORWARD"": ""forwarding"",
                                ""ERROR"": ""error""
                            }}
                        }},
                        ""forwarding"": {{
                            ""entry"": [""forward""],
                            ""on"": {{ ""DONE"": ""ready"" }}
                        }},
                        ""error"": {{
                            ""entry"": [""error""]
                        }}
                    }}
                }}";

                var machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                    id: $"processor{i}",
                    json: json,
                    orchestrator: orchestrator,
                    orchestratedActions: actions,
                    guards: null,
                    services: null,
                    delays: null,
                    activities: null);
                await orchestrator.StartMachineAsync($"processor{i}");
            }

            Console.WriteLine("✅ Test machines registered and started");
        }

        private static async Task RunNormalOperations(EventBusOrchestrator orchestrator)
        {
            Console.WriteLine("🔄 Scenario 1: Normal Operations (100 events)");

            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                var machineId = $"processor{(i % 5) + 1}";
                tasks.Add(orchestrator.SendEventFireAndForgetAsync("demo", machineId, "PROCESS"));
            }

            await Task.WhenAll(tasks);
            Console.WriteLine("   ✅ Normal operations completed");
        }

        private static async Task RunHighLoadScenario(EventBusOrchestrator orchestrator)
        {
            Console.WriteLine("🚀 Scenario 2: High Load (1000 events, parallel)");

            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                var machineId = $"processor{(i % 5) + 1}";
                tasks.Add(orchestrator.SendEventFireAndForgetAsync("demo", machineId, "PROCESS"));
            }

            await Task.WhenAll(tasks);
            Console.WriteLine("   ✅ High load scenario completed");
        }

        private static async Task RunErrorScenario(EventBusOrchestrator orchestrator)
        {
            Console.WriteLine("⚠️  Scenario 3: Error Conditions (50 errors + normal traffic)");

            var tasks = new List<Task>();

            // Send some error events
            for (int i = 0; i < 50; i++)
            {
                var machineId = $"processor{(i % 5) + 1}";
                tasks.Add(orchestrator.SendEventFireAndForgetAsync("demo", machineId, "ERROR"));
            }

            // Mix with normal traffic
            for (int i = 0; i < 100; i++)
            {
                var machineId = $"processor{(i % 5) + 1}";
                tasks.Add(orchestrator.SendEventFireAndForgetAsync("demo", machineId, "PROCESS"));
            }

            await Task.WhenAll(tasks);
            Console.WriteLine("   ✅ Error scenario completed");
        }
    }
}