using XStateNet.Orchestration;
using XStateNet.Semi.Schedulers;

namespace SemiStandard.Testing.Console;

/// <summary>
/// Multi-Station CMP Process Flow Demo
/// Process: Loadport -> WTR1 -> Polishing -> WTR2 -> PostCleaning -> WTR1 -> Loadport
/// </summary>
public static class MultiStationCMPDemo
{
    public static async Task Run()
    {
        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║   MULTI-STATION CMP PROCESS FLOW DEMO                              ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║   Process Flow:                                                    ║");
        System.Console.WriteLine("║   Loadport → WTR1 → Polishing → WTR2 → PostCleaning → WTR1 → LP   ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║   Stations:                                                        ║");
        System.Console.WriteLine("║   • 2 Load Ports (LP1, LP2)                                        ║");
        System.Console.WriteLine("║   • 1 Wafer Transfer Robot #1 (WTR1)                               ║");
        System.Console.WriteLine("║   • 3 Polishing Stations (POL1, POL2, POL3)                        ║");
        System.Console.WriteLine("║   • 1 Wafer Transfer Robot #2 (WTR2)                               ║");
        System.Console.WriteLine("║   • 2 Post-Cleaning Stations (CLN1, CLN2)                          ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 12,
            EnableMetrics = true
        };

        using var orchestrator = new EventBusOrchestrator(config);

        System.Console.WriteLine("🔧 Initializing Multi-Station CMP System...\n");

        // Create Master Scheduler
        System.Console.WriteLine("📋 Creating Master Scheduler...");
        var masterScheduler = new MultiStationCMPMasterScheduler("MASTER_001", orchestrator, maxWip: 5);
        await masterScheduler.StartAsync();
        System.Console.WriteLine("   ✅ Master scheduler ready");
        System.Console.WriteLine();

        // Create Load Ports
        System.Console.WriteLine("🚪 Creating Load Ports...");
        var loadPort1 = new LoadPortStation("LP1", orchestrator);
        var loadPort2 = new LoadPortStation("LP2", orchestrator);
        await loadPort1.StartAsync();
        await loadPort2.StartAsync();
        System.Console.WriteLine("   ✅ Load ports ready");

        // Create WTR1 (transfers from loadport to polishing)
        System.Console.WriteLine("🤖 Creating Wafer Transfer Robot #1...");
        var wtr1 = new WaferTransferRobot("WTR1", orchestrator);
        await wtr1.StartAsync();
        System.Console.WriteLine("   ✅ WTR1 ready");

        // Create Polishing Stations
        System.Console.WriteLine("💎 Creating Polishing Stations...");
        var pol1 = new PolishingStation("POL1", orchestrator);
        var pol2 = new PolishingStation("POL2", orchestrator);
        var pol3 = new PolishingStation("POL3", orchestrator);
        await pol1.StartAsync();
        await pol2.StartAsync();
        await pol3.StartAsync();
        System.Console.WriteLine("   ✅ Polishing stations ready");

        // Create WTR2 (transfers from polishing to cleaning)
        System.Console.WriteLine("🤖 Creating Wafer Transfer Robot #2...");
        var wtr2 = new WaferTransferRobot("WTR2", orchestrator);
        await wtr2.StartAsync();
        System.Console.WriteLine("   ✅ WTR2 ready");

        // Create Post-Cleaning Stations
        System.Console.WriteLine("🧼 Creating Post-Cleaning Stations...");
        var cln1 = new PostCleaningStation("CLN1", orchestrator);
        var cln2 = new PostCleaningStation("CLN2", orchestrator);
        await cln1.StartAsync();
        await cln2.StartAsync();
        System.Console.WriteLine("   ✅ Post-cleaning stations ready");
        System.Console.WriteLine();

        // Register all stations with master scheduler
        System.Console.WriteLine("📝 Registering stations with master scheduler...");
        await masterScheduler.RegisterStationAsync(loadPort1.MachineId, "LoadPort");
        await masterScheduler.RegisterStationAsync(loadPort2.MachineId, "LoadPort");
        await masterScheduler.RegisterStationAsync(wtr1.MachineId, "WTR");
        await masterScheduler.RegisterStationAsync(pol1.MachineId, "Polishing");
        await masterScheduler.RegisterStationAsync(pol2.MachineId, "Polishing");
        await masterScheduler.RegisterStationAsync(pol3.MachineId, "Polishing");
        await masterScheduler.RegisterStationAsync(wtr2.MachineId, "WTR");
        await masterScheduler.RegisterStationAsync(cln1.MachineId, "PostCleaning");
        await masterScheduler.RegisterStationAsync(cln2.MachineId, "PostCleaning");
        System.Console.WriteLine("   ✅ All stations registered");
        System.Console.WriteLine();

        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  System Initialized - Ready to Process Wafers                      ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        PrintSystemStatus(masterScheduler, loadPort1, loadPort2, wtr1, pol1, pol2, pol3, wtr2, cln1, cln2);

        // Simulate wafer processing
        System.Console.WriteLine("\n🚀 Starting multi-station wafer processing simulation...\n");

        var waferCount = 10;
        var waferInterval = 2000;

        for (int i = 0; i < waferCount; i++)
        {
            var waferId = $"W{i + 1:D4}";
            var lotId = $"LOT_{(i / 5) + 1:D3}";

            System.Console.WriteLine($"📨 Wafer {i + 1:D2}/{waferCount} arriving: {waferId} (Lot: {lotId})");

            await orchestrator.SendEventAsync("SYSTEM", masterScheduler.MachineId, "WAFER_ARRIVED", new
            {
                waferId = waferId,
                lotId = lotId,
                recipeId = "CMP_STANDARD_01"
            });

            await Task.Delay(waferInterval);

            // Print periodic status
            if ((i + 1) % 3 == 0)
            {
                System.Console.WriteLine();
                PrintSystemStatus(masterScheduler, loadPort1, loadPort2, wtr1, pol1, pol2, pol3, wtr2, cln1, cln2);
                System.Console.WriteLine();
            }
        }

        System.Console.WriteLine("\n⏳ All wafers submitted. Processing...\n");
        await Task.Delay(30000);

        System.Console.WriteLine("\n📊 Final System Status:\n");
        PrintSystemStatus(masterScheduler, loadPort1, loadPort2, wtr1, pol1, pol2, pol3, wtr2, cln1, cln2);

        System.Console.WriteLine("\n✅ Demo completed!");
    }

    private static void PrintSystemStatus(
        MultiStationCMPMasterScheduler scheduler,
        LoadPortStation lp1, LoadPortStation lp2,
        WaferTransferRobot wtr1,
        PolishingStation pol1, PolishingStation pol2, PolishingStation pol3,
        WaferTransferRobot wtr2,
        PostCleaningStation cln1, PostCleaningStation cln2)
    {
        System.Console.WriteLine("┌────────────────────────────────────────────────────────────────────┐");
        System.Console.WriteLine("│ Multi-Station CMP System Status                                    │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ Master Scheduler:  {scheduler.GetCurrentState(),-40} │");
        System.Console.WriteLine($"│ Current WIP:       {scheduler.GetCurrentWip()}/{scheduler.GetMaxWip()}                                                  │");
        System.Console.WriteLine($"│ Queue Length:      {scheduler.GetQueueLength(),-40} │");
        System.Console.WriteLine($"│ Wafers Processed:  {scheduler.GetTotalWafersProcessed(),-40} │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ LP1:             {lp1.GetCurrentState(),-40} │");
        System.Console.WriteLine($"│ LP2:             {lp2.GetCurrentState(),-40} │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ WTR1:            {wtr1.GetCurrentState(),-40} │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ POL1:            {pol1.GetCurrentState(),-40} │");
        System.Console.WriteLine($"│ POL2:            {pol2.GetCurrentState(),-40} │");
        System.Console.WriteLine($"│ POL3:            {pol3.GetCurrentState(),-40} │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ WTR2:            {wtr2.GetCurrentState(),-40} │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ CLN1:            {cln1.GetCurrentState(),-40} │");
        System.Console.WriteLine($"│ CLN2:            {cln2.GetCurrentState(),-40} │");
        System.Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
    }
}
