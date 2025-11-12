using System;
using System.Threading.Tasks;

namespace SimpleTaskLoopCMP;

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
        Console.WriteLine("   SIMPLE TASKLOOP CMP SCHEDULER");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        var startTime = DateTime.UtcNow;

        // Create stations
        var carrier = new Carrier(waferCount);
        var polisher = new Polisher();
        var cleaner = new Cleaner();
        var buffer = new Buffer();

        // Create robots
        var robot1 = new Robot("R1");
        var robot2 = new Robot("R2");
        var robot3 = new Robot("R3");

        // Create TaskLoop scheduler
        var scheduler = new SimpleTaskLoopScheduler(
            robot1, robot2, robot3,
            carrier, polisher, cleaner, buffer
        );

        Logger.Log("[Main] Starting SimpleTaskLoop scheduler...");

        // Simulate carrier arrival with wafers
        await Task.Delay(100);
        carrier.IsArrived = true;
        Logger.Log($"[Main] Carrier arrived with {waferCount} wafers to process");

        // Start scheduler (runs until all wafers processed)
        var schedulerTask = scheduler.StartAsync();

        // Monitor completion - check if all wafers in carrier are processed
        while (!carrier.AllProcessed)
        {
            await Task.Delay(100);
        }

        // All wafers processed - stop scheduler
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

    private static void PrintAlarmSummary()
    {
        Logger.Log("");
        Logger.Log("===========================================");
        Logger.Log("         ALARM SUMMARY");
        Logger.Log("===========================================");

        var alarms = AlarmManager.GetAlarms();
        if (alarms.Count == 0)
        {
            Logger.Log("✓ No alarms raised - Process completed successfully!");
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
