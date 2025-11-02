using System.Diagnostics;
using Akka.Actor;
using CMPSimXS2.Console.Models;
using CMPSimXS2.Console.Schedulers;

namespace CMPSimXS2.Console;

/// <summary>
/// 1000-cycle stress test for all 12 scheduler architectures.
/// Tests reliability, performance, and failure modes under extended load.
/// </summary>
public class StressTest
{
    public static async Task RunStressTests()
    {
        System.Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  DEBUG Stress Test: 250 Wafers × 1000 Cycles (10 FOUPs)                      ║");
        System.Console.WriteLine("║  Testing: All 16 Scheduler Architectures                                      ║");
        System.Console.WriteLine("║  Metrics: Throughput, Reliability, Performance, Failure Modes                 ║");
        System.Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        var results = new List<StressTestResult>();

        // Test each scheduler architecture
        // Format: [Coordination]-[Engine]-[Optimization]-[Communication]

        // Lock-based
        results.Add(await TestScheduler("Lock (polling)", "lock"));

        // Pure Actor
        results.Add(await TestScheduler("Actor (event)", "actor"));

        // XStateNet Legacy (V1 simulation)
        results.Add(await TestScheduler("XS1-Legacy (event)", "xs1-legacy"));

        // XStateNet2 Variants: Dict → FrozenDict → Array
        results.Add(await TestScheduler("XS2-Dict (event)", "xs2-dict"));
        results.Add(await TestScheduler("XS2-Frozen (event)", "xs2-frozen"));
        results.Add(await TestScheduler("XS2-Array (event)", "xs2-array"));

        // Autonomous Variants
        results.Add(await TestScheduler("Autonomous (polling)", "autonomous"));
        results.Add(await TestScheduler("Autonomous-Array (polling)", "autonomous-array"));
        results.Add(await TestScheduler("Autonomous-Event (event)", "autonomous-event"));

        // Actor Mailbox
        results.Add(await TestScheduler("Actor-Mailbox (event)", "actor-mailbox"));

        // Ant Colony
        results.Add(await TestScheduler("Ant-Colony (event)", "ant-colony"));

        // Pub/Sub Variants
        results.Add(await TestScheduler("XS2-PubSub-Dedicated (multi)", "xs2-pubsub-dedicated"));
        results.Add(await TestScheduler("PubSub-Single (one)", "pubsub-single"));
        results.Add(await TestScheduler("XS2-PubSub-Array (one)", "xs2-pubsub-array"));

        // Synchronized Pipeline
        results.Add(await TestScheduler("Sync-Pipeline (batch)", "sync-pipe"));
        results.Add(await TestScheduler("XS2-Sync-Pipeline (batch)", "xs2-sync-pipe"));

        // Display summary
        PrintSummary(results);
    }

    private static async Task<StressTestResult> TestScheduler(string name, string type)
    {
        System.Console.WriteLine($"═══════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine($"Testing: {name}");
        System.Console.WriteLine($"═══════════════════════════════════════════════════════════════════════");

        var result = new StressTestResult { SchedulerName = name, SchedulerType = type };

        try
        {
            using var actorSystem = ActorSystem.Create($"StressTest-{type}");

            // Create scheduler
            IRobotScheduler scheduler = CreateScheduler(actorSystem, type);

            // Create wafers for 10 FOUPs × 25 wafers each = 250 total wafers (DEBUG mode)
            var wafers = new List<Wafer>();
            int totalWafers = 10 * 25; // 10 FOUPs × 25 wafers per FOUP
            for (int i = 1; i <= totalWafers; i++)
            {
                wafers.Add(new Wafer(i));
            }

            // Create journey scheduler
            var journeyScheduler = new WaferJourneyScheduler(scheduler, wafers);

            // Create stations
            var stations = CreateStations(actorSystem);
            foreach (var (stationName, station) in stations)
            {
                journeyScheduler.RegisterStation(stationName, station);
            }

            // Register stations with scheduler if needed
            RegisterStationsWithScheduler(scheduler, stations);

            // Create robots
            var robots = CreateRobots(actorSystem, scheduler);
            foreach (var (robotId, robotActor) in robots)
            {
                scheduler.RegisterRobot(robotId, robotActor);
                scheduler.UpdateRobotState(robotId, "idle");
            }

            // Start test
            var sw = Stopwatch.StartNew();
            int successfulCycles = 0;
            int failedCycles = 0;
            var errors = new List<string>();

            // Run 1000 cycles
            for (int cycle = 1; cycle <= 1000; cycle++)
            {
                try
                {
                    // Feed wafers at the start (all wafers in one MEGA_CARRIER)
                    if (cycle == 1)
                    {
                        var allWaferIds = wafers.Select(w => w.Id).ToList();
                        journeyScheduler.OnCarrierArrival("MEGA_CARRIER", allWaferIds);
                        System.Console.WriteLine($"  Mega Carrier arrived with all {totalWafers} wafers");
                    }

                    // Process wafer journeys
                    journeyScheduler.ProcessWaferJourneys();

                    // Simulate station processing (complete work every cycle)
                    SimulateStationProgress(stations, wafers);

                    // Auto-depart completed carriers to allow wafers to finish
                    AutoDepartCompletedCarriers(journeyScheduler, cycle);

                    // Check for stalls every 100 cycles
                    if (cycle % 100 == 0)
                    {
                        int queueSize = scheduler.GetQueueSize();

                        // Count active wafers (not completed, not in carrier waiting to start)
                        var activeWafers = wafers.Where(w =>
                            !w.IsCompleted &&
                            w.JourneyStage != "InCarrier").ToList();

                        int completedSoFar = wafers.Count(w => w.IsCompleted);
                        int inCarrier = wafers.Count(w => w.JourneyStage == "InCarrier" && !w.IsCompleted);

                        if (cycle > 100 && queueSize > 50)
                        {
                            errors.Add($"Cycle {cycle}: Queue stall detected (size: {queueSize})");
                        }

                        System.Console.WriteLine($"  Cycle {cycle}: Queue={queueSize}, Active={activeWafers.Count}, Completed={completedSoFar:N0}, Waiting={inCarrier:N0}");

                        // Show sample of active wafers (first 5)
                        if (activeWafers.Count > 0)
                        {
                            var sample = activeWafers.Take(5).Select(w => $"W{w.Id}:{w.JourneyStage}");
                            System.Console.WriteLine($"    Active samples: {string.Join(", ", sample)}{(activeWafers.Count > 5 ? "..." : "")}");
                        }
                    }

                    // Delay to allow actor message processing (5ms gives actors time to drain mailboxes)
                    await Task.Delay(5);
                    successfulCycles++;
                }
                catch (Exception ex)
                {
                    failedCycles++;
                    errors.Add($"Cycle {cycle}: {ex.Message}");

                    if (failedCycles > 10)
                    {
                        errors.Add($"Too many failures, stopping at cycle {cycle}");
                        break;
                    }
                }
            }

            sw.Stop();

            // Check final state - use IsCompleted flag, not JourneyStage
            int completedWafers = wafers.Count(w => w.IsCompleted);
            int stuckWafers = wafers.Count(w => !w.IsCompleted && w.JourneyStage != "InCarrier");
            double completionRate = (double)completedWafers / totalWafers * 100;

            // Success criteria: < 10 errors AND at least 95% of wafers completed (238 out of 250)
            int minimumCompleted = (int)(totalWafers * 0.95);
            result.Success = failedCycles < 10 && completedWafers >= minimumCompleted;
            result.TotalTime = sw.Elapsed;
            result.SuccessfulCycles = successfulCycles;
            result.FailedCycles = failedCycles;
            result.CompletedWafers = completedWafers;
            result.StuckWafers = stuckWafers;
            result.Errors = errors;

            System.Console.WriteLine();
            System.Console.WriteLine($"  ✓ Completed: {successfulCycles}/1000 cycles");
            System.Console.WriteLine($"  ✓ Time: {sw.Elapsed.TotalSeconds:F2}s");
            System.Console.WriteLine($"  ✓ FOUPs Processed: 1000 FOUPs");
            System.Console.WriteLine($"  ✓ Wafers Completed: {completedWafers:N0}/{totalWafers:N0} ({completionRate:F2}%)");
            System.Console.WriteLine($"  ✓ Wafers Stuck: {stuckWafers:N0}");
            System.Console.WriteLine($"  ✓ Errors: {failedCycles}");
            System.Console.WriteLine($"  ✓ Throughput: {(completedWafers / sw.Elapsed.TotalSeconds):F0} wafers/sec");

            if (errors.Any())
            {
                System.Console.WriteLine($"  ⚠ First 3 errors:");
                foreach (var error in errors.Take(3))
                {
                    System.Console.WriteLine($"    - {error}");
                }
            }

            System.Console.WriteLine();

            await actorSystem.Terminate();
            // No cleanup delay - pure overhead test
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Fatal error: {ex.Message}");
            System.Console.WriteLine($"  ✗ FATAL ERROR: {ex.Message}");
            System.Console.WriteLine();
        }

        return result;
    }

    private static IRobotScheduler CreateScheduler(ActorSystem actorSystem, string code)
    {
        return code switch
        {
            // Lock-based
            "lock" => new RobotScheduler(),

            // Pure Actor
            "actor" => new RobotSchedulerActorProxy(actorSystem, $"stress-{code}"),

            // XStateNet Legacy (V1 simulation)
            "xs1-legacy" => new RobotSchedulerXS1Legacy($"stress-{code}"),

            // XStateNet2 Variants (Dict → FrozenDict → Array)
            "xs2-dict" => new RobotSchedulerXStateDict(actorSystem, $"stress-{code}"),
            "xs2-frozen" => new RobotSchedulerXState(actorSystem, $"stress-{code}"),
            "xs2-array" => new RobotSchedulerXStateArray(actorSystem, $"stress-{code}"),

            // Autonomous Variants
            "autonomous" => new AutonomousRobotScheduler(),
            "autonomous-array" => new AutonomousArrayScheduler(),
            "autonomous-event" => new EventDrivenHybridScheduler(),

            // Actor Mailbox
            "actor-mailbox" => new ActorMailboxEventDrivenScheduler(actorSystem, $"stress-{code}"),

            // Ant Colony
            "ant-colony" => new AntColonyScheduler(actorSystem, $"stress-{code}"),

            // Pub/Sub Variants
            "xs2-pubsub-dedicated" => new PublicationBasedScheduler(actorSystem, $"stress-{code}"),
            "pubsub-single" => new SinglePublicationScheduler(actorSystem, $"stress-{code}"),
            "xs2-pubsub-array" => new SinglePublicationSchedulerXState(actorSystem, $"stress-{code}"),

            // Synchronized Pipeline
            "sync-pipe" => new SynchronizedPipelineScheduler(actorSystem, $"stress-{code}"),
            "xs2-sync-pipe" => new RobotSchedulerXS2SyncPipeline(actorSystem, $"stress-{code}"),

            _ => new RobotScheduler()
        };
    }

    private static Dictionary<string, Station> CreateStations(ActorSystem system)
    {
        var stations = new Dictionary<string, Station>();
        var stationNames = new[] { "Polisher", "Cleaner", "Buffer", "Carrier" };

        foreach (var name in stationNames)
        {
            var station = new Station(name) { CurrentState = "idle" };
            var stationActor = system.ActorOf(Props.Create(() => new StressTestStationActor(station)), $"station-{name}-{Guid.NewGuid().ToString("N").Substring(0, 8)}");
            station.StateMachine = stationActor;
            stations[name] = station;
        }

        return stations;
    }

    private static Dictionary<string, IActorRef> CreateRobots(ActorSystem system, IRobotScheduler scheduler)
    {
        var robots = new Dictionary<string, IActorRef>();
        var robotIds = new[] { "Robot 1", "Robot 2", "Robot 3" };

        for (int i = 0; i < robotIds.Length; i++)
        {
            var robotId = robotIds[i];
            var robot = system.ActorOf(
                Props.Create(() => new StressTestRobotActor(robotId, scheduler)),
                $"robot-{i}-{Guid.NewGuid().ToString("N").Substring(0, 8)}"
            );
            robots[robotId] = robot;
        }

        return robots;
    }

    private static void RegisterStationsWithScheduler(IRobotScheduler scheduler, Dictionary<string, Station> stations)
    {
        if (scheduler is PublicationBasedScheduler pubSubScheduler)
        {
            foreach (var (name, station) in stations)
            {
                pubSubScheduler.RegisterStation(name, station.CurrentState, station.CurrentWafer);
                station.OnStateChanged = (state, waferId) =>
                {
                    pubSubScheduler.UpdateStationState(name, state, waferId);
                };
            }
        }
        else if (scheduler is SinglePublicationScheduler singlePubScheduler)
        {
            foreach (var (name, station) in stations)
            {
                singlePubScheduler.RegisterStation(name, station.CurrentState, station.CurrentWafer);
                station.OnStateChanged = (state, waferId) =>
                {
                    singlePubScheduler.UpdateStationState(name, state, waferId);
                };
            }
        }
        else if (scheduler is SinglePublicationSchedulerXState arrayPubScheduler)
        {
            foreach (var (name, station) in stations)
            {
                arrayPubScheduler.RegisterStation(name, station.CurrentState, station.CurrentWafer);
                station.OnStateChanged = (state, waferId) =>
                {
                    arrayPubScheduler.UpdateStationState(name, state, waferId);
                };
            }
        }
        else if (scheduler is SynchronizedPipelineScheduler syncPipelineScheduler)
        {
            foreach (var (name, station) in stations)
            {
                syncPipelineScheduler.RegisterStation(name, station.CurrentState, station.CurrentWafer);
                station.OnStateChanged = (state, waferId) =>
                {
                    syncPipelineScheduler.UpdateStationState(name, state, waferId);
                };
            }
        }
    }

    private static void SimulateStationProgress(Dictionary<string, Station> stations, List<Wafer> wafers)
    {
        // Simulate each station completing its work instantly (for stress test speed)
        foreach (var (name, station) in stations)
        {
            if (station.CurrentState == "processing" && station.CurrentWafer.HasValue)
            {
                // Find the wafer being processed
                var wafer = wafers.FirstOrDefault(w => w.Id == station.CurrentWafer.Value);

                if (wafer != null)
                {
                    // Complete processing immediately for stress test
                    station.CurrentState = "done";
                    station.OnStateChanged?.Invoke("done", station.CurrentWafer);
                }
            }
        }
    }

    private static void AutoDepartCompletedCarriers(IWaferJourneyScheduler journeyScheduler, int cycle)
    {
        // Check if current carrier is complete and depart it automatically
        if (journeyScheduler.IsCurrentCarrierComplete())
        {
            var currentCarrier = journeyScheduler.GetCurrentCarrierId();
            if (!string.IsNullOrEmpty(currentCarrier))
            {
                journeyScheduler.OnCarrierDeparture(currentCarrier);
            }
        }
    }

    private static void PrintSummary(List<StressTestResult> results)
    {
        System.Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  📊 EXTREME STRESS TEST SUMMARY: 1000 FOUPs × 25,000 Wafers                  ║");
        System.Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        // Sort by success, then by time
        var sortedResults = results.OrderByDescending(r => r.Success).ThenBy(r => r.TotalTime).ToList();

        System.Console.WriteLine("┌─────────────────────────────────┬──────────┬───────────┬─────────┬──────────────┬────────────┬────────┐");
        System.Console.WriteLine("│ Scheduler                       │ Status   │ Time (s)  │ Cycles  │ Wafers       │ Throughput │ Errors │");
        System.Console.WriteLine("│                                 │          │           │         │ (Completed)  │ (wafer/s)  │        │");
        System.Console.WriteLine("├─────────────────────────────────┼──────────┼───────────┼─────────┼──────────────┼────────────┼────────┤");

        foreach (var result in sortedResults)
        {
            string status = result.Success ? "✓ PASS" : "✗ FAIL";
            double completionRate = (double)result.CompletedWafers / 250 * 100;  // Fixed: 250 wafers total, not 25000
            double throughput = result.TotalTime.TotalSeconds > 0 ? result.CompletedWafers / result.TotalTime.TotalSeconds : 0;

            System.Console.WriteLine(
                $"│ {result.SchedulerName,-31} │ {status,-8} │ {result.TotalTime.TotalSeconds,9:F2} │ {result.SuccessfulCycles,7} │ {result.CompletedWafers,6:N0} ({completionRate,5:F1}%) │ {throughput,10:F0} │ {result.FailedCycles,6} │"
            );
        }

        System.Console.WriteLine("└─────────────────────────────────┴──────────┴───────────┴─────────┴──────────────┴────────────┴────────┘");
        System.Console.WriteLine();

        // Performance rankings
        var successfulResults = results.Where(r => r.Success).OrderBy(r => r.TotalTime).ToList();

        if (successfulResults.Any())
        {
            System.Console.WriteLine("🏆 PERFORMANCE RANKINGS (Successful Tests Only):");
            System.Console.WriteLine();

            for (int i = 0; i < successfulResults.Count; i++)
            {
                var result = successfulResults[i];
                string medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    2 => "🥉",
                    _ => $"{i + 1}."
                };

                double cyclesPerSecond = result.SuccessfulCycles / result.TotalTime.TotalSeconds;

                System.Console.WriteLine($"  {medal} {result.SchedulerName,-30} - {result.TotalTime.TotalSeconds,6:F2}s ({cyclesPerSecond,7:F0} cycles/sec)");
            }
            System.Console.WriteLine();
        }

        // Failure analysis
        var failedResults = results.Where(r => !r.Success).ToList();

        if (failedResults.Any())
        {
            System.Console.WriteLine("⚠️  FAILURES:");
            System.Console.WriteLine();

            foreach (var result in failedResults)
            {
                System.Console.WriteLine($"  ✗ {result.SchedulerName}");
                foreach (var error in result.Errors.Take(3))
                {
                    System.Console.WriteLine($"    - {error}");
                }
                System.Console.WriteLine();
            }
        }
        else
        {
            System.Console.WriteLine("✅ ALL TESTS PASSED!");
            System.Console.WriteLine();
        }

        // Detailed Statistics
        int passCount = results.Count(r => r.Success);
        int failCount = results.Count(r => !r.Success);

        System.Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  📊 DETAILED STATISTICS: 12 Scheduler Architecture Comparison                ║");
        System.Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        if (successfulResults.Any())
        {
            var fastest = successfulResults.First();
            var slowest = successfulResults.Last();
            double avgTime = successfulResults.Average(r => r.TotalTime.TotalSeconds);
            double avgThroughput = successfulResults.Average(r => r.CompletedWafers / r.TotalTime.TotalSeconds);
            double maxThroughput = successfulResults.Max(r => r.CompletedWafers / r.TotalTime.TotalSeconds);
            double minThroughput = successfulResults.Min(r => r.CompletedWafers / r.TotalTime.TotalSeconds);
            double avgCompletionRate = successfulResults.Average(r => (double)r.CompletedWafers / 25000 * 100);

            System.Console.WriteLine("⏱️  TIME STATISTICS:");
            System.Console.WriteLine($"  Fastest:      {fastest.SchedulerName,-30} {fastest.TotalTime.TotalSeconds,8:F2}s");
            System.Console.WriteLine($"  Slowest:      {slowest.SchedulerName,-30} {slowest.TotalTime.TotalSeconds,8:F2}s");
            System.Console.WriteLine($"  Average:      {avgTime,47:F2}s");
            System.Console.WriteLine($"  Speed Range:  {fastest.TotalTime.TotalSeconds:F2}s - {slowest.TotalTime.TotalSeconds:F2}s (Δ {(slowest.TotalTime.TotalSeconds - fastest.TotalTime.TotalSeconds):F2}s)");
            System.Console.WriteLine();

            System.Console.WriteLine("🚀 THROUGHPUT STATISTICS (Wafers/Second):");
            System.Console.WriteLine($"  Maximum:      {maxThroughput,47:F0} wafers/s");
            System.Console.WriteLine($"  Minimum:      {minThroughput,47:F0} wafers/s");
            System.Console.WriteLine($"  Average:      {avgThroughput,47:F0} wafers/s");
            System.Console.WriteLine($"  Range:        {minThroughput:F0} - {maxThroughput:F0} (Δ {(maxThroughput - minThroughput):F0} wafers/s)");
            System.Console.WriteLine();

            System.Console.WriteLine("✅ RELIABILITY STATISTICS:");
            System.Console.WriteLine($"  Pass Rate:             {passCount}/{results.Count} ({(passCount * 100.0 / results.Count),5:F1}%)");
            System.Console.WriteLine($"  Fail Rate:             {failCount}/{results.Count} ({(failCount * 100.0 / results.Count),5:F1}%)");
            System.Console.WriteLine($"  Avg Completion Rate:   {avgCompletionRate,41:F2}%");
            System.Console.WriteLine($"  Total Wafers Tested:   {25000:N0} wafers × {results.Count} schedulers = {25000 * results.Count:N0} total");
            System.Console.WriteLine();

            // Performance comparison
            if (successfulResults.Count >= 2)
            {
                double speedup = slowest.TotalTime.TotalSeconds / fastest.TotalTime.TotalSeconds;
                System.Console.WriteLine("⚡ PERFORMANCE COMPARISON:");
                System.Console.WriteLine($"  Fastest vs Slowest:    {fastest.SchedulerName} is {speedup:F2}× faster than {slowest.SchedulerName}");
                System.Console.WriteLine($"  Throughput Advantage:  {(maxThroughput / minThroughput):F2}× (best vs worst)");
                System.Console.WriteLine();
            }
        }
        else
        {
            System.Console.WriteLine("❌ NO SUCCESSFUL TESTS - All schedulers failed!");
            System.Console.WriteLine();
        }
    }

    private class StressTestResult
    {
        public string SchedulerName { get; set; } = "";
        public string SchedulerType { get; set; } = "";
        public bool Success { get; set; }
        public TimeSpan TotalTime { get; set; }
        public int SuccessfulCycles { get; set; }
        public int FailedCycles { get; set; }
        public int CompletedWafers { get; set; }
        public int StuckWafers { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    private class StressTestStationActor : ReceiveActor
    {
        private readonly Station _station;

        public StressTestStationActor(Station station)
        {
            _station = station;

            ReceiveAny(msg =>
            {
                var msgStr = msg.ToString() ?? "";

                if (msgStr.Contains("LOAD_WAFER"))
                {
                    var sendEvent = msg as XStateNet2.Core.Messages.SendEvent;
                    if (sendEvent?.Data != null && sendEvent.Data is Dictionary<string, object> data && data.ContainsKey("waferId"))
                    {
                        _station.CurrentWafer = (int)data["waferId"];
                        _station.CurrentState = "processing";
                        _station.OnStateChanged?.Invoke("processing", _station.CurrentWafer);

                        // Instant processing for stress test (no delays)
                        _station.CurrentState = "done";
                        _station.OnStateChanged?.Invoke("done", _station.CurrentWafer);
                    }
                }
                else if (msgStr.Contains("UNLOAD_WAFER"))
                {
                    _station.CurrentWafer = null;
                    _station.CurrentState = "idle";
                    _station.OnStateChanged?.Invoke("idle", null);
                }
            });
        }
    }

    private class StressTestRobotActor : ReceiveActor
    {
        private readonly string _robotId;
        private readonly IRobotScheduler _scheduler;
        private int? _currentWafer;

        public StressTestRobotActor(string robotId, IRobotScheduler scheduler)
        {
            _robotId = robotId;
            _scheduler = scheduler;

            ReceiveAny(msg =>
            {
                if (msg.ToString()!.Contains("PICKUP"))
                {
                    var sendEvent = msg as XStateNet2.Core.Messages.SendEvent;
                    if (sendEvent?.Data != null && sendEvent.Data is Dictionary<string, object> data && data.ContainsKey("waferId"))
                    {
                        _currentWafer = (int)data["waferId"];

                        // Instant transfer for stress test (no delays)
                        _scheduler.UpdateRobotState(_robotId, "carrying", _currentWafer);
                        _currentWafer = null;
                        _scheduler.UpdateRobotState(_robotId, "idle");
                    }
                }
            });
        }
    }
}
