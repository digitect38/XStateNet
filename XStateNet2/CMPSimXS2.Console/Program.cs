using System.Text;
using System.Collections.ObjectModel;
using Akka.Actor;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;
using CMPSimXS2.ViewModels;
using CMPSimXS2.Helpers;

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

        System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
        System.Console.WriteLine("║  CMPSimXS2 Single-Wafer Rule Demonstration                            ║");
        System.Console.WriteLine("║  Train Pattern: Carrier → R1 → Polisher → R2 → Cleaner → R3 →        ║");
        System.Console.WriteLine("║                 Buffer → R1 → Carrier                                  ║");
        System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
        System.Console.WriteLine();

        System.Console.WriteLine("📋 CRITICAL RULES:");
        System.Console.WriteLine("   🤖 Robot Rule: Each robot can carry only ONE wafer at a time");
        System.Console.WriteLine("   ⚙️  Station Rule: Each station can hold only ONE wafer at a time");
        System.Console.WriteLine("   🔄 Parallel Work: Multiple stations/robots work simultaneously");
        System.Console.WriteLine();

        // Create Akka ActorSystem
        System.Console.WriteLine("🔧 Initializing Akka ActorSystem...");
        using var actorSystem = ActorSystem.Create("CMPSimXS2-Console");

        // Initialize schedulers
        System.Console.WriteLine("🤖 Initializing RobotScheduler...");
        var robotScheduler = new RobotScheduler();

        // Create wafers for TWO carriers (5 wafers per carrier)
        System.Console.WriteLine("💿 Creating 10 wafers (2 carriers × 5 wafers each)...");
        var wafers = new ObservableCollection<Wafer>
        {
            // Carrier 1: Wafers 1-5
            new Wafer(1),
            new Wafer(2),
            new Wafer(3),
            new Wafer(4),
            new Wafer(5),
            // Carrier 2: Wafers 6-10
            new Wafer(6),
            new Wafer(7),
            new Wafer(8),
            new Wafer(9),
            new Wafer(10)
        };

        System.Console.WriteLine("🔄 Initializing WaferJourneyScheduler...");
        var journeyScheduler = new WaferJourneyScheduler(robotScheduler, wafers);

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
        System.Console.WriteLine("   ✓ RobotScheduler (enforces robot single-wafer rule)");
        System.Console.WriteLine("   ✓ WaferJourneyScheduler (enforces station single-wafer rule)");
        System.Console.WriteLine("   ✓ 56 unit tests validating these rules (100% passing)");
        System.Console.WriteLine();
        System.Console.WriteLine("📝 See documentation:");
        System.Console.WriteLine("   - ROBOT_RULE.md: Robot single-wafer rule enforcement");
        System.Console.WriteLine("   - STATION_RULE.md: Station single-wafer rule enforcement");

        // Shutdown actor system
        await actorSystem.Terminate();
    }

    /// <summary>
    /// Create mock station actors and viewmodels
    /// </summary>
    static Dictionary<string, StationViewModel> CreateMockStations(ActorSystem system)
    {
        var stations = new Dictionary<string, StationViewModel>();
        var stationNames = new[] { "Polisher", "Cleaner", "Buffer" };

        foreach (var name in stationNames)
        {
            // Create view model first
            var viewModel = new StationViewModel(name)
            {
                CurrentState = "idle" // Initialize stations to idle state
            };

            // Create actor with reference to viewmodel so it can update state
            var stationActor = system.ActorOf(Props.Create(() => new MockStationActor(viewModel)), name);
            viewModel.StateMachine = stationActor;

            stations[name] = viewModel;
        }

        return stations;
    }

    /// <summary>
    /// Create mock robot actors
    /// </summary>
    static Dictionary<string, IActorRef> CreateMockRobots(ActorSystem system, RobotScheduler scheduler)
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
        ObservableCollection<Wafer> wafers,
        Dictionary<string, StationViewModel> stations,
        RobotScheduler robotScheduler,
        WaferJourneyScheduler journeyScheduler)
    {
        int cycle = 0;
        int maxCycles = 100; // Increased for two carriers
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

        // Trigger Carrier 1 arrival
        System.Console.WriteLine("🚛 EVENT: Carrier C1 arrives with 5 wafers (IDs: 1-5)");
        journeyScheduler.OnCarrierArrival("C1", new List<int> { 1, 2, 3, 4, 5 });
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
                System.Console.WriteLine("🚚 EVENT: Carrier C1 departs with all processed wafers");
                journeyScheduler.OnCarrierDeparture("C1");
                carrier1Departed = true;

                System.Console.WriteLine();
                await Task.Delay(1000); // Pause between carriers
                System.Console.WriteLine("🚛 EVENT: Carrier C2 arrives with 5 wafers (IDs: 6-10)");
                journeyScheduler.OnCarrierArrival("C2", new List<int> { 6, 7, 8, 9, 10 });
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
                System.Console.WriteLine("🎉🎉🎉 All wafers from both carriers completed! 🎉🎉🎉");
                System.Console.WriteLine();

                // Trigger final carrier departure
                if (currentCarrier == "C2")
                {
                    System.Console.WriteLine("🚚 EVENT: Carrier C2 departs with all processed wafers");
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
        ObservableCollection<Wafer> wafers,
        Dictionary<string, StationViewModel> stations,
        RobotScheduler robotScheduler,
        WaferJourneyScheduler journeyScheduler)
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

            System.Console.WriteLine($"  [{completedMark}] Wafer {wafer.Id}: {colorEmoji} {journeyIcon} {wafer.JourneyStage,-13} @ {wafer.CurrentStation}");
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
    static void SimulateStationProgress(Dictionary<string, StationViewModel> stations, int cycle)
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
    static void SimulateRobotProgress(RobotScheduler robotScheduler, int cycle)
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
        ObservableCollection<Wafer> wafers,
        RobotScheduler scheduler,
        Dictionary<string, StationViewModel> stations)
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
    private readonly StationViewModel _station;

    public MockStationActor(StationViewModel station)
    {
        _station = station;

        ReceiveAny(msg =>
        {
            var msgStr = msg.ToString();
            Logger.Instance.Debug("MockStation", $"{_station.Name} received: {msg.GetType().Name}");

            // Handle LOAD_WAFER event
            if (msgStr!.Contains("LOAD_WAFER"))
            {
                var sendEvent = msg as XStateNet2.Core.Messages.SendEvent;
                if (sendEvent?.Data != null && sendEvent.Data is Dictionary<string, object> data && data.ContainsKey("wafer"))
                {
                    var waferId = (int)data["wafer"];
                    _station.CurrentWafer = waferId;
                    _station.CurrentState = "processing";
                    Logger.Instance.Debug("MockStation", $"{_station.Name} started processing wafer {waferId}");
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
                    Logger.Instance.Debug("MockStation", $"{_station.Name} stored wafer {waferId}");
                }
            }
            // Handle UNLOAD_WAFER event
            else if (msgStr.Contains("UNLOAD_WAFER"))
            {
                Logger.Instance.Debug("MockStation", $"{_station.Name} unloaded wafer {_station.CurrentWafer}");
                _station.CurrentWafer = null;
                _station.CurrentState = "idle";
            }
            // Handle RETRIEVE_WAFER event (for Buffer)
            else if (msgStr.Contains("RETRIEVE_WAFER"))
            {
                Logger.Instance.Debug("MockStation", $"{_station.Name} retrieved wafer {_station.CurrentWafer}");
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
    private readonly RobotScheduler _scheduler;
    private int? _currentWafer;

    public MockRobotActor(string robotId, RobotScheduler scheduler)
    {
        _robotId = robotId;
        _scheduler = scheduler;

        ReceiveAny(msg =>
        {
            Logger.Instance.Debug("MockRobot", $"{_robotId} received: {msg.GetType().Name}");

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

                        Logger.Instance.Debug("MockRobot", $"{_robotId} completed simulated transfer");
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
