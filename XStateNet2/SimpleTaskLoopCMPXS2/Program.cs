using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace SimpleTaskLoopCMPXS2;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Parse wafer count from args (default: 25)
        int waferCount = 25;
        if (args.Length > 0 && int.TryParse(args[0], out int count))
        {
            waferCount = count;
        }

        Console.WriteLine("===========================================");
        Console.WriteLine("   XSTATENET2 CMP SCHEDULER");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        var startTime = DateTime.UtcNow;

        // Create Akka.NET ActorSystem for XStateNet2
        var actorSystem = ActorSystem.Create("CMPSimulation");

        try
        {
            // Create XState scheduler
            var scheduler = new XStateScheduler(actorSystem, waferCount);

            Logger.Log("[Main] Starting XStateNet2 scheduler...");
            Logger.Log($"[Main] Processing {waferCount} wafers");

            // Start scheduler (runs until all wafers processed)
            var schedulerTask = scheduler.StartAsync();

            // Wait for completion
            await schedulerTask;

            Logger.Log($"[Main] All {waferCount} wafers processed! Stopping scheduler...");
            scheduler.Stop();

            // Wait for scheduler to fully stop
            await Task.Delay(500);

            var endTime = DateTime.UtcNow;
            var totalTime = (endTime - startTime).TotalSeconds;

            // Print statistics
            scheduler.PrintStatistics();

            Logger.Log("");
            Logger.Log("===========================================");
            Logger.Log("      PROCESSING TIME STATISTICS");
            Logger.Log("===========================================");
            Logger.Log($"Wafer Count: {waferCount}");
            Logger.Log($"Total Time: {totalTime:F2} seconds");
            Logger.Log($"Throughput: {waferCount / totalTime:F2} wafers/second");
            Logger.Log("===========================================");

            // Print alarm summary
            PrintAlarmSummary();
        }
        finally
        {
            // Shutdown actor system
            await actorSystem.Terminate();
        }
    }

    private static void PrintAlarmSummary()
    {
        Logger.Log("");
        Logger.Log("===========================================");
        Logger.Log("         ALARM SUMMARY");
        Logger.Log("===========================================");

        var alarms = AlarmManager.GetAlarms();
        if (alarms.Count == 0)
        {
            Logger.Log("No alarms raised - Process completed successfully!");
        }
        else
        {
            Logger.Log($"Total Alarms: {alarms.Count}");
            Logger.Log($"  - Critical: {AlarmManager.GetAlarmCount(AlarmLevel.Critical)}");
            Logger.Log($"  - Error: {AlarmManager.GetAlarmCount(AlarmLevel.Error)}");
            Logger.Log($"  - Warning: {AlarmManager.GetAlarmCount(AlarmLevel.Warning)}");

            Logger.Log("");
            Logger.Log("Alarm Details:");
            foreach (var alarm in alarms)
            {
                Logger.Log($"  [{alarm.Level}] {alarm.Code}: {alarm.Message}");
            }
        }
        Logger.Log("===========================================");
    }
}
