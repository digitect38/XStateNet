using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet.Orchestration;
using XStateNet.Semi.Schedulers;

namespace SemiStandard.Testing.Console;

/// <summary>
/// CMP Scheduler System Demo
/// Demonstrates master scheduler + tool schedulers coordinating job processing
/// </summary>
public class CMPSchedulerDemo
{
    public static async Task RunAsync()
    {
        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║                                                              ║");
        System.Console.WriteLine("║  CMP Scheduler System - Master + Tool Schedulers            ║");
        System.Console.WriteLine("║  Demonstrating Production Job Scheduling Architecture       ║");
        System.Console.WriteLine("║                                                              ║");
        System.Console.WriteLine("║  Features:                                                   ║");
        System.Console.WriteLine("║  • Priority-based job queuing                                ║");
        System.Console.WriteLine("║  • WIP (Work In Progress) control                            ║");
        System.Console.WriteLine("║  • Load balancing across tools                               ║");
        System.Console.WriteLine("║  • Tool selection algorithm                                  ║");
        System.Console.WriteLine("║  • Consumable tracking & PM scheduling                       ║");
        System.Console.WriteLine("║  • Automatic maintenance cycles                              ║");
        System.Console.WriteLine("║                                                              ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        // Create orchestrator
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 4,
            EnableMetrics = true
        };

        using var orchestrator = new EventBusOrchestrator(config);

        // Create master scheduler with WIP limit of 3
        System.Console.WriteLine("🔧 Creating master scheduler (WIP limit: 3)...");
        var masterScheduler = new CMPMasterScheduler("001", orchestrator, maxWip: 3);
        await masterScheduler.StartAsync();
        System.Console.WriteLine();

        // Create CMP tool schedulers
        System.Console.WriteLine("🔧 Creating CMP tool schedulers...");
        var cmpTool1 = new CMPToolScheduler("CMP_TOOL_1", orchestrator);
        var cmpTool2 = new CMPToolScheduler("CMP_TOOL_2", orchestrator);
        var cmpTool3 = new CMPToolScheduler("CMP_TOOL_3", orchestrator);

        await cmpTool1.StartAsync();
        await cmpTool2.StartAsync();
        await cmpTool3.StartAsync();
        System.Console.WriteLine();

        // Register tools with master scheduler
        System.Console.WriteLine("📋 Registering tools with master scheduler...");
        masterScheduler.RegisterTool("CMP_TOOL_1", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_OXIDE_01" }
        });

        masterScheduler.RegisterTool("CMP_TOOL_2", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_METAL_01" }
        });

        masterScheduler.RegisterTool("CMP_TOOL_3", "CMP", new Dictionary<string, object>
        {
            ["recipes"] = new[] { "CMP_STANDARD_01" }
        });
        System.Console.WriteLine();

        System.Console.WriteLine("✅ Scheduler system initialized\n");

        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  System Status                                                ║");
        System.Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        System.Console.WriteLine($"║  Master Scheduler:  {masterScheduler.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  CMP Tool 1:        {cmpTool1.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  CMP Tool 2:        {cmpTool2.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  CMP Tool 3:        {cmpTool3.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  Current WIP:       {masterScheduler.GetCurrentWip()}/3{new string(' ', 42)}║");
        System.Console.WriteLine($"║  Queue Length:      {masterScheduler.GetQueueLength()}{new string(' ', 45)}║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        // Simulate jobs arriving
        System.Console.WriteLine("🚀 Simulating job arrivals...\n");

        var jobCount = 15; // Will process 15 jobs
        var jobInterval = 1000; // Job arrives every 1 second

        for (int i = 0; i < jobCount; i++)
        {
            var priority = i % 5 == 0 ? "High" : "Normal";

            System.Console.WriteLine($"📨 Job {i + 1:D2}/{jobCount} arriving (Priority: {priority})");

            await orchestrator.SendEventAsync("SYSTEM", "MASTER_SCHEDULER_001", "JOB_ARRIVED", new
            {
                jobId = $"JOB_{i + 1:D3}",
                priority = priority
            });

            await Task.Delay(jobInterval);
        }

        System.Console.WriteLine();
        System.Console.WriteLine("⏳ All jobs submitted. Processing...\n");

        // Let the system process jobs
        await Task.Delay(40000); // Wait 40 seconds for processing

        System.Console.WriteLine();
        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  Final System Status                                         ║");
        System.Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        System.Console.WriteLine($"║  Master Scheduler:  {masterScheduler.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  CMP Tool 1:        {cmpTool1.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  CMP Tool 2:        {cmpTool2.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  CMP Tool 3:        {cmpTool3.GetCurrentState(),-38} ║");
        System.Console.WriteLine($"║  Current WIP:       {masterScheduler.GetCurrentWip()}/3{new string(' ', 42)}║");
        System.Console.WriteLine($"║  Queue Length:      {masterScheduler.GetQueueLength()}{new string(' ', 45)}║");
        System.Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        System.Console.WriteLine("║  Tool Performance                                            ║");
        System.Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        System.Console.WriteLine($"║  CMP Tool 1:  {cmpTool1.GetWafersProcessed(),3} wafers  Slurry: {cmpTool1.GetSlurryLevel(),5:F1}%  Pad: {cmpTool1.GetPadWear(),5:F1}% ║");
        System.Console.WriteLine($"║  CMP Tool 2:  {cmpTool2.GetWafersProcessed(),3} wafers  Slurry: {cmpTool2.GetSlurryLevel(),5:F1}%  Pad: {cmpTool2.GetPadWear(),5:F1}% ║");
        System.Console.WriteLine($"║  CMP Tool 3:  {cmpTool3.GetWafersProcessed(),3} wafers  Slurry: {cmpTool3.GetSlurryLevel(),5:F1}%  Pad: {cmpTool3.GetPadWear(),5:F1}% ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  Demo Complete!                                              ║");
        System.Console.WriteLine("║                                                              ║");
        System.Console.WriteLine("║  This demonstrates XStateNet's production-ready capability   ║");
        System.Console.WriteLine("║  for hierarchical scheduler coordination in semiconductor    ║");
        System.Console.WriteLine("║  manufacturing environments.                                 ║");
        System.Console.WriteLine("║                                                              ║");
        System.Console.WriteLine("║  Key Features Demonstrated:                                  ║");
        System.Console.WriteLine("║  ✅ Master-subordinate scheduler architecture                ║");
        System.Console.WriteLine("║  ✅ Priority-based job queuing (High/Normal)                 ║");
        System.Console.WriteLine("║  ✅ WIP control and load balancing                           ║");
        System.Console.WriteLine("║  ✅ Tool selection algorithm (scoring-based)                 ║");
        System.Console.WriteLine("║  ✅ Consumable tracking (slurry, pad wear)                   ║");
        System.Console.WriteLine("║  ✅ Automatic PM scheduling (wafer count + wear)             ║");
        System.Console.WriteLine("║  ✅ Guards, services, and orchestrated communication         ║");
        System.Console.WriteLine("║                                                              ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();
    }
}