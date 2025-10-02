using System;
using System.Threading.Tasks;
using XStateNet.Orchestration;
using XStateNet.Semi.Machines;

namespace SemiStandard.Testing.Console;

/// <summary>
/// Simple test to verify orchestrator-based machine creation works
/// </summary>
public class SimpleOrchestratorTest
{
    public static async Task RunAsync()
    {
        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  Simple Orchestrator Test                                    ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        // Create orchestrator
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 2,
            EnableMetrics = true
        };

        using var orchestrator = new EventBusOrchestrator(config);

        System.Console.WriteLine("✅ Orchestrator created");

        // Create LoadPortMachine
        var loadPort = new LoadPortMachine("001", orchestrator);
        System.Console.WriteLine("✅ LoadPortMachine created");

        // Start the machine
        await loadPort.StartAsync();
        System.Console.WriteLine($"✅ LoadPortMachine started - State: {loadPort.GetCurrentState()}");
        System.Console.WriteLine();

        // Send LOAD_CARRIER event
        System.Console.WriteLine("📤 Sending LOAD_CARRIER event...");
        System.Console.WriteLine();

        var result = await orchestrator.SendEventAsync(
            "TEST",
            "LOADPORT_001",
            "LOAD_CARRIER",
            new { carrierId = "CAR-001", waferId = "W001" }
        );

        if (result.Success)
        {
            System.Console.WriteLine();
            System.Console.WriteLine($"✅ Event processed successfully");
            System.Console.WriteLine($"   Final state: {loadPort.GetCurrentState()}");
        }
        else
        {
            System.Console.WriteLine();
            System.Console.WriteLine($"❌ Event failed: {result.ErrorMessage}");
        }

        System.Console.WriteLine();
        System.Console.WriteLine("Press any key to exit...");
        System.Console.ReadKey(true);
    }
}