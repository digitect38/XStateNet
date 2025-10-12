using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CMPSimulator.Models;
using XStateNet;

namespace CMPSimulator.Controllers;

/// <summary>
/// Forward Priority Scheduler-based CMP controller for WPF
/// Centralized scheduler with forward priority: Process equipment resource release first
/// </summary>
public class ForwardPriorityController : IForwardPriorityController
{
    private readonly Dictionary<string, StationPosition> _stations;
    private readonly Dictionary<int, int> _waferOriginalSlots;
    private readonly StreamWriter? _debugLog;
    private readonly System.Diagnostics.Stopwatch _stopwatch;
    private readonly object _stateLock = new object();  // Thread safety for all state changes
    private IStateMachine _machine = null!;  // Will be initialized in InitializeStateMachine
    private readonly CancellationTokenSource _cts;
    private Task? _schedulerTask;
    private Task? _uiUpdateTask;

    // Context
    private List<int> _lPending = new();
    private List<int> _lCompleted = new();
    private int? _r1;
    private int? _p;
    private int? _r2;
    private int? _r3;  // New robot for C↔B transfer
    private int? _c;
    private int? _b;
    private bool _pProcessing;
    private bool _cProcessing;
    private bool _r1Busy;
    private bool _r2Busy;
    private bool _r3Busy;  // R3 busy flag
    private bool _r1ReturningToL;  // R1이 L로 귀환 중인지 표시
    private List<int> _completed = new();
    private int _totalWafers = 25;

    // Previous states for transition display (Past→Present)
    private string _previousPolisherStatus = "Empty";
    private string _previousCleanerStatus = "Empty";
    private string _previousR1Status = "Empty";
    private string _previousR2Status = "Empty";
    private string _previousR3Status = "Empty";
    private string _previousBufferStatus = "Empty";

    public ObservableCollection<Wafer> Wafers { get; }
    public Dictionary<string, StationPosition> Stations => _stations;

    // Public properties for UI status display (Past→Present format)
    public string PolisherStatus
    {
        get
        {
            lock (_stateLock)
            {
                string currentStatus;
                if (_pProcessing) currentStatus = "Processing";
                else if (_p.HasValue) currentStatus = "Done";
                else currentStatus = "Empty";

                // Skip transition display if state hasn't changed
                if (_previousPolisherStatus == currentStatus)
                    return currentStatus;

                return $"{_previousPolisherStatus}→{currentStatus}";
            }
        }
    }

    public string CleanerStatus
    {
        get
        {
            lock (_stateLock)
            {
                string currentStatus;
                if (_cProcessing) currentStatus = "Processing";
                else if (_c.HasValue) currentStatus = "Done";
                else currentStatus = "Empty";

                // Skip transition display if state hasn't changed
                if (_previousCleanerStatus == currentStatus)
                    return currentStatus;

                return $"{_previousCleanerStatus}→{currentStatus}";
            }
        }
    }

    // Helper method to update previous state before changing current state
    private void UpdatePreviousPolisherState()
    {
        if (_pProcessing) _previousPolisherStatus = "Processing";
        else if (_p.HasValue) _previousPolisherStatus = "Done";
        else _previousPolisherStatus = "Empty";
    }

    private void UpdatePreviousCleanerState()
    {
        if (_cProcessing) _previousCleanerStatus = "Processing";
        else if (_c.HasValue) _previousCleanerStatus = "Done";
        else _previousCleanerStatus = "Empty";
    }

    private void UpdatePreviousR1State()
    {
        if (_r1Busy) _previousR1Status = "Busy";
        else if (_r1.HasValue) _previousR1Status = "Holding";
        else _previousR1Status = "Empty";
    }

    private void UpdatePreviousR2State()
    {
        if (_r2Busy) _previousR2Status = "Busy";
        else if (_r2.HasValue) _previousR2Status = "Holding";
        else _previousR2Status = "Empty";
    }

    private void UpdatePreviousR3State()
    {
        if (_r3Busy) _previousR3Status = "Busy";
        else if (_r3.HasValue) _previousR3Status = "Holding";
        else _previousR3Status = "Empty";
    }

    private void UpdatePreviousBufferState()
    {
        _previousBufferStatus = _b.HasValue ? "Occupied" : "Empty";
    }

    public string R1Status
    {
        get
        {
            lock (_stateLock)
            {
                string currentStatus;
                if (_r1Busy) currentStatus = "Busy";
                else if (_r1.HasValue) currentStatus = "Holding";
                else currentStatus = "Empty";

                if (_previousR1Status == currentStatus)
                    return currentStatus;

                return $"{_previousR1Status}→{currentStatus}";
            }
        }
    }

    public string R2Status
    {
        get
        {
            lock (_stateLock)
            {
                string currentStatus;
                if (_r2Busy) currentStatus = "Busy";
                else if (_r2.HasValue) currentStatus = "Holding";
                else currentStatus = "Empty";

                if (_previousR2Status == currentStatus)
                    return currentStatus;

                return $"{_previousR2Status}→{currentStatus}";
            }
        }
    }

    public string R3Status
    {
        get
        {
            lock (_stateLock)
            {
                string currentStatus;
                if (_r3Busy) currentStatus = "Busy";
                else if (_r3.HasValue) currentStatus = "Holding";
                else currentStatus = "Empty";

                if (_previousR3Status == currentStatus)
                    return currentStatus;

                return $"{_previousR3Status}→{currentStatus}";
            }
        }
    }

    public string BufferStatus
    {
        get
        {
            lock (_stateLock)
            {
                string currentStatus = _b.HasValue ? "Occupied" : "Empty";

                if (_previousBufferStatus == currentStatus)
                    return currentStatus;

                return $"{_previousBufferStatus}→{currentStatus}";
            }
        }
    }

    public event EventHandler<string>? LogMessage;
    public event EventHandler? StationStatusChanged;  // New event for UI status updates
    public event PropertyChangedEventHandler? PropertyChanged;

    // Timing constants
    private const int POLISHING = 3000;   // 3 seconds
    private const int CLEANING = 3000;    // 3 seconds
    public const int TRANSFER = 800;     // 800ms - public for animation sync
    private const int POLL_INTERVAL = 100;

    public ForwardPriorityController()
    {
        _stopwatch = new System.Diagnostics.Stopwatch();  // Initialize first!
        _stations = new Dictionary<string, StationPosition>();
        _waferOriginalSlots = new Dictionary<int, int>();
        Wafers = new ObservableCollection<Wafer>();
        _cts = new CancellationTokenSource();

        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CMPSimulator_ForwardPriority.log");
            _debugLog = new StreamWriter(logPath, false) { AutoFlush = true };
            DebugLog("=== Forward Priority CMP Simulator Started ===");
        }
        catch
        {
            _debugLog = null;
        }

        InitializeStations();
        InitializeWafers();
        InitializeStateMachine();

        DebugLog("=== Controller Initialization Complete ===");
    }

    private void InitializeStations()
    {
        _stations["LoadPort"] = new StationPosition("LoadPort", 46, 256, 108, 108, 25);  // Y=310 center aligned with R1
        _stations["R1"] = new StationPosition("R1", 250, 270, 80, 80, 0);  // Y=310 aligned with P, R2, C
        _stations["Polisher"] = new StationPosition("Polisher", 420, 250, 120, 120, 1);  // Y=310
        _stations["R2"] = new StationPosition("R2", 590, 270, 80, 80, 0);  // Y=310 aligned with R1, P, C
        _stations["Cleaner"] = new StationPosition("Cleaner", 760, 250, 120, 120, 1);  // Y=310
        _stations["R3"] = new StationPosition("R3", 590, 460, 80, 80, 0);  // Y=500 aligned with Buffer
        _stations["Buffer"] = new StationPosition("Buffer", 440, 460, 80, 80, 1);  // X=480, Y=500 aligned with R3
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

    private void InitializeStateMachine()
    {
        var definition = """
        {
            "id": "forwardPriorityCMP",
            "initial": "running",
            "states": {
                "running": {
                    "on": {
                        "LOG_STATE": { "actions": ["logState"] },
                        "CHECK_COMPLETE": [
                            { "cond": "isAllComplete", "target": "completed" }
                        ]
                    }
                },
                "completed": {
                    "type": "final",
                    "entry": ["logComplete"]
                }
            }
        }
        """;

        var guards = new GuardMap
        {
            ["isAllComplete"] = new NamedGuard(_ => _completed.Count == _totalWafers, "isAllComplete")
        };

        var actions = new ActionMap
        {
            ["logState"] = new List<NamedAction>
            {
                new NamedAction("logState", async _ =>
                {
                    LogCurrentState();
                    await Task.CompletedTask;
                })
            },
            ["logComplete"] = new List<NamedAction>
            {
                new NamedAction("logComplete", async _ =>
                {
                    Log("\n========== Simulation Complete ==========");
                    Log($"Total Wafers Processed: {_completed.Count}/{_totalWafers}");
                    Log($"LoadPort Completed: {FormatWaferList(_lCompleted)}");
                    Log("=========================================\n");
                    await Task.CompletedTask;
                })
            }
        };

        _machine = StateMachineFactory.CreateFromScript(
            jsonScript: definition,
            threadSafe: false,
            guidIsolate: false,
            actionCallbacks: actions,
            guardCallbacks: guards
        );
    }

    public async Task StartSimulation()
    {
        await _machine.StartAsync();
        _stopwatch.Restart();
        Log("✅ Forward Priority Scheduler Started (with R3 Robot)");
        Log("Priority: P1(C→R3→B) > P2(P→R2→C) > P3(L→R1→P) > P4(B→R1→L)");
        Log("Robots: R1(L↔P↔B), R2(P↔C), R3(C↔B)");

        _schedulerTask = Task.Run(() => SchedulerService(_cts.Token), _cts.Token);
        _uiUpdateTask = Task.Run(() => UIUpdateService(_cts.Token), _cts.Token);
    }

    public async Task ExecuteOneStep()
    {
        if (!_stopwatch.IsRunning)
        {
            await _machine.StartAsync();
            _stopwatch.Restart();
            Log("⏭ Step Mode");
        }

        // Execute highest priority action that can run
        CancellationToken ct = CancellationToken.None;

        // P1: C→B (highest priority)
        if (CanExecCtoB())
        {
            await ExecCtoB(ct);
            return;
        }

        // P2: P→C
        if (CanExecPtoC())
        {
            await ExecPtoC(ct);
            return;
        }

        // P3: L→P
        if (CanExecLtoP())
        {
            await ExecLtoP(ct);
            return;
        }

        // P4: B→L (lowest priority)
        if (CanExecBtoL())
        {
            await ExecBtoL(ct);
            return;
        }

        Log("⏭ No action available (all robots busy or conditions not met)");
    }

    public void StopSimulation()
    {
        _cts.Cancel();
        Log("⏸ Simulation Paused");
    }

    public void ResetSimulation()
    {
        _cts.Cancel();

        // Reset context
        _lPending = Enumerable.Range(1, 25).ToList();
        _lCompleted.Clear();
        _r1 = null;
        _p = null;
        _r2 = null;
        _r3 = null;
        _c = null;
        _b = null;
        _pProcessing = false;
        _cProcessing = false;
        _r1Busy = false;
        _r2Busy = false;
        _r3Busy = false;
        _r1ReturningToL = false;

        // Reset previous states
        _previousPolisherStatus = "Empty";
        _previousCleanerStatus = "Empty";
        _previousR1Status = "Empty";
        _previousR2Status = "Empty";
        _previousR3Status = "Empty";
        _previousBufferStatus = "Empty";
        _completed.Clear();

        // Reset wafer positions
        foreach (var wafer in Wafers)
        {
            wafer.CurrentStation = "LoadPort";
            var slot = _waferOriginalSlots[wafer.Id];
            var (x, y) = _stations["LoadPort"].GetWaferPosition(slot);
            wafer.X = x;
            wafer.Y = y;
        }

        Log("↻ Simulation Reset");
    }

    // Scheduler Service - Forward Priority with True Parallel Execution
    private async Task SchedulerService(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_machine.GetActiveStateNames().Contains("completed"))
        {
            await Task.Delay(POLL_INTERVAL, ct);

            // Fire-and-forget: Start tasks without waiting
            // Guards use busy flags to prevent duplicate execution

            // Priority 1: C → B (highest) - R3
            if (CanExecCtoB())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecCtoB(ct);
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ ERROR in ExecCtoB: {ex.Message}");
                    }
                }, ct);
            }

            // Priority 2: P → C - R2
            if (CanExecPtoC())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecPtoC(ct);
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ ERROR in ExecPtoC: {ex.Message}");
                    }
                }, ct);
            }

            // Priority 3: L → P - R1
            if (CanExecLtoP())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecLtoP(ct);
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ ERROR in ExecLtoP: {ex.Message}");
                    }
                }, ct);
            }

            // Priority 4: B → L (lowest) - R1
            if (CanExecBtoL())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecBtoL(ct);
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ ERROR in ExecBtoL: {ex.Message}");
                    }
                }, ct);
            }
        }
    }

    // UI Update Service (faster polling for better sync)
    private async Task UIUpdateService(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(50, ct);  // 50ms for smoother UI updates

            Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateWaferPositions();

                // Notify UI to update station status displays
                StationStatusChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private void UpdateWaferPositions()
    {
        // Create snapshot of current state (thread-safe read)
        List<int> lPending, lCompleted;
        int? r1, p, r2, c, r3, b;
        bool r1Busy, r2Busy, r3Busy;
        bool pProcessing, cProcessing;

        lock (_stateLock)
        {
            lPending = _lPending.ToList();
            lCompleted = _lCompleted.ToList();
            r1 = _r1;
            p = _p;
            r2 = _r2;
            c = _c;
            r3 = _r3;
            b = _b;
            r1Busy = _r1Busy;
            r2Busy = _r2Busy;
            r3Busy = _r3Busy;
            pProcessing = _pProcessing;
            cProcessing = _cProcessing;

            // Update previous states for transition display
            // Previous state is already stored, current state will be computed in property getter
        }

        // Update LoadPort wafers (both pending and completed)
        foreach (var waferId in lPending.Concat(lCompleted))
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
            if (wafer != null)
            {
                // Force CurrentStation to LoadPort for all wafers in LoadPort lists
                wafer.CurrentStation = "LoadPort";
                var slot = _waferOriginalSlots[waferId];
                var (x, y) = _stations["LoadPort"].GetWaferPosition(slot);
                wafer.X = x;
                wafer.Y = y;
            }
        }

        // Update R1
        if (r1.HasValue)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == r1.Value);
            if (wafer != null)
            {
                wafer.CurrentStation = "R1";
                var pos = _stations["R1"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update Polisher
        if (p.HasValue)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == p.Value);
            if (wafer != null)
            {
                wafer.CurrentStation = "Polisher";
                var pos = _stations["Polisher"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update R2
        if (r2.HasValue)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == r2.Value);
            if (wafer != null)
            {
                wafer.CurrentStation = "R2";
                var pos = _stations["R2"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update Cleaner
        if (c.HasValue)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == c.Value);
            if (wafer != null)
            {
                wafer.CurrentStation = "Cleaner";
                var pos = _stations["Cleaner"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update R3
        if (r3.HasValue)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == r3.Value);
            if (wafer != null)
            {
                wafer.CurrentStation = "R3";
                var pos = _stations["R3"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }

        // Update Buffer
        if (b.HasValue)
        {
            var wafer = Wafers.FirstOrDefault(w => w.Id == b.Value);
            if (wafer != null)
            {
                wafer.CurrentStation = "Buffer";
                var pos = _stations["Buffer"];
                wafer.X = pos.X + pos.Width / 2;
                wafer.Y = pos.Y + pos.Height / 2;
            }
        }
    }

    // Guards (Thread-safe)
    private bool CanExecCtoB()
    {
        lock (_stateLock)
        {
            bool canExec = _c.HasValue && !_cProcessing && !_r3Busy && !_r3.HasValue && !_b.HasValue;

            if (_c.HasValue && _cProcessing)
            {
                // Debugging: Log when we can't pick because still processing
                DebugLog($"🚫 Cannot Pick C({_c.Value}): Still Cleaning (cProcessing={_cProcessing})");
            }

            return canExec;
        }
    }

    private bool CanExecPtoC()
    {
        lock (_stateLock)
        {
            // R2 can pick from Polisher if:
            // 1. Polisher has wafer and processing is done
            // 2. R2 is not busy
            // 3. Cleaner is either empty OR (has wafer that is Done and R3 will pick it up)
            bool cleanerAvailable = !_c.HasValue || (_c.HasValue && !_cProcessing && !_r3Busy);

            bool canExec = _p.HasValue && !_pProcessing && !_r2Busy && !_r2.HasValue && cleanerAvailable;

            if (_p.HasValue && _pProcessing)
            {
                // Debugging: Log when we can't pick because still processing
                DebugLog($"🚫 Cannot Pick P({_p.Value}): Still Processing (pProcessing={_pProcessing})");
            }

            return canExec;
        }
    }

    private bool CanExecLtoP()
    {
        lock (_stateLock)
        {
            // R1 can start L→P if:
            // 1. LoadPort has pending wafers
            // 2. R1 is not busy and not returning to LoadPort
            // 3. Polisher is empty OR (Polisher is Done AND R2 will pick it up soon)
            bool polisherWillBeAvailable = !_p.HasValue || (_p.HasValue && !_pProcessing);

            return _lPending.Count > 0 && !_r1Busy && !_r1.HasValue && polisherWillBeAvailable && !_r1ReturningToL;
        }
    }

    private bool CanExecBtoL()
    {
        lock (_stateLock)
        {
            // P4(B→L) has lowest priority - only execute when no higher priority work is possible
            // Check if P3(L→P) can execute
            bool canDoLtoP = _lPending.Count > 0 && !_r1Busy && !_r1.HasValue && !_p.HasValue && !_r1ReturningToL;

            // P4 can execute when: Buffer has wafer AND R1 is idle AND P3 cannot execute
            return _b.HasValue && !_r1Busy && !_r1.HasValue && !canDoLtoP;
        }
    }

    // Actions - Priority 1: C → R3 → B (R3 dedicated to C↔B)
    private async Task ExecCtoB(CancellationToken ct)
    {
        int waferId;
        lock (_stateLock)
        {
            if (!_c.HasValue || _cProcessing)
            {
                Log($"⚠️ ERROR: Cannot pick from Cleaner (Processing or empty)");
                return;
            }
            if (_b.HasValue)
            {
                Log($"⚠️ ERROR: Buffer is not empty (has wafer {_b.Value})");
                return;
            }
            waferId = _c.Value;
            UpdatePreviousCleanerState();
            _c = null;  // Cleaner now empty when R3 starts picking
            UpdatePreviousR3State();
            _r3 = waferId;
            _r3Busy = true;
        }

        Log($"[P1] C({waferId}) → R3 (Pick from Cleaner) - Cleaning was complete");

        // R3 picks up from Cleaner (800ms)
        await Task.Delay(TRANSFER, ct);

        Log($"[P1] R3({waferId}) → B (Moving to Buffer)");

        // R3 moves to Buffer (800ms)
        await Task.Delay(TRANSFER, ct);

        // Wait until Buffer is empty (check every 100ms)
        bool waitedForBuffer = false;
        while (true)
        {
            lock (_stateLock)
            {
                if (!_b.HasValue)
                {
                    // Buffer is now empty, R3 can place the wafer
                    UpdatePreviousR3State();
                    _r3 = null;
                    UpdatePreviousBufferState();
                    _b = waferId;
                    break;
                }
            }

            if (!waitedForBuffer)
            {
                Log($"⏸️ [P1] R3({waferId}) waiting at Buffer (still occupied by {_b.Value})");
                waitedForBuffer = true;
            }

            // Wait a bit before checking again
            await Task.Delay(POLL_INTERVAL, ct);
        }

        if (waitedForBuffer)
        {
            Log($"▶️ [P1] R3({waferId}) resuming - Buffer now empty");
        }

        Log($"[P1] R3 placed wafer {waferId} at Buffer ★");

        // R3 returns to idle position
        await Task.Delay(TRANSFER / 2, ct);

        lock (_stateLock)
        {
            UpdatePreviousR3State();
            _r3Busy = false;
        }
        Log($"[P1] R3 returned to idle position");
    }

    // Actions - Priority 2: P → R2 → C (R2 dedicated to P↔C)
    private async Task ExecPtoC(CancellationToken ct)
    {
        int waferId;
        lock (_stateLock)
        {
            if (!_p.HasValue || _pProcessing)
            {
                Log($"⚠️ ERROR: Cannot pick from Polisher (Processing or empty)");
                return;
            }
            waferId = _p.Value;
            UpdatePreviousPolisherState();
            _p = null;  // Polisher now empty when R2 starts picking
            UpdatePreviousR2State();
            _r2 = waferId;
            _r2Busy = true;
        }

        Log($"[P2] P({waferId}) → R2 (Pick from Polisher) - Polishing was complete");

        // R2 picks up from Polisher (800ms)
        await Task.Delay(TRANSFER, ct);

        Log($"[P2] R2({waferId}) → C (Moving to Cleaner)");

        // R2 moves to Cleaner (800ms)
        await Task.Delay(TRANSFER, ct);

        // Wait until Cleaner is empty (check every 100ms)
        bool waitedForCleaner = false;
        while (true)
        {
            lock (_stateLock)
            {
                if (!_c.HasValue)
                {
                    // Cleaner is now empty, R2 can place the wafer
                    UpdatePreviousR2State();
                    _r2 = null;
                    UpdatePreviousCleanerState();
                    _c = waferId;
                    _cProcessing = true;
                    break;
                }
            }

            if (!waitedForCleaner)
            {
                Log($"⏸️ [P2] R2({waferId}) waiting at Cleaner (still occupied by {_c.Value})");
                waitedForCleaner = true;
            }

            // Wait a bit before checking again
            await Task.Delay(POLL_INTERVAL, ct);
        }

        if (waitedForCleaner)
        {
            Log($"▶️ [P2] R2({waferId}) resuming - Cleaner now empty");
        }

        Log($"[P2] R2 placed wafer {waferId} at Cleaner");
        Log($"🧼 [Processing] C({waferId}) Cleaning START (will take {CLEANING}ms)");

        // R2 returns to idle position
        await Task.Delay(TRANSFER / 2, ct);

        lock (_stateLock)
        {
            UpdatePreviousR2State();
            _r2Busy = false;
        }
        Log($"[P2] R2 returned to idle position");

        _ = Task.Delay(CLEANING, ct).ContinueWith(_ =>
        {
            lock (_stateLock)
            {
                UpdatePreviousCleanerState();
                _cProcessing = false;
            }
            Log($"✅ [Processing] C({waferId}) Cleaning DONE (after {CLEANING}ms)");
        }, ct);
    }

    // Actions - Priority 3: L → R1 → P
    private async Task ExecLtoP(CancellationToken ct)
    {
        int waferId;
        lock (_stateLock)
        {
            if (_lPending.Count == 0)
            {
                Log($"⚠️ ERROR: No pending wafers in LoadPort");
                return;
            }
            waferId = _lPending[0];
            _lPending.RemoveAt(0);
            UpdatePreviousR1State();
            _r1 = waferId;
            _r1Busy = true;
        }

        Log($"[P3] L({waferId}) → R1 (Pick from LoadPort)");

        // R1 picks up from LoadPort (800ms)
        await Task.Delay(TRANSFER, ct);

        Log($"[P3] R1({waferId}) → P (Moving to Polisher)");

        // R1 moves to Polisher (800ms)
        await Task.Delay(TRANSFER, ct);

        // Wait until Polisher is empty (check every 100ms)
        bool waitedForPolisher = false;
        while (true)
        {
            lock (_stateLock)
            {
                if (!_p.HasValue)
                {
                    // Polisher is now empty, R1 can place the wafer
                    UpdatePreviousR1State();
                    _r1 = null;
                    UpdatePreviousPolisherState();
                    _p = waferId;
                    _pProcessing = true;
                    break;
                }
            }

            if (!waitedForPolisher)
            {
                Log($"⏸️ [P3] R1({waferId}) waiting at Polisher (still occupied by {_p.Value})");
                waitedForPolisher = true;
            }

            // Wait a bit before checking again
            await Task.Delay(POLL_INTERVAL, ct);
        }

        if (waitedForPolisher)
        {
            Log($"▶️ [P3] R1({waferId}) resuming - Polisher now empty");
        }

        Log($"[P3] R1 placed wafer {waferId} at Polisher");
        Log($"🔨 [Processing] P({waferId}) Polishing START (will take {POLISHING}ms)");

        // R1 returns to idle position
        await Task.Delay(TRANSFER / 2, ct);

        lock (_stateLock)
        {
            UpdatePreviousR1State();
            _r1Busy = false;
        }
        Log($"[P3] R1 returned to idle position");

        _ = Task.Delay(POLISHING, ct).ContinueWith(_ =>
        {
            lock (_stateLock)
            {
                UpdatePreviousPolisherState();
                _pProcessing = false;
            }
            Log($"✅ [Processing] P({waferId}) Polishing DONE (after {POLISHING}ms)");
        }, ct);
    }

    // Actions - Priority 4: B → R1 → L
    private async Task ExecBtoL(CancellationToken ct)
    {
        int waferId;
        lock (_stateLock)
        {
            if (!_b.HasValue)
            {
                Log($"⚠️ ERROR: Buffer is empty");
                return;
            }
            if (_r1Busy)
            {
                Log($"⚠️ ERROR: R1 is busy, cannot pick from Buffer");
                return;
            }
            waferId = _b.Value;
            UpdatePreviousR1State();
            _r1 = waferId;
            _r1Busy = true;
            _r1ReturningToL = true;
        }

        Log($"[P4] B({waferId}) → R1 (Pick from Buffer)");

        // R1 picks up from Buffer (800ms)
        await Task.Delay(TRANSFER, ct);

        lock (_stateLock)
        {
            UpdatePreviousBufferState();
            _b = null;  // Buffer now empty after robot picked it up
        }

        Log($"[P4] R1({waferId}) → L (Moving to LoadPort)");

        // R1 moves to LoadPort (800ms)
        await Task.Delay(TRANSFER, ct);

        lock (_stateLock)
        {
            _lCompleted.Add(waferId);
            _lCompleted.Sort();
            _completed.Add(waferId);
            UpdatePreviousR1State();
            _r1 = null;

            // Mark wafer as completed (changes font color to white)
            var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
            if (wafer != null)
            {
                wafer.IsCompleted = true;
            }
        }
        Log($"[P4] R1 placed wafer {waferId} at LoadPort ✓ Completed!");

        await Task.Delay(TRANSFER / 2, ct);

        lock (_stateLock)
        {
            UpdatePreviousR1State();
            _r1Busy = false;
            _r1ReturningToL = false;
        }
        Log($"[P4] R1 returned to idle position (DONE)");

        _machine.SendAndForget("CHECK_COMPLETE");
    }

    private void LogCurrentState()
    {
        var parts = new List<string>();

        if (_lPending.Count > 0 || _lCompleted.Count > 0)
        {
            var pending = FormatWaferList(_lPending);
            var completed = FormatWaferList(_lCompleted);
            if (!string.IsNullOrEmpty(pending) && !string.IsNullOrEmpty(completed))
                parts.Add($"@L({pending}, {completed})");
            else if (!string.IsNullOrEmpty(pending))
                parts.Add($"@L({pending},)");
            else if (!string.IsNullOrEmpty(completed))
                parts.Add($"@L(,{completed})");
        }
        if (_r1.HasValue) parts.Add($"@R1({_r1})");
        if (_p.HasValue) parts.Add($"@P({_p})");
        if (_r2.HasValue) parts.Add($"@R2({_r2})");
        if (_c.HasValue) parts.Add($"@C({_c})");
        if (_b.HasValue) parts.Add($"@B({_b})");

        DebugLog($"[State] {string.Join(", ", parts)}");
    }

    private string FormatWaferList(List<int> wafers)
    {
        if (!wafers.Any()) return "";
        if (wafers.Count == 1) return wafers[0].ToString();

        var ranges = new List<string>();
        int start = wafers[0];
        int end = wafers[0];

        for (int i = 1; i < wafers.Count; i++)
        {
            if (wafers[i] == end + 1)
            {
                end = wafers[i];
            }
            else
            {
                ranges.Add(start == end ? $"{start}" : $"{start}~{end}");
                start = end = wafers[i];
            }
        }
        ranges.Add(start == end ? $"{start}" : $"{start}~{end}");

        return string.Join(", ", ranges);
    }

    private void Log(string message)
    {
        DebugLog(message);
        LogMessage?.Invoke(this, message);
    }

    private void DebugLog(string message)
    {
        var elapsed = _stopwatch.IsRunning ? _stopwatch.Elapsed.TotalMilliseconds : 0;
        _debugLog?.WriteLine($"[T+{elapsed,8:F0}ms] {message}");
    }

    public void SetExecutionMode(ExecutionMode mode)
    {
        // ForwardPriorityController doesn't support mode switching
        // It always runs in its own polling-based mode
        Log($"⚠ ForwardPriorityController does not support execution mode switching");
        Log($"  (This controller uses polling-based execution)");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _schedulerTask?.Wait(TimeSpan.FromSeconds(1));
        _uiUpdateTask?.Wait(TimeSpan.FromSeconds(1));
        _cts?.Dispose();
        _debugLog?.Close();
        _debugLog?.Dispose();
    }
}
