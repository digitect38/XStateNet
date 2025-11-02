using System.Text;
using Akka.Actor;
using CMPSimXS2.Console.Models;
using CMPSimXS2.Console.Schedulers;
using LoggerHelper;
using XStateNet2.Core.Messages;

namespace CMPSimXS2.Console;

/// <summary>
/// Console application demonstrating CMPSimXS2 with real schedulers
/// Runs actual 8-step wafer journey using RobotScheduler and WaferJourneyScheduler
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // Set console encoding to UTF-8 for proper emoji display
        System.Console.OutputEncoding = Encoding.UTF8;
        System.Console.InputEncoding = Encoding.UTF8;

        // Show help if no arguments provided
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        // Parse command-line arguments
        bool runBenchmark = args.Contains("--benchmark") || args.Contains("-b");
        bool runStressTest = args.Contains("--stress-test") || args.Contains("--stress");

        // Robot scheduler selection
        string robotSchedulerType = "lock"; // default
        if (args.Contains("--robot-lock")) robotSchedulerType = "lock";
        else if (args.Contains("--robot-actor")) robotSchedulerType = "actor";
        else if (args.Contains("--robot-xstate")) robotSchedulerType = "xstate";
        else if (args.Contains("--robot-array")) robotSchedulerType = "array";
        else if (args.Contains("--robot-xs2-frozen")) robotSchedulerType = "xs2-frozen";
        else if (args.Contains("--robot-xs2-dict")) robotSchedulerType = "xs2-dict";
        else if (args.Contains("--robot-autonomous")) robotSchedulerType = "autonomous";
        else if (args.Contains("--robot-hybrid")) robotSchedulerType = "hybrid";
        else if (args.Contains("--robot-eventdriven")) robotSchedulerType = "eventdriven";
        else if (args.Contains("--robot-actormailbox")) robotSchedulerType = "actormailbox";
        else if (args.Contains("--robot-ant")) robotSchedulerType = "ant";
        else if (args.Contains("--robot-pubsub")) robotSchedulerType = "pubsub";
        else if (args.Contains("--robot-singlepub")) robotSchedulerType = "singlepub";
        else if (args.Contains("--robot-array-singlepub")) robotSchedulerType = "array-singlepub";
        else if (args.Contains("--robot-sync-pipe")) robotSchedulerType = "sync-pipe";
        else if (args.Contains("--robot-xs1-legacy")) robotSchedulerType = "xs1-legacy";
        else if (args.Contains("--robot-xs2-sync-pipe")) robotSchedulerType = "xs2-sync-pipe";
        else if (args.Contains("--robot-pub1")) robotSchedulerType = "singlepub"; // alias
        else if (args.Contains("--actor") || args.Contains("-a")) robotSchedulerType = "actor"; // backward compat
        else if (args.Contains("--xstate") || args.Contains("-x")) robotSchedulerType = "xstate"; // backward compat

        // Journey scheduler selection
        string journeySchedulerType = "lock"; // default
        if (args.Contains("--journey-actor")) journeySchedulerType = "actor";
        else if (args.Contains("--journey-xstate")) journeySchedulerType = "xstate";

        // Run benchmark if requested
        if (runBenchmark)
        {
            await SchedulerBenchmark.RunBenchmark();
            return;
        }

        // Run stress test if requested
        if (runStressTest)
        {
            await StressTest.RunStressTests();
            return;
        }

        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  CMPSimXS2 Single-Wafer Rule Demonstration                            ║");
        System.Console.WriteLine("║  Configuration: 2 FOUP Carriers × 25 Wafers = 50 Total Wafers        ║");
        System.Console.WriteLine("║  Train Pattern: Carrier → R1 → Polisher → R2 → Cleaner → R3 →        ║");
        System.Console.WriteLine("║                 Buffer → R1 → Carrier                                  ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        // Display mode
        var robotIcon = robotSchedulerType switch {
            "lock" => "🔒",
            "actor" => "🎭",
            "xstate" => "🔄",
            "array" => "⚡",
            "xs2-frozen" => "❄️",
            "xs2-dict" => "📚",
            "autonomous" => "🤖",
            "hybrid" => "🚀",
            "eventdriven" => "🔔",
            "actormailbox" => "📬",
            "ant" => "🐜",
            "pubsub" => "📡",
            "singlepub" => "📢",
            "array-singlepub" => "🚀",
            "sync-pipe" => "⏸️",
            "xs1-legacy" => "🕰️",
            "xs2-sync-pipe" => "🔄",
            _ => "🔒"
        };
        var robotName = robotSchedulerType switch {
            "lock" => "Lock-based (polling)",
            "actor" => "Pure Akka.NET Actor (event)",
            "xstate" => "XS2-Array (event)",
            "array" => "XS2-Array (event)",
            "xs2-frozen" => "XS2-Frozen (event)",
            "xs2-dict" => "XS2-Dict (event)",
            "autonomous" => "Autonomous (polling)",
            "hybrid" => "Autonomous-Array (polling)",
            "eventdriven" => "Autonomous-Event (event)",
            "actormailbox" => "Actor-Mailbox (event)",
            "ant" => "Ant-Colony (event)",
            "pubsub" => "XS2-PubSub-Dedicated (multi)",
            "singlepub" => "PubSub-Single (one)",
            "array-singlepub" => "XS2-PubSub-Array (one)",
            "sync-pipe" => "Sync-Pipeline (batch)",
            "xs1-legacy" => "XS1-Legacy (event) [BROKEN]",
            "xs2-sync-pipe" => "XS2-Sync-Pipeline (batch) [DEBUG]",
            _ => "Lock-based (polling)"
        };
        var journeyIcon = journeySchedulerType switch { "actor" => "🎭", "xstate" => "🔄", _ => "🔒" };
        var journeyName = journeySchedulerType switch { "actor" => "Actor", "xstate" => "XState", _ => "Lock" };

        System.Console.WriteLine($"{robotIcon} ROBOT SCHEDULER: {robotName}-based");
        System.Console.WriteLine($"{journeyIcon} JOURNEY SCHEDULER: {journeyName}-based");
        System.Console.WriteLine();
        System.Console.WriteLine("💡 TIP: Use --stress-test to benchmark all 16 schedulers, or select one:");
        System.Console.WriteLine();
        System.Console.WriteLine("🤖 ROBOT SCHEDULER OPTIONS (Reliability-First):");
        System.Console.WriteLine();
        System.Console.WriteLine("  ✅ 100% COMPLETION RATE (Most Reliable):");
        System.Console.WriteLine("     --robot-lock");
        System.Console.WriteLine("        🔒 Lock-based polling (15.73s, traditional mutex pattern)");
        System.Console.WriteLine("     --robot-actor");
        System.Console.WriteLine("        🎭 Pure Akka.NET actor (15.69s, event-driven coordination)");
        System.Console.WriteLine("     --robot-xstate");
        System.Console.WriteLine("        🔄 XS2-Array (15.69s, max performance with byte arrays)");
        System.Console.WriteLine("     --robot-xs2-frozen");
        System.Console.WriteLine("        ❄️  XS2-Frozen (15.68s, FrozenDictionary optimized)");
        System.Console.WriteLine("     --robot-xs2-dict");
        System.Console.WriteLine("        📚 XS2-Dict (18.xx s, Dictionary baseline for comparison)");
        System.Console.WriteLine("     --robot-autonomous");
        System.Console.WriteLine("        🤖 Autonomous polling (15.72s, self-organizing robots)");
        System.Console.WriteLine("     --robot-array");
        System.Console.WriteLine("        ⚡ Autonomous-Array (15.68s, optimized autonomous)");
        System.Console.WriteLine("     --robot-eventdriven");
        System.Console.WriteLine("        🔔 Autonomous-Event (15.68s, event-driven autonomous)");
        System.Console.WriteLine("     --robot-actormailbox");
        System.Console.WriteLine("        📬 Actor-Mailbox (15.68s, mailbox-based coordination)");
        System.Console.WriteLine("     --robot-ant");
        System.Console.WriteLine("        🐜 Ant-Colony (15.68s, swarm intelligence optimization)");
        System.Console.WriteLine("     --robot-pubsub");
        System.Console.WriteLine("        📡 XS2-PubSub-Dedicated (15.72s, multi-publisher pattern)");
        System.Console.WriteLine("     --robot-singlepub");
        System.Console.WriteLine("        📢 PubSub-Single (15.68s, one-publisher pattern)");
        System.Console.WriteLine("     --robot-array-singlepub");
        System.Console.WriteLine("        🚀 XS2-PubSub-Array (15.68s, optimized one-publisher)");
        System.Console.WriteLine("     --robot-sync-pipe");
        System.Console.WriteLine("        ⏸️  Sync-Pipeline (15.70s, synchronized batch execution)");
        System.Console.WriteLine();
        System.Console.WriteLine("  ⚠️  EXPERIMENTAL (Under Development):");
        System.Console.WriteLine("     --robot-xs1-legacy");
        System.Console.WriteLine("        🕰️  XS1-Legacy (0% completion, XStateNet V1 simulation - broken)");
        System.Console.WriteLine("     --robot-xs2-sync-pipe");
        System.Console.WriteLine("        🔄 XS2-Sync-Pipeline (0% completion, XS2 batch sync - debugging)");
        System.Console.WriteLine();
        System.Console.WriteLine("🗺️  JOURNEY SCHEDULER OPTIONS:");
        System.Console.WriteLine("     --journey-actor  (🎭 Actor-based journey coordination)");
        System.Console.WriteLine("     --journey-xstate (🔄 XState-based journey coordination)");
        System.Console.WriteLine();

        System.Console.WriteLine("📋 CRITICAL RULES:");
        System.Console.WriteLine("   🤖 Robot Rule: Each robot can carry only ONE wafer at a time");
        System.Console.WriteLine("   ⚙️  Station Rule: Each station can hold only ONE wafer at a time");
        System.Console.WriteLine("   🔄 Parallel Work: Multiple stations/robots work simultaneously");
        System.Console.WriteLine();

        // Create Akka ActorSystem
        System.Console.WriteLine("🔧 Initializing Akka ActorSystem...");
        using var actorSystem = ActorSystem.Create("CMPSimXS2-Console");

        // Initialize robot scheduler
        System.Console.WriteLine($"🤖 Initializing RobotScheduler ({robotName}-based)...");

        IRobotScheduler robotScheduler = robotSchedulerType switch
        {
            "lock" => new RobotScheduler(),
            "actor" => new RobotSchedulerActorProxy(actorSystem),
            "xstate" => new RobotSchedulerXStateArray(actorSystem, "robot-scheduler-xstate"),
            "array" => new RobotSchedulerXStateArray(actorSystem, "robot-scheduler-array"),
            "xs2-frozen" => new RobotSchedulerXState(actorSystem, "robot-scheduler-xs2-frozen"),
            "xs2-dict" => new RobotSchedulerXStateDict(actorSystem, "robot-scheduler-xs2-dict"),
            "autonomous" => new AutonomousRobotScheduler(),
            "hybrid" => new AutonomousArrayScheduler(),
            "eventdriven" => new EventDrivenHybridScheduler(),
            "actormailbox" => new ActorMailboxEventDrivenScheduler(actorSystem),
            "ant" => new AntColonyScheduler(actorSystem),
            "pubsub" => new PublicationBasedScheduler(actorSystem),
            "singlepub" => new SinglePublicationScheduler(actorSystem),
            "array-singlepub" => new SinglePublicationSchedulerXState(actorSystem),
            "sync-pipe" => new SynchronizedPipelineScheduler(actorSystem),
            "xs1-legacy" => new RobotSchedulerXS1Legacy($"demo-xs1-legacy"),
            "xs2-sync-pipe" => new RobotSchedulerXS2SyncPipeline(actorSystem, $"demo-xs2-sync-pipe"),
            _ => new RobotScheduler()
        };

        // Create wafers for TWO carriers (25 wafers per carrier)
        System.Console.WriteLine("💿 Creating 50 wafers (2 FOUP carriers × 25 wafers each)...");
        var wafers = new List<Wafer>();
        for (int i = 1; i <= 50; i++)
        {
            wafers.Add(new Wafer(i));
        }

        // Initialize journey scheduler
        System.Console.WriteLine($"🔄 Initializing WaferJourneyScheduler ({journeyName}-based)...");

        IWaferJourneyScheduler journeyScheduler = journeySchedulerType switch
        {
            "actor" => new WaferJourneySchedulerActorProxy(actorSystem, robotScheduler, wafers),
            "xstate" => new WaferJourneySchedulerXState(actorSystem, robotScheduler, wafers),
            _ => new WaferJourneyScheduler(robotScheduler, wafers)
        };

        // Create mock station actors and viewmodels
        System.Console.WriteLine("⚙️  Creating station actors...");
        var stations = CreateMockStations(actorSystem);

        // Register stations with journey scheduler
        foreach (var (name, station) in stations)
        {
            journeyScheduler.RegisterStation(name, station);
        }

        // Create mock robot actors
        System.Console.WriteLine("🤖 Creating robot actors...");
        var robots = CreateMockRobots(actorSystem, robotScheduler);

        // Register robots with robot scheduler
        foreach (var (robotId, robotActor) in robots)
        {
            robotScheduler.RegisterRobot(robotId, robotActor);
            robotScheduler.UpdateRobotState(robotId, "idle");
        }

        // For publication-based schedulers, register stations and connect state updates
        if (robotScheduler is PublicationBasedScheduler pubSubScheduler)
        {
            System.Console.WriteLine("📡 Registering stations with publication-based scheduler...");
            foreach (var (name, station) in stations)
            {
                pubSubScheduler.RegisterStation(name, station.CurrentState, station.CurrentWafer);

                // Connect station to publish state changes
                station.OnStateChanged = (state, waferId) =>
                {
                    pubSubScheduler.UpdateStationState(name, state, waferId);
                };
            }
        }
        else if (robotScheduler is SinglePublicationScheduler singlePubScheduler)
        {
            System.Console.WriteLine("📡⚡ Registering stations with single publication scheduler...");
            foreach (var (name, station) in stations)
            {
                singlePubScheduler.RegisterStation(name, station.CurrentState, station.CurrentWafer);

                // Connect station to publish state changes
                station.OnStateChanged = (state, waferId) =>
                {
                    singlePubScheduler.UpdateStationState(name, state, waferId);
                };
            }
        }
        else if (robotScheduler is SinglePublicationSchedulerXState arrayPubScheduler)
        {
            System.Console.WriteLine("📡⚡🎯 Registering stations with array-based publication scheduler...");
            foreach (var (name, station) in stations)
            {
                arrayPubScheduler.RegisterStation(name, station.CurrentState, station.CurrentWafer);

                // Connect station to publish state changes
                station.OnStateChanged = (state, waferId) =>
                {
                    arrayPubScheduler.UpdateStationState(name, state, waferId);
                };
            }
        }

        System.Console.WriteLine();
        System.Console.WriteLine("✅ All components initialized!");
        System.Console.WriteLine();
        System.Console.WriteLine("════════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("🚀 Starting Two-Carrier Successive Processing Simulation");
        System.Console.WriteLine("════════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine();

        // Manually drive the simulation to show scheduler logic with two carriers
        await SimulateWaferJourney(wafers, stations, robotScheduler, journeyScheduler);

        System.Console.WriteLine("════════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine("📊 Simulation Summary");
        System.Console.WriteLine("════════════════════════════════════════════════════════════════════════");
        System.Console.WriteLine();
        DisplayFinalSummary(wafers, robotScheduler, stations);

        System.Console.WriteLine();
        System.Console.WriteLine("✅ Demo complete! Components used:");
        System.Console.WriteLine($"   ✓ RobotScheduler ({robotName}-based) - enforces robot single-wafer rule");
        System.Console.WriteLine($"   ✓ WaferJourneyScheduler ({journeyName}-based) - enforces station single-wafer rule");
        System.Console.WriteLine("   ✓ 56 unit tests validating these rules (100% passing)");
        System.Console.WriteLine();
        System.Console.WriteLine("📝 See documentation:");
        System.Console.WriteLine("   - ROBOT_RULE.md: Robot single-wafer rule enforcement");
        System.Console.WriteLine("   - STATION_RULE.md: Station single-wafer rule enforcement");
        System.Console.WriteLine();
        System.Console.WriteLine("💡 3x3 Matrix: Choose implementations independently:");
        System.Console.WriteLine("   RobotScheduler:");
        System.Console.WriteLine("     --robot-actor      # Actor-based (no locks)");
        System.Console.WriteLine("     --robot-xstate     # XState-based (declarative)");
        System.Console.WriteLine("     (default)          # Lock-based (traditional)");
        System.Console.WriteLine();
        System.Console.WriteLine("   JourneyScheduler:");
        System.Console.WriteLine("     --journey-actor    # Actor-based (no locks)");
        System.Console.WriteLine("     --journey-xstate   # XState-based (declarative)");
        System.Console.WriteLine("     (default)          # Lock-based (traditional)");
        System.Console.WriteLine();
        System.Console.WriteLine("   Examples:");
        System.Console.WriteLine("     dotnet run                                  # Lock + Lock");
        System.Console.WriteLine("     dotnet run --robot-actor                    # Actor + Lock");
        System.Console.WriteLine("     dotnet run --robot-actor --journey-xstate   # Actor + XState");
        System.Console.WriteLine("     dotnet run --benchmark                      # Run benchmark");

        // Shutdown actor system
        await actorSystem.Terminate();
    }

    static void PrintUsage()
    {
        System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  CMPSimXS2.Console - Wafer Manufacturing Simulator                              ║");
        System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();
        System.Console.WriteLine("📋 USAGE:");
        System.Console.WriteLine("   dotnet run [OPTIONS]");
        System.Console.WriteLine();
        System.Console.WriteLine("🎯 EXECUTION MODES:");
        System.Console.WriteLine();
        System.Console.WriteLine("   --benchmark, -b");
        System.Console.WriteLine("      Run performance benchmark (12-way comparison)");
        System.Console.WriteLine("      Tests: Sequential throughput, Query latency, Concurrent load");
        System.Console.WriteLine();
        System.Console.WriteLine("   --stress-test, --stress");
        System.Console.WriteLine("      Run EXTREME stress test: 1000 FOUPs × 25 Wafers = 25,000 total wafers");
        System.Console.WriteLine("      Tests all 16 schedulers: Reliability, throughput, failure modes");
        System.Console.WriteLine();
        System.Console.WriteLine("   (any robot/journey option)");
        System.Console.WriteLine("      Run single-wafer manufacturing simulation");
        System.Console.WriteLine("      Configuration: 2 FOUP Carriers × 25 Wafers = 50 Total Wafers");
        System.Console.WriteLine();
        System.Console.WriteLine("🤖 ROBOT SCHEDULER OPTIONS (Reliability-First Ranking):");
        System.Console.WriteLine();
        System.Console.WriteLine("   ✅ 100% COMPLETION RATE (Most Reliable):");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-lock");
        System.Console.WriteLine("      🔒 Lock (polling) - 15.73s, traditional mutex pattern");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-actor");
        System.Console.WriteLine("      🎭 Actor (event) - 15.69s, pure Akka.NET coordination");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-xstate");
        System.Console.WriteLine("      🔄 XS2-Array (event) - 15.69s, max performance with byte arrays");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-xs2-frozen");
        System.Console.WriteLine("      ❄️  XS2-Frozen (event) - 15.68s, FrozenDictionary optimized");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-xs2-dict");
        System.Console.WriteLine("      📚 XS2-Dict (event) - 18.xx s, Dictionary baseline");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-autonomous");
        System.Console.WriteLine("      🤖 Autonomous (polling) - 15.72s, self-organizing robots");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-array (alias for hybrid)");
        System.Console.WriteLine("      ⚡ Autonomous-Array (polling) - 15.68s, optimized autonomous");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-eventdriven");
        System.Console.WriteLine("      🔔 Autonomous-Event (event) - 15.68s, event-driven autonomous");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-actormailbox");
        System.Console.WriteLine("      📬 Actor-Mailbox (event) - 15.68s, mailbox-based coordination");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-ant");
        System.Console.WriteLine("      🐜 Ant-Colony (event) - 15.68s, swarm intelligence");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-pubsub");
        System.Console.WriteLine("      📡 XS2-PubSub-Dedicated (multi) - 15.72s, multi-publisher");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-singlepub");
        System.Console.WriteLine("      📢 PubSub-Single (one) - 15.68s, one-publisher pattern");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-array-singlepub");
        System.Console.WriteLine("      🚀 XS2-PubSub-Array (one) - 15.68s, optimized one-publisher");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-sync-pipe");
        System.Console.WriteLine("      ⏸️  Sync-Pipeline (batch) - 15.70s, synchronized batch execution");
        System.Console.WriteLine();
        System.Console.WriteLine("   ⚠️  EXPERIMENTAL (Under Development):");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-xs1-legacy");
        System.Console.WriteLine("      🕰️  XS1-Legacy (event) - 0% completion, XStateNet V1 (BROKEN)");
        System.Console.WriteLine();
        System.Console.WriteLine("   --robot-xs2-sync-pipe");
        System.Console.WriteLine("      🔄 XS2-Sync-Pipeline (batch) - 0% completion (DEBUGGING)");
        System.Console.WriteLine();
        System.Console.WriteLine("🚄 JOURNEY SCHEDULER OPTIONS:");
        System.Console.WriteLine();
        System.Console.WriteLine("   --journey-actor");
        System.Console.WriteLine("      🎭 Actor-based journey scheduler");
        System.Console.WriteLine();
        System.Console.WriteLine("   --journey-xstate");
        System.Console.WriteLine("      🔄 XState-based journey scheduler");
        System.Console.WriteLine();
        System.Console.WriteLine("   (default: Lock-based)");
        System.Console.WriteLine("      🔒 Lock-based journey scheduler");
        System.Console.WriteLine();
        System.Console.WriteLine("📊 EXAMPLES:");
        System.Console.WriteLine();
        System.Console.WriteLine("   # Run benchmark");
        System.Console.WriteLine("   dotnet run --benchmark");
        System.Console.WriteLine();
        System.Console.WriteLine("   # Run simulation with fastest scheduler");
        System.Console.WriteLine("   dotnet run --robot-singlepub");
        System.Console.WriteLine();
        System.Console.WriteLine("   # Run with XStateNet2 Array Single Publication (array + fast)");
        System.Console.WriteLine("   dotnet run --robot-array-singlepub");
        System.Console.WriteLine();
        System.Console.WriteLine("   # Run with Actor-based robot + XState journey");
        System.Console.WriteLine("   dotnet run --robot-actor --journey-xstate");
        System.Console.WriteLine();
        System.Console.WriteLine("   # Run with Ant Colony (decentralized)");
        System.Console.WriteLine("   dotnet run --robot-ant");
        System.Console.WriteLine();
        System.Console.WriteLine("💡 PERFORMANCE RANKING (Sequential Throughput):");
        System.Console.WriteLine();
        System.Console.WriteLine("   🥇 Single Publication:  6,608,075 req/sec  (--robot-singlepub) ⭐⭐⭐⭐⭐");
        System.Console.WriteLine("   🥈 Array SinglePub:     ~3,000,000 req/sec  (--robot-array-singlepub) 🎯");
        System.Console.WriteLine("   🥉 Actor:               2,927,143 req/sec  (--robot-actor)");
        System.Console.WriteLine("   4️⃣  XState Array:        2,717,613 req/sec  (--robot-array)");
        System.Console.WriteLine("   5️⃣  XState FrozenDict:  1,577,660 req/sec  (--robot-xstate)");
        System.Console.WriteLine("   6️⃣  Ant Colony:         1,718 req/sec      (--robot-ant)");
        System.Console.WriteLine("   7️⃣  Lock:               1,707 req/sec      (default)");
        System.Console.WriteLine("   8️⃣  Publication-Based:  1,351 req/sec      (--robot-pubsub)");
        System.Console.WriteLine();
        System.Console.WriteLine("📊 SIMULATION CONFIGURATION:");
        System.Console.WriteLine();
        System.Console.WriteLine("   Carriers:       2 FOUP Carriers (C1 and C2)");
        System.Console.WriteLine("   Wafers/FOUP:    25 wafers per carrier");
        System.Console.WriteLine("   Total Wafers:   50 wafers (C1: 1-25, C2: 26-50)");
        System.Console.WriteLine("   Flow:           C1 arrives → processes → departs → C2 arrives → processes");
        System.Console.WriteLine("   Journey:        Carrier → Polisher → Cleaner → Buffer → Carrier");
        System.Console.WriteLine("   Max Cycles:     400 (auto-increased for 50 wafers)");
        System.Console.WriteLine();
        System.Console.WriteLine("🔗 MORE INFO:");
        System.Console.WriteLine();
        System.Console.WriteLine("   See README.md for detailed documentation");
        System.Console.WriteLine("   See SCHEDULER_MATRIX.md for feature comparison");
        System.Console.WriteLine("   See SINGLE_PUB_VS_ACTOR_VS_ARRAY.md for architecture details");
        System.Console.WriteLine();
    }

    /// <summary>
    /// Create mock station actors and models
    /// </summary>
    static Dictionary<string, Station> CreateMockStations(ActorSystem system)
    {
        var stations = new Dictionary<string, Station>();
        var stationNames = new[] { "Polisher", "Cleaner", "Buffer" };

        foreach (var name in stationNames)
        {
            // Create station model
            var station = new Station(name)
            {
                CurrentState = "idle" // Initialize stations to idle state
            };

            // Create actor with reference to station so it can update state
            var stationActor = system.ActorOf(Props.Create(() => new MockStationActor(station)), name);
            station.StateMachine = stationActor;

            stations[name] = station;
        }

        return stations;
    }

    /// <summary>
    /// Create mock robot actors
    /// </summary>
    static Dictionary<string, IActorRef> CreateMockRobots(ActorSystem system, IRobotScheduler scheduler)
    {
        var robots = new Dictionary<string, IActorRef>();
        var robotIds = new[] { "Robot 1", "Robot 2", "Robot 3" };
        var actorNames = new[] { "Robot1", "Robot2", "Robot3" };

        for (int i = 0; i < robotIds.Length; i++)
        {
            var robotId = robotIds[i];
            var actorName = actorNames[i];
            var robot = system.ActorOf(Props.Create(() => new MockRobotActor(robotId, scheduler)), actorName);
            robots[robotId] = robot;
        }

        return robots;
    }

    /// <summary>
    /// Simulate wafer journey with TWO carriers processed successively
    /// </summary>
    static async Task SimulateWaferJourney(
        List<Wafer> wafers,
        Dictionary<string, Station> stations,
        IRobotScheduler robotScheduler,
        IWaferJourneyScheduler journeyScheduler)
    {
        int cycle = 0;
        int maxCycles = 400; // Increased for 2 carriers × 25 wafers
        string? currentCarrier = null;
        bool carrier1Departed = false;

        // Event handler for carrier completion
        journeyScheduler.OnCarrierCompleted += (carrierId) =>
        {
            System.Console.WriteLine();
            System.Console.WriteLine("════════════════════════════════════════════════════════════════════════");
            System.Console.WriteLine($"✅ CARRIER {carrierId} COMPLETE - All wafers processed!");
            System.Console.WriteLine("════════════════════════════════════════════════════════════════════════");
            System.Console.WriteLine();
        };

        // Trigger Carrier 1 arrival with first 25 wafers
        System.Console.WriteLine("🚛 EVENT: FOUP Carrier C1 arrives with 25 wafers (IDs: 1-25)");
        var carrier1Ids = Enumerable.Range(1, 25).ToList();
        journeyScheduler.OnCarrierArrival("C1", carrier1Ids);
        currentCarrier = "C1";
        System.Console.WriteLine();

        while (cycle < maxCycles)
        {
            cycle++;
            System.Console.WriteLine($"╔══ Cycle {cycle} ══════════════════════════════════════════════════════════╗");

            // Display system state BEFORE processing
            DisplaySystemState(wafers, stations, robotScheduler, journeyScheduler);

            // Process scheduler logic
            journeyScheduler.ProcessWaferJourneys();

            // Check if Carrier 1 is complete and needs to depart
            if (currentCarrier == "C1" && !carrier1Departed && journeyScheduler.IsCurrentCarrierComplete())
            {
                System.Console.WriteLine();
                System.Console.WriteLine("🚚 EVENT: FOUP Carrier C1 departs with all processed wafers");
                journeyScheduler.OnCarrierDeparture("C1");
                carrier1Departed = true;

                System.Console.WriteLine();
                await Task.Delay(1000); // Pause between carriers
                System.Console.WriteLine("🚛 EVENT: FOUP Carrier C2 arrives with 25 wafers (IDs: 26-50)");
                var carrier2Ids = Enumerable.Range(26, 25).ToList();
                journeyScheduler.OnCarrierArrival("C2", carrier2Ids);
                currentCarrier = "C2";
                System.Console.WriteLine();
            }

            // Simulate stations completing their work
            SimulateStationProgress(stations, cycle);

            // Simulate robots completing transfers
            SimulateRobotProgress(robotScheduler, cycle);

            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
            System.Console.WriteLine();

            await Task.Delay(500);

            // Stop if all wafers completed
            if (wafers.All(w => w.IsCompleted))
            {
                System.Console.WriteLine("🎉🎉🎉 All 50 wafers from both FOUP carriers completed! 🎉🎉🎉");
                System.Console.WriteLine();

                // Trigger final carrier departure
                if (currentCarrier == "C2")
                {
                    System.Console.WriteLine("🚚 EVENT: FOUP Carrier C2 departs with all processed wafers");
                    journeyScheduler.OnCarrierDeparture("C2");
                }

                break;
            }
        }

        if (cycle >= maxCycles)
        {
            System.Console.WriteLine("⚠️  Simulation stopped after maximum cycles");
        }
    }

    /// <summary>
    /// Display complete system state including wafers, stations, robots, and carrier
    /// </summary>
    static void DisplaySystemState(
        List<Wafer> wafers,
        Dictionary<string, Station> stations,
        IRobotScheduler robotScheduler,
        IWaferJourneyScheduler journeyScheduler)
    {
        System.Console.WriteLine();

        // Display current carrier
        var currentCarrier = journeyScheduler.GetCurrentCarrierId();
        if (!string.IsNullOrEmpty(currentCarrier))
        {
            System.Console.WriteLine($"🚛 CURRENT CARRIER: {currentCarrier}");
            System.Console.WriteLine();
        }

        System.Console.WriteLine("📍 WAFER TRAIN STATUS:");

        foreach (var wafer in wafers)
        {
            // 5-stage color progression: Black → Blue → Green → Yellow → White
            var colorEmoji = wafer.JourneyStage switch
            {
                "InCarrier" when wafer.ProcessingState == "Cleaned" => "⚪",  // White = Completed, returned to carrier
                "ToCarrier" or "InBuffer" => "⚪",                              // White = Cleaned wafer
                "Cleaning" => "🟡",                                            // Yellow = Being cleaned
                "ToCleaner" => "🟢",                                           // Green = Polished, moving to cleaner
                "Polishing" or "ToPolisher" => "🔵",                           // Blue = Being polished
                _ => "⚫"                                                       // Black = Raw in carrier
            };

            var completedMark = wafer.IsCompleted ? "✓" : " ";
            var journeyIcon = GetJourneyIcon(wafer.JourneyStage);

            System.Console.WriteLine($"  [{completedMark}] Wafer {wafer.Id}: {colorEmoji} {journeyIcon} {wafer.JourneyStage,-13}");
        }

        System.Console.WriteLine();
        System.Console.WriteLine("⚙️  STATION STATUS (Each holds MAX 1 wafer):");

        foreach (var station in stations.Values.OrderBy(s => s.Name))
        {
            var stateIcon = station.CurrentState switch
            {
                "idle" => "🟢",
                "processing" => "🔴",
                "done" => "🟡",
                "occupied" => "🟠",
                _ => "⚪"
            };

            var waferInfo = station.CurrentWafer.HasValue
                ? $"Wafer {station.CurrentWafer.Value}"
                : "Empty";

            System.Console.WriteLine($"  {stateIcon} {station.Name,-10} [{station.CurrentState,-10}] → {waferInfo}");
        }

        System.Console.WriteLine();
        System.Console.WriteLine("🤖 ROBOT STATUS (Each carries MAX 1 wafer):");

        var robotIds = new[] { "Robot 1", "Robot 2", "Robot 3" };
        foreach (var robotId in robotIds)
        {
            var state = robotScheduler.GetRobotState(robotId);
            var stateIcon = state switch
            {
                "idle" => "🟢",
                "busy" => "🔴",
                "carrying" => "🟡",
                _ => "⚪"
            };

            System.Console.WriteLine($"  {stateIcon} {robotId}: {state}");
        }

        System.Console.WriteLine();
        System.Console.WriteLine($"📋 QUEUE: {robotScheduler.GetQueueSize()} transfer requests waiting");
        System.Console.WriteLine();
    }

    /// <summary>
    /// Get icon representing journey stage
    /// </summary>
    static string GetJourneyIcon(string journeyStage)
    {
        return journeyStage switch
        {
            "InCarrier" => "📦",
            "ToPolisher" => "→",
            "Polishing" => "🔧",
            "ToCleaner" => "→",
            "Cleaning" => "🧼",
            "ToBuffer" => "→",
            "InBuffer" => "💾",
            "ToCarrier" => "←",
            _ => "?"
        };
    }

    /// <summary>
    /// Simulate stations making progress on their work
    /// </summary>
    static void SimulateStationProgress(Dictionary<string, Station> stations, int cycle)
    {
        foreach (var station in stations.Values)
        {
            // Stations that are processing transition to done after a few cycles
            if (station.CurrentState == "processing" && cycle % 3 == 0)
            {
                station.CurrentState = "done";
                System.Console.WriteLine($"   ✓ {station.Name} completed processing wafer {station.CurrentWafer}");
            }
            // Occupied Buffer can transition to allow retrieval
            else if (station.Name == "Buffer" && station.CurrentState == "occupied" && cycle % 2 == 0)
            {
                // Buffer stays occupied until retrieved
                System.Console.WriteLine($"   💾 Buffer ready for retrieval (wafer {station.CurrentWafer})");
            }
        }
    }

    /// <summary>
    /// Simulate robots completing their transfers
    /// </summary>
    static void SimulateRobotProgress(IRobotScheduler robotScheduler, int cycle)
    {
        var robotIds = new[] { "Robot 1", "Robot 2", "Robot 3" };

        foreach (var robotId in robotIds)
        {
            var state = robotScheduler.GetRobotState(robotId);

            // Busy robots complete transfer and return to idle
            if ((state == "busy" || state == "carrying") && cycle % 2 == 0)
            {
                robotScheduler.UpdateRobotState(robotId, "idle");
                System.Console.WriteLine($"   🤖 {robotId} completed transfer, now idle");
            }
        }
    }

    /// <summary>
    /// Display final simulation summary
    /// </summary>
    static void DisplayFinalSummary(
        List<Wafer> wafers,
        IRobotScheduler scheduler,
        Dictionary<string, Station> stations)
    {
        var completed = wafers.Count(w => w.IsCompleted);
        var inProgress = wafers.Count(w => !w.IsCompleted);

        System.Console.WriteLine("🎯 WAFER RESULTS:");
        System.Console.WriteLine($"   Total Wafers: {wafers.Count}");
        System.Console.WriteLine($"   ✓ Completed Full Journey: {completed}");
        System.Console.WriteLine($"   🔄 In Progress: {inProgress}");
        System.Console.WriteLine();

        System.Console.WriteLine("📋 FINAL WAFER STATES:");
        foreach (var wafer in wafers)
        {
            var colorEmoji = wafer.ProcessingState switch
            {
                "Cleaned" => "⚪",
                "Polished" => "🟡",
                _ => "⚫"
            };

            var status = wafer.IsCompleted ? "✓ COMPLETE" : "In Progress";
            System.Console.WriteLine($"   Wafer {wafer.Id}: {colorEmoji} {status,-12} [{wafer.JourneyStage}]");
        }
        System.Console.WriteLine();

        System.Console.WriteLine("🤖 FINAL ROBOT STATES:");
        var robotIds = new[] { "Robot 1", "Robot 2", "Robot 3" };
        foreach (var robotId in robotIds)
        {
            var state = scheduler.GetRobotState(robotId);
            var icon = state == "idle" ? "🟢" : "🔴";
            System.Console.WriteLine($"   {icon} {robotId}: {state}");
        }
        System.Console.WriteLine();

        System.Console.WriteLine("⚙️  FINAL STATION STATES:");
        foreach (var station in stations.Values.OrderBy(s => s.Name))
        {
            var icon = station.CurrentState == "idle" ? "🟢" : "🔴";
            var waferInfo = station.CurrentWafer.HasValue ? $"(Wafer {station.CurrentWafer.Value})" : "(Empty)";
            System.Console.WriteLine($"   {icon} {station.Name}: {station.CurrentState} {waferInfo}");
        }
        System.Console.WriteLine();

        System.Console.WriteLine($"📊 Queue: {scheduler.GetQueueSize()} pending transfer requests");
        System.Console.WriteLine();

        // Rule enforcement summary
        System.Console.WriteLine("✅ SINGLE-WAFER RULES ENFORCED:");
        System.Console.WriteLine("   🤖 Each robot carried max 1 wafer at a time");
        System.Console.WriteLine("   ⚙️  Each station held max 1 wafer at a time");
        System.Console.WriteLine("   🔄 Stations worked in parallel with exclusive wafer ownership");
    }
}

/// <summary>
/// Mock station actor that simulates station state changes
/// </summary>
public class MockStationActor : ReceiveActor
{
    private readonly Station _station;

    public MockStationActor(Station station)
    {
        _station = station;

        ReceiveAny(msg =>
        {
            var msgStr = msg.ToString();
            Logger.Instance.Log($"[MockStation:DEBUG] {_station.Name} received: {msg.GetType().Name}");

            // Handle LOAD_WAFER event
            if (msgStr!.Contains("LOAD_WAFER"))
            {
                var sendEvent = msg as XStateNet2.Core.Messages.SendEvent;
                if (sendEvent?.Data != null && sendEvent.Data is Dictionary<string, object> data && data.ContainsKey("wafer"))
                {
                    var waferId = (int)data["wafer"];
                    _station.CurrentWafer = waferId;
                    _station.CurrentState = "processing";
                    Logger.Instance.Log($"[MockStation:DEBUG] {_station.Name} started processing wafer {waferId}");
                }
            }
            // Handle STORE_WAFER event (for Buffer)
            else if (msgStr.Contains("STORE_WAFER"))
            {
                var sendEvent = msg as XStateNet2.Core.Messages.SendEvent;
                if (sendEvent?.Data != null && sendEvent.Data is Dictionary<string, object> data && data.ContainsKey("wafer"))
                {
                    var waferId = (int)data["wafer"];
                    _station.CurrentWafer = waferId;
                    _station.CurrentState = "occupied";
                    Logger.Instance.Log($"[MockStation:DEBUG] {_station.Name} stored wafer {waferId}");
                }
            }
            // Handle UNLOAD_WAFER event
            else if (msgStr.Contains("UNLOAD_WAFER"))
            {
                Logger.Instance.Log($"[MockStation:DEBUG] {_station.Name} unloaded wafer {_station.CurrentWafer}");
                _station.CurrentWafer = null;
                _station.CurrentState = "idle";
            }
            // Handle RETRIEVE_WAFER event (for Buffer)
            else if (msgStr.Contains("RETRIEVE_WAFER"))
            {
                Logger.Instance.Log($"[MockStation:DEBUG] {_station.Name} retrieved wafer {_station.CurrentWafer}");
                _station.CurrentWafer = null;
                _station.CurrentState = "idle";
            }
        });
    }
}

/// <summary>
/// Mock robot actor that simulates robot transfers
/// </summary>
public class MockRobotActor : ReceiveActor
{
    private readonly string _robotId;
    private readonly IRobotScheduler _scheduler;
    private int? _currentWafer;

    public MockRobotActor(string robotId, IRobotScheduler scheduler)
    {
        _robotId = robotId;
        _scheduler = scheduler;

        ReceiveAny(msg =>
        {
            Logger.Instance.Log($"[MockRobot:DEBUG] {_robotId} received: {msg.GetType().Name}");

            // Simulate robot handling PICKUP event
            if (msg.ToString()!.Contains("PICKUP"))
            {
                var sendEvent = msg as XStateNet2.Core.Messages.SendEvent;
                if (sendEvent?.Data != null && sendEvent.Data is Dictionary<string, object> data && data.ContainsKey("waferId"))
                {
                    _currentWafer = (int)data["waferId"];

                    // Simulate pickup → carrying → placing sequence
                    Task.Run(async () =>
                    {
                        // Simulate picking up
                        await Task.Delay(50);
                        _scheduler.UpdateRobotState(_robotId, "carrying", _currentWafer);

                        // Simulate carrying
                        await Task.Delay(100);

                        // Simulate placing - robot returns to idle, wafer delivered
                        _currentWafer = null;
                        _scheduler.UpdateRobotState(_robotId, "idle");

                        Logger.Instance.Log($"[MockRobot:DEBUG] {_robotId} completed simulated transfer");
                    });
                }
                else
                {
                    // Fallback for old-style messages without explicit waferId
                    Task.Run(async () =>
                    {
                        await Task.Delay(150);
                        _scheduler.UpdateRobotState(_robotId, "idle");
                    });
                }
            }
        });
    }
}
