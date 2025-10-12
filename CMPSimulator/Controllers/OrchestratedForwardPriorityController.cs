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
public class OrchestratedForwardPriorityController : INotifyPropertyChanged, IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly Dictionary<string, StationPosition> _stations;
    private readonly Dictionary<int, int> _waferOriginalSlots;
    private readonly CancellationTokenSource _cts;

    // State Machines
    private ProcessingStationMachine? _polisher;
    private ProcessingStationMachine? _cleaner;
    private BufferMachine? _buffer;
    private RobotMachine? _r1;
    private RobotMachine? _r2;
    private RobotMachine? _r3;

    // Scheduler state (simplified for now - will be moved to SchedulerMachine later)
    private readonly object _stateLock = new();
    private List<int> _lPending = new();
    private List<int> _lCompleted = new();
    private Task? _schedulerTask;
    private Task? _uiUpdateTask;

    // Timing constants
    private const int POLISHING = 3000;
    private const int CLEANING = 3000;
    public const int TRANSFER = 800;
    private const int POLL_INTERVAL = 100;

    public ObservableCollection<Wafer> Wafers { get; }
    public Dictionary<string, StationPosition> Stations => _stations;

    public event EventHandler<string>? LogMessage;
    public event EventHandler? StationStatusChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    // Status properties (read from state machines)
    public string PolisherStatus => GetMachineStatus(_polisher);
    public string CleanerStatus => GetMachineStatus(_cleaner);
    public string BufferStatus => _buffer?.CurrentState ?? "Unknown";
    public string R1Status => _r1?.CurrentState ?? "Unknown";
    public string R2Status => _r2?.CurrentState ?? "Unknown";
    public string R3Status => _r3?.CurrentState ?? "Unknown";

    public OrchestratedForwardPriorityController()
    {
        _orchestrator = new EventBusOrchestrator();
        _stations = new Dictionary<string, StationPosition>();
        _waferOriginalSlots = new Dictionary<int, int>();
        Wafers = new ObservableCollection<Wafer>();
        _cts = new CancellationTokenSource();

        InitializeStations();
        InitializeWafers();
        InitializeStateMachines();

        Log("═══════════════════════════════════════════════════════════");
        Log("CMP Tool Simulator - Orchestrated Forward Priority");
        Log("Using XStateNet State Machines + EventBusOrchestrator");
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
        var colors = GenerateDistinctColors(25);

        for (int i = 0; i < 25; i++)
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
            _lPending.Add(wafer.Id);
        }
    }

    private void InitializeStateMachines()
    {
        // Create state machines
        _polisher = new ProcessingStationMachine("polisher", _orchestrator, POLISHING, Log);
        _cleaner = new ProcessingStationMachine("cleaner", _orchestrator, CLEANING, Log);
        _buffer = new BufferMachine(_orchestrator, Log);
        _r1 = new RobotMachine("R1", _orchestrator, TRANSFER, Log);
        _r2 = new RobotMachine("R2", _orchestrator, TRANSFER, Log);
        _r3 = new RobotMachine("R3", _orchestrator, TRANSFER, Log);

        Log("✓ State machines created");
    }

    public async Task StartSimulation()
    {
        // Start all state machines
        await _polisher!.StartAsync();
        await _cleaner!.StartAsync();
        await _buffer!.StartAsync();
        await _r1!.StartAsync();
        await _r2!.StartAsync();
        await _r3!.StartAsync();

        Log("✓ All state machines started");

        // Start scheduler and UI update tasks
        _schedulerTask = Task.Run(() => SchedulerService(_cts.Token), _cts.Token);
        _uiUpdateTask = Task.Run(() => UIUpdateService(_cts.Token), _cts.Token);

        Log("▶ Simulation started");
    }

    public void StopSimulation()
    {
        _cts.Cancel();
        Log("⏸ Simulation stopped");
    }

    public void ResetSimulation()
    {
        // Reset wafer positions
        lock (_stateLock)
        {
            _lPending.Clear();
            _lCompleted.Clear();

            for (int i = 0; i < 25; i++)
            {
                _lPending.Add(i + 1);
            }
        }

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

    private async Task SchedulerService(CancellationToken ct)
    {
        // Simplified scheduler - just demonstrates the pattern
        // Full Forward Priority logic would go here

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(POLL_INTERVAL, ct);

            // TODO: Implement Forward Priority scheduling logic
            // For now, just log periodically
            // Real implementation would check machine states and send commands
        }
    }

    private async Task UIUpdateService(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(50, ct);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateWaferPositions();
                StationStatusChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private void UpdateWaferPositions()
    {
        // Update wafer positions based on machine states
        // This would read from state machines and update visual positions
    }

    private string GetMachineStatus(ProcessingStationMachine? machine)
    {
        if (machine == null) return "Unknown";

        var state = machine.CurrentState;
        if (state.Contains(".empty")) return "Empty";
        if (state.Contains(".processing")) return "Processing";
        if (state.Contains(".done")) return "Done";
        return state;
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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
