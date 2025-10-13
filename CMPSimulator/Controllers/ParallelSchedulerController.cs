using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using CMPSimulator.Models;
using CMPSimulator.StateMachines;
using XStateNet.Orchestration;

namespace CMPSimulator.Controllers;

/// <summary>
/// Controller using ParallelSchedulerMachine with Parallel states
/// Same interface as OrchestratedForwardPriorityController for easy testing
/// </summary>
public class ParallelSchedulerController : IForwardPriorityController
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Dictionary<string, StationPosition> _stations;
    private readonly Dictionary<int, int> _waferOriginalSlots;
    private readonly CancellationTokenSource _cts;

    // State Machines
    private ParallelSchedulerMachine? _scheduler;
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

    // Timing constants
    private const int POLISHING = 7000;
    private const int CLEANING = 7000;
    public const int TRANSFER = 1000;

    // Simulation configuration
    public const int TOTAL_WAFERS = 10;

    public ObservableCollection<Wafer> Wafers { get; }
    public Dictionary<string, StationPosition> Stations => _stations;

    public event EventHandler<string>? LogMessage;
    public event EventHandler? StationStatusChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    // Status properties
    public string PolisherStatus => _polisher?.CurrentState ?? "Unknown";
    public string CleanerStatus => _cleaner?.CurrentState ?? "Unknown";
    public string BufferStatus => _buffer?.CurrentState ?? "Unknown";
    public string R1Status => _r1?.CurrentState ?? "Unknown";
    public string R2Status => _r2?.CurrentState ?? "Unknown";
    public string R3Status => _r3?.CurrentState ?? "Unknown";

    public string PolisherRemainingTime => _polisher?.RemainingTimeMs > 0
        ? $"{(_polisher.RemainingTimeMs / 1000.0):F1}s"
        : "";
    public string CleanerRemainingTime => _cleaner?.RemainingTimeMs > 0
        ? $"{(_cleaner.RemainingTimeMs / 1000.0):F1}s"
        : "";

    public ParallelSchedulerController()
    {
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
        Log("CMP Tool Simulator - Parallel Scheduler Architecture");
        Log("Using XStateNet Parallel States for Concurrent Scheduling");
        Log("Architecture: Parallel regions for R1, R2, R3 management");
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
        // Use ParallelSchedulerMachine instead of SchedulerMachine
        _scheduler = new ParallelSchedulerMachine(_orchestrator, Log, TOTAL_WAFERS);
        _polisher = new PolisherMachine("polisher", _orchestrator, POLISHING, Log);
        _cleaner = new CleanerMachine("cleaner", _orchestrator, CLEANING, Log);
        _buffer = new BufferMachine(_orchestrator, Log);
        _r1 = new RobotMachine("R1", _orchestrator, TRANSFER, Log);
        _r2 = new RobotMachine("R2", _orchestrator, TRANSFER, Log);
        _r3 = new RobotMachine("R3", _orchestrator, TRANSFER, Log);

        _scheduler.AllWafersCompleted += (s, e) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Log($"✅ All {TOTAL_WAFERS} wafers completed!");
                LogTimingStatistics();
            });
        };

        Log("✓ State machines created (with Parallel Scheduler)");
    }

    private void SubscribeToStateUpdates()
    {
        _polisher!.StateChanged += OnStateChanged;
        _cleaner!.StateChanged += OnStateChanged;
        _buffer!.StateChanged += OnStateChanged;
        _r1!.StateChanged += OnStateChanged;
        _r2!.StateChanged += OnStateChanged;
        _r3!.StateChanged += OnStateChanged;
        _scheduler!.StateChanged += OnStateChanged;

        Log("✓ Subscribed to state update events (Direct Pub/Sub pattern)");
    }

    private void OnStateChanged(object? sender, XStateNet.Monitoring.StateTransitionEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            string currentState = e.StateMachineId switch
            {
                "polisher" => PolisherStatus,
                "cleaner" => CleanerStatus,
                "buffer" => BufferStatus,
                "R1" => R1Status,
                "R2" => R2Status,
                "R3" => R3Status,
                "parallelScheduler" => _scheduler?.CurrentState ?? "Unknown",
                _ => "Unknown"
            };

            Log($"[StateChanged] '{e.StateMachineId}': {e.FromState} → {e.ToState} (Event: {e.TriggerEvent}) | CurrentState={currentState}");

            UpdateWaferPositions();
            StationStatusChanged?.Invoke(this, EventArgs.Empty);

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
        _simulationStartTime = DateTime.Now;

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
        Log("▶ Simulation started (Parallel Scheduler - Event-driven mode)");

        _progressTimer = new System.Threading.Timer(
            UpdateProgress,
            null,
            0,
            100
        );

        _isInitialized = true;
    }

    public async Task ExecuteOneStep()
    {
        if (!_isInitialized)
        {
            await StartSimulation();
            return;
        }

        if (_executionMode == ExecutionMode.Async)
        {
            Log("⏭ Step execution not available in ASYNC mode");
            return;
        }

        Log("⏭ Executing one step...");
        Log("  [SYNC] Step executed (event processing)");

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
        foreach (var wafer in Wafers)
        {
            wafer.CurrentStation = "LoadPort";
            var slot = _waferOriginalSlots[wafer.Id];
            var (x, y) = _stations["LoadPort"].GetWaferPosition(slot);
            wafer.X = x;
            wafer.Y = y;
        }

        Log("↻ Simulation reset");
    }

    private void UpdateWaferPositions()
    {
        if (_scheduler != null)
        {
            var completedWafers = _scheduler.Completed;
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

                    if (completedWafers.Contains(waferId))
                    {
                        wafer.IsCompleted = true;
                    }
                }
            }
        }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PolisherRemainingTime)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CleanerRemainingTime)));
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void LogTimingStatistics()
    {
        var totalElapsed = (DateTime.Now - _simulationStartTime).TotalMilliseconds;

        Log("");
        Log("═══════════════════════════════════════════════════════════");
        Log("        TIMING STATISTICS (Parallel Scheduler)            ");
        Log("═══════════════════════════════════════════════════════════");
        Log($"Total simulation time: {totalElapsed:F1} ms ({totalElapsed / 1000:F2} s)");
        Log("");
        Log("Configured operation times (per wafer):");
        Log($"  • Polishing:  {POLISHING} ms ({POLISHING / 1000.0:F1} s)");
        Log($"  • Cleaning:   {CLEANING} ms ({CLEANING / 1000.0:F1} s)");
        Log($"  • Transfer:   {TRANSFER} ms ({TRANSFER / 1000.0:F1} s)");
        Log("");

        var firstWaferTime = 4 * TRANSFER + POLISHING + CLEANING;
        var additionalWaferTime = Math.Max(POLISHING, CLEANING);
        var theoreticalMin = firstWaferTime + ((TOTAL_WAFERS - 1) * additionalWaferTime);

        Log($"Theoretical minimum time for {TOTAL_WAFERS} wafers:");
        Log($"  • First wafer:         {firstWaferTime} ms ({firstWaferTime / 1000.0:F1} s)");
        Log($"  • Each additional:     {additionalWaferTime} ms ({additionalWaferTime / 1000.0:F1} s)");
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
    }

    public void UpdateSettings(int r1Transfer, int polisher, int r2Transfer, int cleaner, int r3Transfer, int bufferHold, int loadPortReturn)
    {
        Log($"⚠ ParallelSchedulerController does not support runtime settings update");
        Log($"  (This controller is for parallel scheduler experiments)");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        _progressTimer?.Dispose();

        if (_polisher != null) _polisher.StateChanged -= OnStateChanged;
        if (_cleaner != null) _cleaner.StateChanged -= OnStateChanged;
        if (_buffer != null) _buffer.StateChanged -= OnStateChanged;
        if (_r1 != null) _r1.StateChanged -= OnStateChanged;
        if (_r2 != null) _r2.StateChanged -= OnStateChanged;
        if (_r3 != null) _r3.StateChanged -= OnStateChanged;
        if (_scheduler != null) _scheduler.StateChanged -= OnStateChanged;
    }
}
