using XStateNet.Orchestration;
using XStateNet.Semi.Schedulers;
using XStateNet.Semi.Standards;

namespace SemiStandard.Testing.Console;

/// <summary>
/// Enhanced CMP Demo - Phase 1 Integration
/// Demonstrates: E40 Process Jobs, E134 Data Collection, E39 Metrics, E90 Substrate Tracking
/// </summary>
public class EnhancedCMPDemo
{
    public static async Task RunAsync()
    {
        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║   ENHANCED CMP SIMULATOR - Phase 1 Integration                     ║");
        System.Console.WriteLine("║   Utilizing Full XStateNet Infrastructure                          ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║   SEMI Standards Integrated:                                       ║");
        System.Console.WriteLine("║   ✅ E40  - Process Job Management                                 ║");
        System.Console.WriteLine("║   ✅ E90  - Substrate Tracking (per wafer)                         ║");
        System.Console.WriteLine("║   ✅ E134 - Data Collection Management                             ║");
        System.Console.WriteLine("║   ✅ E39  - Equipment Metrics & Alarms                             ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║   Features:                                                        ║");
        System.Console.WriteLine("║   • Formal E40 job lifecycle (QUEUED→PROCESSING→COMPLETED)         ║");
        System.Console.WriteLine("║   • E90 wafer genealogy & location tracking                        ║");
        System.Console.WriteLine("║   • E134 real-time data collection (events, metrics, state)        ║");
        System.Console.WriteLine("║   • E39 performance metrics (utilization, throughput, cycle time)  ║");
        System.Console.WriteLine("║   • Priority-based scheduling                                      ║");
        System.Console.WriteLine("║   • Load balancing with advanced scoring                           ║");
        System.Console.WriteLine("║   • Automatic PM scheduling                                        ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        // Create orchestrator
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 8,
            EnableMetrics = true
        };

        using var orchestrator = new EventBusOrchestrator(config);

        System.Console.WriteLine("🔧 Initializing Enhanced CMP System...\n");

        // Create enhanced master scheduler
        System.Console.WriteLine("📋 Creating Enhanced Master Scheduler with SEMI integration...");
        var masterScheduler = new EnhancedCMPMasterScheduler("001", orchestrator, maxWip: 3);
        await masterScheduler.StartAsync();
        System.Console.WriteLine("   ✅ E40 Process Job management active");
        System.Console.WriteLine("   ✅ E134 Data Collection plans configured");
        System.Console.WriteLine("   ✅ E39 Equipment Metrics defined");
        System.Console.WriteLine();

        // Create enhanced tool schedulers
        System.Console.WriteLine("🔧 Creating Enhanced CMP Tool Schedulers...");
        var tool1 = new EnhancedCMPToolScheduler("CMP_TOOL_1", orchestrator);
        var tool2 = new EnhancedCMPToolScheduler("CMP_TOOL_2", orchestrator);
        var tool3 = new EnhancedCMPToolScheduler("CMP_TOOL_3", orchestrator);

        await tool1.StartAsync();
        await tool2.StartAsync();
        await tool3.StartAsync();

        System.Console.WriteLine("   ✅ E90 Substrate Tracking ready");
        System.Console.WriteLine("   ✅ E134 Tool-level data collection active");
        System.Console.WriteLine("   ✅ E39 Tool metrics configured");
        System.Console.WriteLine();

        // Register tools
        System.Console.WriteLine("📝 Registering tools with master scheduler...");
        masterScheduler.RegisterTool("CMP_TOOL_1", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_OXIDE_01" },
            ["maxWaferSize"] = 300,
            ["chamber"] = "A"
        });

        masterScheduler.RegisterTool("CMP_TOOL_2", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_METAL_01" },
            ["maxWaferSize"] = 300,
            ["chamber"] = "B"
        });

        masterScheduler.RegisterTool("CMP_TOOL_3", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01" },
            ["maxWaferSize"] = 300,
            ["chamber"] = "C"
        });
        System.Console.WriteLine();

        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  System Initialized - Ready to Process Wafers                      ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        PrintSystemStatus(masterScheduler, new[] { tool1, tool2, tool3 });

        // Simulate job processing
        System.Console.WriteLine("\n🚀 Starting job processing simulation...\n");

        var jobCount = 12;
        var jobInterval = 1500;

        for (int i = 0; i < jobCount; i++)
        {
            var priority = i % 4 == 0 ? "High" : "Normal";

            System.Console.WriteLine($"📨 Job {i + 1:D2}/{jobCount} arriving (Priority: {priority})");

            await orchestrator.SendEventAsync("SYSTEM", masterScheduler.MachineId, "JOB_ARRIVED", new
            {
                jobId = $"JOB_{i + 1:D3}",
                priority = priority,
                waferId = $"W{i + 1:D4}",
                recipeId = "CMP_STANDARD_01"
            });

            await Task.Delay(jobInterval);

            // Print periodic status
            if ((i + 1) % 4 == 0)
            {
                System.Console.WriteLine();
                PrintSystemStatus(masterScheduler, new[] { tool1, tool2, tool3 });
                System.Console.WriteLine();
            }
        }

        System.Console.WriteLine("\n⏳ All jobs submitted. Processing...\n");

        // Wait for processing to complete
        await Task.Delay(45000);

        System.Console.WriteLine();
        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  Final System Status                                               ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        PrintFinalReport(masterScheduler, new[] { tool1, tool2, tool3 });

        System.Console.WriteLine();
        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  E134 Data Collection Reports                                      ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        PrintDataCollectionReports(masterScheduler, new[] { tool1, tool2, tool3 });

        System.Console.WriteLine();
        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  Demo Complete! ✅                                                  ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║  This demonstration showcases XStateNet's comprehensive            ║");
        System.Console.WriteLine("║  SEMI standards integration for production semiconductor           ║");
        System.Console.WriteLine("║  manufacturing automation.                                         ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║  Key Achievements:                                                 ║");
        System.Console.WriteLine("║  ✅ E40 formal job lifecycle management                            ║");
        System.Console.WriteLine("║  ✅ E90 complete wafer genealogy tracking                          ║");
        System.Console.WriteLine("║  ✅ E134 comprehensive data collection & trending                  ║");
        System.Console.WriteLine("║  ✅ E39 real-time performance metrics                              ║");
        System.Console.WriteLine("║  ✅ Orchestrated state machine communication                       ║");
        System.Console.WriteLine("║  ✅ Multi-tool coordination                                        ║");
        System.Console.WriteLine("║  ✅ Priority scheduling & load balancing                           ║");
        System.Console.WriteLine("║  ✅ Automatic maintenance scheduling                               ║");
        System.Console.WriteLine("║                                                                    ║");
        System.Console.WriteLine("║  Next Phase: SharedMemory IPC, E148 Time Sync, E94 Control Jobs   ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();
    }

    private static void PrintSystemStatus(
        EnhancedCMPMasterScheduler master,
        EnhancedCMPToolScheduler[] tools)
    {
        System.Console.WriteLine("┌────────────────────────────────────────────────────────────────────┐");
        System.Console.WriteLine("│ System Status                                                      │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ Master Scheduler:  {master.GetCurrentState(),-41} │");
        System.Console.WriteLine($"│ Current WIP:       {master.GetCurrentWip()}/{master.GetQueueLength() + master.GetCurrentWip(),-41} │");
        System.Console.WriteLine($"│ Queue Length:      {master.GetQueueLength(),-41} │");
        System.Console.WriteLine($"│ Jobs Processed:    {master.GetTotalJobsProcessed(),-41} │");
        System.Console.WriteLine($"│ Utilization:       {master.GetUtilization():F1}%{new string(' ', 37)} │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");

        for (int i = 0; i < tools.Length; i++)
        {
            var tool = tools[i];
            System.Console.WriteLine($"│ Tool {i + 1}:          {tool.GetCurrentState(),-41} │");
            System.Console.WriteLine($"│   Wafers:         {tool.GetWafersProcessed(),-41} │");
            System.Console.WriteLine($"│   Slurry:         {tool.GetSlurryLevel():F1}%{new string(' ', 37)} │");
            System.Console.WriteLine($"│   Pad Wear:       {tool.GetPadWear():F1}%{new string(' ', 37)} │");
            if (i < tools.Length - 1)
                System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        }

        System.Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
    }

    private static void PrintFinalReport(
        EnhancedCMPMasterScheduler master,
        EnhancedCMPToolScheduler[] tools)
    {
        System.Console.WriteLine("┌────────────────────────────────────────────────────────────────────┐");
        System.Console.WriteLine("│ Master Scheduler Summary                                           │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine($"│ Total Jobs Processed:    {master.GetTotalJobsProcessed(),-37} │");
        System.Console.WriteLine($"│ Final WIP:               {master.GetCurrentWip(),-37} │");
        System.Console.WriteLine($"│ Queue Length:            {master.GetQueueLength(),-37} │");
        System.Console.WriteLine($"│ Average Utilization:     {master.GetUtilization():F1}%{new string(' ', 33)} │");
        System.Console.WriteLine($"│ Throughput:              {master.GetThroughput():F1} wafers/hour{new string(' ', 21)} │");
        System.Console.WriteLine("└────────────────────────────────────────────────────────────────────┘");
        System.Console.WriteLine();

        System.Console.WriteLine("┌────────────────────────────────────────────────────────────────────┐");
        System.Console.WriteLine("│ Tool Performance Summary                                           │");
        System.Console.WriteLine("├────────────────────────────────────────────────────────────────────┤");
        System.Console.WriteLine("│ Tool    │ Wafers │ Avg Cycle │ Slurry │ Pad Wear │ State         │");
        System.Console.WriteLine("├─────────┼────────┼───────────┼────────┼──────────┼───────────────┤");

        for (int i = 0; i < tools.Length; i++)
        {
            var tool = tools[i];
            System.Console.WriteLine($"│ Tool {i + 1}  │ {tool.GetWafersProcessed(),6} │  {tool.GetAvgCycleTime(),5:F1}s   │ {tool.GetSlurryLevel(),5:F1}% │  {tool.GetPadWear(),5:F1}%  │ {tool.GetCurrentState(),-13} │");
        }

        System.Console.WriteLine("└─────────┴────────┴───────────┴────────┴──────────┴───────────────┘");
    }

    private static void PrintDataCollectionReports(
        EnhancedCMPMasterScheduler master,
        EnhancedCMPToolScheduler[] tools)
    {
        System.Console.WriteLine("Master Scheduler - Job Completion Reports:");
        var completionReports = master.GetReports("JOB_COMPLETION").Take(5).ToList();
        System.Console.WriteLine($"  Total Reports: {master.GetReports("JOB_COMPLETION").Count()}");

        if (completionReports.Any())
        {
            foreach (var report in completionReports)
            {
                System.Console.WriteLine($"  [{report.Timestamp:HH:mm:ss}] Jobs: {report.Data.GetValueOrDefault("TotalJobs", 0)}, " +
                    $"WIP: {report.Data.GetValueOrDefault("CurrentWIP", 0)}, " +
                    $"Throughput: {report.Data.GetValueOrDefault("ThroughputWPH", 0):F1} WPH");
            }
        }

        System.Console.WriteLine();
        System.Console.WriteLine("Tool 1 - Wafer Completion Reports:");
        var tool1Reports = tools[0].GetReports("WAFER_COMPLETION").Take(5).ToList();
        System.Console.WriteLine($"  Total Reports: {tools[0].GetReports("WAFER_COMPLETION").Count()}");

        if (tool1Reports.Any())
        {
            foreach (var report in tool1Reports)
            {
                System.Console.WriteLine($"  [{report.Timestamp:HH:mm:ss}] Wafer: {report.Data.GetValueOrDefault("WaferId", "N/A")}, " +
                    $"Cycle: {report.Data.GetValueOrDefault("CycleTime", 0):F1}s, " +
                    $"Total: {report.Data.GetValueOrDefault("TotalWafers", 0)}");
            }
        }

        System.Console.WriteLine();
        System.Console.WriteLine($"📊 E134 Data Collection: {master.GetReports("JOB_COMPLETION").Count() + tools.Sum(t => t.GetReports("WAFER_COMPLETION").Count())} total reports collected");
    }
}
