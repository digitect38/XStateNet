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
        // Forward Priority Scheduler
        // P1: C→B (highest) - R3
        // P2: P→C - R2
        // P3: L→P - R1
        // P4: B→L (lowest) - R1

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(POLL_INTERVAL, ct);

            // Check if all wafers completed
            lock (_stateLock)
            {
                if (_lCompleted.Count >= 25)
                {
                    Log("✓ All 25 wafers completed!");
                    break;
                }
            }

            // Priority 1: C → B (R3)
            if (CanExecuteCtoB())
            {
                _ = Task.Run(() => ExecuteCtoB(ct), ct);
            }

            // Priority 2: P → C (R2)
            if (CanExecutePtoC())
            {
                _ = Task.Run(() => ExecutePtoC(ct), ct);
            }

            // Priority 3: L → P (R1)
            if (CanExecuteLtoP())
            {
                _ = Task.Run(() => ExecuteLtoP(ct), ct);
            }

            // Priority 4: B → L (R1)
            if (CanExecuteBtoL())
            {
                _ = Task.Run(() => ExecuteBtoL(ct), ct);
            }
        }
    }

    // Priority 1: C → B (R3)
    private bool CanExecuteCtoB()
    {
        lock (_stateLock)
        {
            return _cleaner!.CurrentState.Contains(".done") &&
                   _r3!.CurrentState.Contains(".idle") &&
                   _buffer!.CurrentState.Contains(".empty");
        }
    }

    private async Task ExecuteCtoB(CancellationToken ct)
    {
        int waferId = _cleaner!.CurrentWafer ?? 0;
        if (waferId == 0) return;

        Log($"[P1] C→B: R3 transferring wafer {waferId}");

        // Set transfer info and send TRANSFER command
        _r3!.SetTransferInfo(waferId, "cleaner", "buffer");
        _buffer!.SetWafer(waferId);

        await _orchestrator.SendEventAsync("scheduler", "R3", "TRANSFER");

        // Wait for transfer to complete (R3 will return to idle)
        int timeout = 5000;
        int elapsed = 0;
        while (!_r3.CurrentState.Contains(".idle") && elapsed < timeout)
        {
            await Task.Delay(50, ct);
            elapsed += 50;
        }
    }

    // Priority 2: P → C (R2)
    private bool CanExecutePtoC()
    {
        lock (_stateLock)
        {
            bool cleanerAvailable = _cleaner!.CurrentState.Contains(".empty") ||
                                  (_cleaner.CurrentState.Contains(".done") && _r3!.CurrentState.Contains(".idle"));

            return _polisher!.CurrentState.Contains(".done") &&
                   _r2!.CurrentState.Contains(".idle") &&
                   cleanerAvailable;
        }
    }

    private async Task ExecutePtoC(CancellationToken ct)
    {
        int waferId = _polisher!.CurrentWafer ?? 0;
        if (waferId == 0) return;

        Log($"[P2] P→C: R2 transferring wafer {waferId}");

        _r2!.SetTransferInfo(waferId, "polisher", "cleaner");
        _cleaner!.SetWafer(waferId);

        await _orchestrator.SendEventAsync("scheduler", "R2", "TRANSFER");

        int timeout = 5000;
        int elapsed = 0;
        while (!_r2.CurrentState.Contains(".idle") && elapsed < timeout)
        {
            await Task.Delay(50, ct);
            elapsed += 50;
        }
    }

    // Priority 3: L → P (R1)
    private bool CanExecuteLtoP()
    {
        lock (_stateLock)
        {
            bool polisherAvailable = _polisher!.CurrentState.Contains(".empty") ||
                                   (_polisher.CurrentState.Contains(".done") && _r2!.CurrentState.Contains(".idle"));

            return _lPending.Count > 0 &&
                   _r1!.CurrentState.Contains(".idle") &&
                   polisherAvailable;
        }
    }

    private async Task ExecuteLtoP(CancellationToken ct)
    {
        int waferId;
        lock (_stateLock)
        {
            if (_lPending.Count == 0) return;
            waferId = _lPending[0];
            _lPending.RemoveAt(0);
        }

        Log($"[P3] L→P: R1 transferring wafer {waferId}");

        _r1!.SetTransferInfo(waferId, "LoadPort", "polisher");
        _polisher!.SetWafer(waferId);

        await _orchestrator.SendEventAsync("scheduler", "R1", "TRANSFER");

        int timeout = 5000;
        int elapsed = 0;
        while (!_r1.CurrentState.Contains(".idle") && elapsed < timeout)
        {
            await Task.Delay(50, ct);
            elapsed += 50;
        }
    }

    // Priority 4: B → L (R1)
    private bool CanExecuteBtoL()
    {
        lock (_stateLock)
        {
            // Only when no L→P work available
            bool canDoLtoP = _lPending.Count > 0 && _r1!.CurrentState.Contains(".idle");

            return _buffer!.CurrentState.Contains(".occupied") &&
                   _r1!.CurrentState.Contains(".idle") &&
                   !canDoLtoP;
        }
    }

    private async Task ExecuteBtoL(CancellationToken ct)
    {
        int waferId = _buffer!.CurrentWafer ?? 0;
        if (waferId == 0) return;

        Log($"[P4] B→L: R1 returning wafer {waferId}");

        _r1!.SetTransferInfo(waferId, "buffer", "LoadPort");

        await _orchestrator.SendEventAsync("scheduler", "R1", "TRANSFER");

        int timeout = 5000;
        int elapsed = 0;
        while (!_r1.CurrentState.Contains(".idle") && elapsed < timeout)
        {
            await Task.Delay(50, ct);
            elapsed += 50;
        }

        // Mark as completed
        lock (_stateLock)
        {
            _lCompleted.Add(waferId);

            // Mark wafer as completed (changes font color to white)
            var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
            if (wafer != null)
            {
                wafer.IsCompleted = true;
            }
        }

        Log($"✓ Wafer {waferId} completed ({_lCompleted.Count}/25)");
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
        // Update LoadPort wafers
        lock (_stateLock)
        {
            foreach (var waferId in _lPending.Concat(_lCompleted))
            {
                var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
                if (wafer != null)
                {
                    wafer.CurrentStation = "LoadPort";
                    var slot = _waferOriginalSlots[waferId];
                    var (x, y) = _stations["LoadPort"].GetWaferPosition(slot);
                    wafer.X = x;
                    wafer.Y = y;
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
