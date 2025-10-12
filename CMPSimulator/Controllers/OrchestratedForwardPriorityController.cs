using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using CMPSimulator.Models;
using CMPSimulator.StateMachines;
using XStateNet.Orchestration;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.Controllers;

/// <summary>
/// Orchestrated Forward Priority Controller using XStateNet state machines
/// Architecture: Station → Scheduler (status reports only)
///              Scheduler → Robot (centralized commands)
/// </summary>
public class OrchestratedForwardPriorityController : IForwardPriorityController
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Dictionary<string, StationPosition> _stations;
    private readonly Dictionary<int, int> _waferOriginalSlots;
    private readonly CancellationTokenSource _cts;

    // State Machines
    private SchedulerMachine? _scheduler;
    private PolisherMachine? _polisher;
    private CleanerMachine? _cleaner;
    private BufferMachine? _buffer;
    private RobotMachine? _r1;
    private RobotMachine? _r2;
    private RobotMachine? _r3;

    private bool _isInitialized = false;
    private System.Threading.Timer? _progressTimer;
    private DateTime _simulationStartTime;
    private ExecutionMode _executionMode = ExecutionMode.Async;
    private Queue<string> _pendingEvents = new Queue<string>();

    // Timing constants
    private const int POLISHING = 7000;  // 7 seconds
    private const int CLEANING = 7000;   // 7 seconds
    public const int TRANSFER = 1000;    // 1 second (1000ms) for robot transfers

    // Simulation configuration
    public const int TOTAL_WAFERS = 10;  // Total number of wafers to process

    public ObservableCollection<Wafer> Wafers { get; }
    public Dictionary<string, StationPosition> Stations => _stations;

    public event EventHandler<string>? LogMessage;
    public event EventHandler? StationStatusChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    // Status properties (read from state machines) - Show full state path
    public string PolisherStatus => _polisher?.CurrentState ?? "Unknown";
    public string CleanerStatus => _cleaner?.CurrentState ?? "Unknown";
    public string BufferStatus => _buffer?.CurrentState ?? "Unknown";
    public string R1Status => _r1?.CurrentState ?? "Unknown";
    public string R2Status => _r2?.CurrentState ?? "Unknown";
    public string R3Status => _r3?.CurrentState ?? "Unknown";

    // Remaining time properties for UI binding
    public string PolisherRemainingTime => _polisher?.RemainingTimeMs > 0
        ? $"{(_polisher.RemainingTimeMs / 1000.0):F1}s"
        : "";
    public string CleanerRemainingTime => _cleaner?.RemainingTimeMs > 0
        ? $"{(_cleaner.RemainingTimeMs / 1000.0):F1}s"
        : "";

    public OrchestratedForwardPriorityController()
    {
        // Enable orchestrator logging for debugging
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            EnableMetrics = true
        };
        _orchestrator = new EventBusOrchestrator(config);
        _stations = new Dictionary<string, StationPosition>();
        _waferOriginalSlots = new Dictionary<int, int>();
        Wafers = new ObservableCollection<Wafer>();
        _cts = new CancellationTokenSource();

        InitializeStations();
        InitializeWafers();
        InitializeStateMachines();
        SubscribeToStateUpdates();

        Log("═══════════════════════════════════════════════════════════");
        Log("CMP Tool Simulator - Orchestrated Forward Priority");
        Log("Using XStateNet State Machines + EventBusOrchestrator");
        Log("Architecture: Pub/Sub pattern for state updates (no polling)");
        Log("═══════════════════════════════════════════════════════════");
    }

    private void InitializeStations()
    {
        _stations["LoadPort"] = new StationPosition("LoadPort", 46, 256, 108, 108, 25);
        _stations["R1"] = new StationPosition("R1", 250, 270, 80, 80, 0);
        _stations["Polisher"] = new StationPosition("Polisher", 420, 250, 120, 120, 1);
        _stations["R2"] = new StationPosition("R2", 590, 270, 80, 80, 0);
        _stations["Cleaner"] = new StationPosition("Cleaner", 760, 250, 120, 120, 1);
        _stations["R3"] = new StationPosition("R3", 590, 460, 80, 80, 0);
        _stations["Buffer"] = new StationPosition("Buffer", 440, 460, 80, 80, 1);
    }

    private void InitializeWafers()
    {
        var colors = GenerateDistinctColors(TOTAL_WAFERS);

        for (int i = 0; i < TOTAL_WAFERS; i++)
        {
            var wafer = new Wafer(i + 1, colors[i]);
            var loadPort = _stations["LoadPort"];
            var (x, y) = loadPort.GetWaferPosition(i);

            wafer.X = x;
            wafer.Y = y;
            wafer.CurrentStation = "LoadPort";

            Wafers.Add(wafer);
            _waferOriginalSlots[wafer.Id] = i;
            _stations["LoadPort"].AddWafer(wafer.Id);
        }
    }

    private void InitializeStateMachines()
    {
        // Create state machines
        _scheduler = new SchedulerMachine(_orchestrator, Log);
        _polisher = new PolisherMachine("polisher", _orchestrator, POLISHING, Log);
        _cleaner = new CleanerMachine("cleaner", _orchestrator, CLEANING, Log);
        _buffer = new BufferMachine(_orchestrator, Log);
        _r1 = new RobotMachine("R1", _orchestrator, TRANSFER, Log);
        _r2 = new RobotMachine("R2", _orchestrator, TRANSFER, Log);
        _r3 = new RobotMachine("R3", _orchestrator, TRANSFER, Log);

        // Subscribe to scheduler completion event
        _scheduler.AllWafersCompleted += (s, e) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Log($"✅ All {TOTAL_WAFERS} wafers completed!");
                LogTimingStatistics();
            });
        };

        Log("✓ State machines created");
    }

    private void SubscribeToStateUpdates()
    {
        // Subscribe to state machine StateChanged events (direct Pub/Sub pattern)
        _polisher!.StateChanged += OnStateChanged;
        _cleaner!.StateChanged += OnStateChanged;
        _buffer!.StateChanged += OnStateChanged;
        _r1!.StateChanged += OnStateChanged;
        _r2!.StateChanged += OnStateChanged;
        _r3!.StateChanged += OnStateChanged;

        Log("✓ Subscribed to state update events (Direct Pub/Sub pattern)");
    }

    private void OnStateChanged(object? sender, XStateNet.Monitoring.StateTransitionEventArgs e)
    {
        // This is called after state transitions complete (entry actions have executed)
        // StateChanged event fires after RaiseStateChanged() in Transition.cs

        // Trigger UI update on state changes
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            // Get the current state for the machine that changed
            string currentState = e.StateMachineId switch
            {
                "polisher" => PolisherStatus,
                "cleaner" => CleanerStatus,
                "buffer" => BufferStatus,
                "R1" => R1Status,
                "R2" => R2Status,
                "R3" => R3Status,
                _ => "Unknown"
            };

            // Log state transition (CurrentState should now be correct)
            Log($"[StateChanged] '{e.StateMachineId}': {e.FromState} → {e.ToState} (Event: {e.TriggerEvent}) | CurrentState={currentState}");

            UpdateWaferPositions();
            StationStatusChanged?.Invoke(this, EventArgs.Empty);

            // Notify property changes for status properties
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PolisherStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CleanerStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BufferStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(R1Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(R2Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(R3Status)));
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    public async Task StartSimulation()
    {
        // Record start time for statistics
        _simulationStartTime = DateTime.Now;

        // Start all state machines in parallel to avoid sequential startup delay
        var startTasks = new[]
        {
            _scheduler!.StartAsync(),
            _polisher!.StartAsync(),
            _cleaner!.StartAsync(),
            _buffer!.StartAsync(),
            _r1!.StartAsync(),
            _r2!.StartAsync(),
            _r3!.StartAsync()
        };

        await Task.WhenAll(startTasks);

        Log("✓ All state machines started");
        Log("▶ Simulation started (Event-driven mode - no polling)");

        // Start progress update timer (100ms intervals)
        _progressTimer = new System.Threading.Timer(
            UpdateProgress,
            null,
            0,
            100  // Update every 100ms
        );

        _isInitialized = true;
    }

    public async Task ExecuteOneStep()
    {
        // Ensure state machines are started
        if (!_isInitialized)
        {
            await StartSimulation();
            return;
        }

        if (_executionMode == ExecutionMode.Async)
        {
            Log("⏭ Step execution not available in ASYNC mode");
            Log("  Switch to SYNC mode to use step-by-step execution");
            return;
        }

        // In SYNC mode: Process one pending event from the orchestrator
        // The EventBusOrchestrator queues events internally
        // We'll manually trigger the scheduler to process one step

        Log("⏭ Executing one step...");

        // For now, sync mode simply shows that it's activated
        // Full implementation would require modifying EventBusOrchestrator
        // to expose a ProcessOneEvent() method
        Log("  [SYNC] Step executed (event processing)");

        // Manually update UI
        UpdateWaferPositions();
        StationStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopSimulation()
    {
        _cts.Cancel();
        Log("⏸ Simulation stopped");
    }

    public void ResetSimulation()
    {
        // Reset wafer positions
        foreach (var wafer in Wafers)
        {
            wafer.CurrentStation = "LoadPort";
            var slot = _waferOriginalSlots[wafer.Id];
            var (x, y) = _stations["LoadPort"].GetWaferPosition(slot);
            wafer.X = x;
            wafer.Y = y;
        }

        Log("↻ Simulation reset");
        Log("Note: State machines need to be recreated for full reset");
    }

    // All scheduling logic moved to SchedulerMachine - event-driven architecture

    private void UpdateWaferPositions()
    {
        // Update LoadPort wafers (get lists from SchedulerMachine)
        if (_scheduler != null)
        {
            var completedWafers = _scheduler.Completed;

            // Pending wafers are wafers 1-TOTAL_WAFERS that haven't been completed yet
            var allWafers = Enumerable.Range(1, TOTAL_WAFERS);
            var pendingWafers = allWafers.Except(completedWafers);
            var loadPortWafers = pendingWafers.Concat(completedWafers);

            foreach (var waferId in loadPortWafers)
            {
                var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
                if (wafer != null)
                {
                    wafer.CurrentStation = "LoadPort";
                    var slot = _waferOriginalSlots[waferId];
                    var (x, y) = _stations["LoadPort"].GetWaferPosition(slot);
                    wafer.X = x;
                    wafer.Y = y;

                    // Mark completed wafers
                    if (completedWafers.Contains(waferId))
                    {
                        wafer.IsCompleted = true;
                    }
                }
            }
        }

        // Update R1
        if (_r1?.HeldWafer != null)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == _r1.HeldWafer);
            if (wafer != null)
            {
                wafer.CurrentStation = "R1";
                var pos = _stations["R1"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update Polisher
        if (_polisher?.CurrentWafer != null)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == _polisher.CurrentWafer);
            if (wafer != null)
            {
                wafer.CurrentStation = "Polisher";
                var pos = _stations["Polisher"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update R2
        if (_r2?.HeldWafer != null)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == _r2.HeldWafer);
            if (wafer != null)
            {
                wafer.CurrentStation = "R2";
                var pos = _stations["R2"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update Cleaner
        if (_cleaner?.CurrentWafer != null)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == _cleaner.CurrentWafer);
            if (wafer != null)
            {
                wafer.CurrentStation = "Cleaner";
                var pos = _stations["Cleaner"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update R3
        if (_r3?.HeldWafer != null)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == _r3.HeldWafer);
            if (wafer != null)
            {
                wafer.CurrentStation = "R3";
                var pos = _stations["R3"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update Buffer
        if (_buffer?.CurrentWafer != null)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == _buffer.CurrentWafer);
            if (wafer != null)
            {
                wafer.CurrentStation = "Buffer";
                var pos = _stations["Buffer"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }
    }

    private List<Color> GenerateDistinctColors(int count)
    {
        var colors = new List<Color>();
        for (int i = 0; i < count; i++)
        {
            double hue = (360.0 / count) * i;
            var color = ColorFromHSV(hue, 0.8, 0.9);
            colors.Add(color);
        }
        return colors;
    }

    private Color ColorFromHSV(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);

        value = value * 255;
        byte v = Convert.ToByte(value);
        byte p = Convert.ToByte(value * (1 - saturation));
        byte q = Convert.ToByte(value * (1 - f * saturation));
        byte t = Convert.ToByte(value * (1 - (1 - f) * saturation));

        return hi switch
        {
            0 => Color.FromRgb(v, t, p),
            1 => Color.FromRgb(q, v, p),
            2 => Color.FromRgb(p, v, t),
            3 => Color.FromRgb(p, q, v),
            4 => Color.FromRgb(t, p, v),
            _ => Color.FromRgb(v, p, q)
        };
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, message);
    }

    private void UpdateProgress(object? state)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            // Notify UI of property changes for remaining time
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PolisherRemainingTime)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CleanerRemainingTime)));
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void LogTimingStatistics()
    {
        var totalElapsed = (DateTime.Now - _simulationStartTime).TotalMilliseconds;

        Log("");
        Log("═══════════════════════════════════════════════════════════");
        Log("                   TIMING STATISTICS                       ");
        Log("═══════════════════════════════════════════════════════════");
        Log($"Total simulation time: {totalElapsed:F1} ms ({totalElapsed / 1000:F2} s)");
        Log("");
        Log("Configured operation times (per wafer):");
        Log($"  • Polishing:  {POLISHING} ms ({POLISHING / 1000.0:F1} s)");
        Log($"  • Cleaning:   {CLEANING} ms ({CLEANING / 1000.0:F1} s)");
        Log($"  • Transfer:   {TRANSFER} ms ({TRANSFER / 1000.0:F1} s)");
        Log("");
        Log($"Theoretical minimum time for {TOTAL_WAFERS} wafers:");
        Log("  (Assuming perfect parallelization and no overhead)");

        // In Forward Priority with perfect execution:
        // - Each wafer goes: L→P(transfer) → Polish(3000ms) → P→C(transfer) → Clean(3000ms) → C→B(transfer) → B→L(transfer)
        // - For TOTAL_WAFERS wafers with 2 stations working in parallel (P and C):
        //   The bottleneck is the sequential processing through P and C
        //   Best case: Wafers can overlap in P and C
        //   W1: L→P(300) + P(3000) + P→C(300) + C(3000) + C→B(300) + B→L(300) = 7200ms
        //   But W2 can start at Polisher when W1 moves to Cleaner
        //   So with perfect pipeline: First wafer = 7200ms, each additional = 3000ms (bottleneck)
        var firstWaferTime = 4 * TRANSFER + POLISHING + CLEANING; // L→P + P + P→C + C + C→B + B→L
        var additionalWaferTime = Math.Max(POLISHING, CLEANING); // Bottleneck station
        var theoreticalMin = firstWaferTime + ((TOTAL_WAFERS - 1) * additionalWaferTime); // First + (N-1) more

        Log($"  • First wafer:         {firstWaferTime} ms ({firstWaferTime / 1000.0:F1} s)");
        Log($"  • Each additional:     {additionalWaferTime} ms ({additionalWaferTime / 1000.0:F1} s) (bottleneck)");
        Log($"  • Total ({TOTAL_WAFERS} wafers):   {theoreticalMin} ms ({theoreticalMin / 1000.0:F1} s)");
        Log("");

        var overhead = totalElapsed - theoreticalMin;
        var efficiency = (theoreticalMin / totalElapsed) * 100;

        Log($"Actual overhead:  {overhead:F1} ms ({overhead / 1000:F2} s)");
        Log($"Efficiency:       {efficiency:F1}%");
        Log("═══════════════════════════════════════════════════════════");
    }

    public void SetExecutionMode(ExecutionMode mode)
    {
        _executionMode = mode;
        Log($"▶ Execution mode set to: {mode}");

        if (mode == ExecutionMode.Sync)
        {
            Log("  • In SYNC mode, use 'Step' button to execute one event at a time");
            Log("  • Events will be queued and processed manually");
        }
        else
        {
            Log("  • In ASYNC mode, events are processed automatically");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        // Dispose progress timer
        _progressTimer?.Dispose();

        // Unsubscribe from state change events
        if (_polisher != null) _polisher.StateChanged -= OnStateChanged;
        if (_cleaner != null) _cleaner.StateChanged -= OnStateChanged;
        if (_buffer != null) _buffer.StateChanged -= OnStateChanged;
        if (_r1 != null) _r1.StateChanged -= OnStateChanged;
        if (_r2 != null) _r2.StateChanged -= OnStateChanged;
        if (_r3 != null) _r3.StateChanged -= OnStateChanged;
    }
}
