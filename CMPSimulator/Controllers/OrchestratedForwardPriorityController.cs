using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CMPSimulator.Models;
using CMPSimulator.Services;
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

    // E87 Carrier Management
    private CarrierManager? _carrierManager;
    private readonly Dictionary<string, Carrier> _carriers = new();

    // State Machines
    private SchedulerMachine? _scheduler;
    private DeclarativeSchedulerMachine? _declarativeScheduler;
    private bool _useDeclarativeScheduler = true; // Toggle between old and new scheduler
    private LoadPortMachine? _loadPort;
    private readonly List<CarrierMachine> _carrierMachines = new();
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

    // Timing configuration (can be updated via UpdateSettings)
    private int POLISHING;
    private int CLEANING;
    public int TRANSFER;  // public for state machines
    private int BUFFER_HOLD;
    private int LOADPORT_RETURN;

    // Simulation configuration
    public int TOTAL_WAFERS;  // Total number of wafers to process - public for state machines

    public ObservableCollection<Wafer> Wafers { get; }
    public Dictionary<string, StationPosition> Stations => _stations;

    public event EventHandler<string>? LogMessage;
    public event EventHandler? StationStatusChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    // Status properties (read from state machines) - Show full state path
    public string LoadPortStatus => _loadPort?.CurrentState ?? "Unknown";
    public string CurrentCarrierStatus
    {
        get
        {
            var currentCarrier = _carrierMachines.FirstOrDefault(c => c.CurrentState == "atLoadPort" || c.CurrentState == "transferring");
            return currentCarrier?.CurrentState ?? "None";
        }
    }
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

    // Real-time statistics properties for UI binding
    public string ElapsedTime
    {
        get
        {
            if (!_isInitialized) return "0.0s";
            var elapsed = (DateTime.Now - _simulationStartTime).TotalSeconds;
            return $"{elapsed:F1}s";
        }
    }

    public int CompletedWafers => _useDeclarativeScheduler
        ? (_declarativeScheduler?.Completed.Count ?? 0)
        : (_scheduler?.Completed.Count ?? 0);
    public int PendingWafers => TOTAL_WAFERS - CompletedWafers;

    public string TheoreticalMinTime
    {
        get
        {
            var firstWaferTime = 4 * TRANSFER + POLISHING + CLEANING;
            var additionalWaferTime = Math.Max(POLISHING, CLEANING);
            var theoreticalMin = firstWaferTime + ((TOTAL_WAFERS - 1) * additionalWaferTime);
            return $"{theoreticalMin / 1000.0:F1}s";
        }
    }

    public string Efficiency
    {
        get
        {
            if (!_isInitialized) return "0.0%";
            var totalElapsed = (DateTime.Now - _simulationStartTime).TotalMilliseconds;
            if (totalElapsed < 100) return "0.0%"; // Avoid division by near-zero

            var firstWaferTime = 4 * TRANSFER + POLISHING + CLEANING;
            var additionalWaferTime = Math.Max(POLISHING, CLEANING);
            var theoreticalMin = firstWaferTime + ((TOTAL_WAFERS - 1) * additionalWaferTime);
            var efficiency = (theoreticalMin / totalElapsed) * 100;
            return $"{efficiency:F1}%";
        }
    }

    public string Throughput
    {
        get
        {
            if (!_isInitialized) return "0.00 wafers/s";
            var elapsed = (DateTime.Now - _simulationStartTime).TotalSeconds;
            if (elapsed < 0.1) return "0.00 wafers/s";
            var throughput = CompletedWafers / elapsed;
            return $"{throughput:F2} wafers/s";
        }
    }

    public OrchestratedForwardPriorityController()
    {
        // Load settings
        var settings = Helpers.SettingsManager.LoadSettings();
        TRANSFER = settings.R1TransferTime;
        POLISHING = settings.PolisherTime;
        CLEANING = settings.CleanerTime;
        BUFFER_HOLD = settings.BufferHoldTime;
        LOADPORT_RETURN = settings.LoadPortReturnTime;
        TOTAL_WAFERS = settings.InitialWaferCount;

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

        // Initialize CarrierManager (E87/E90)
        _carrierManager = new CarrierManager("CMP_TOOL_001", _orchestrator);

        InitializeStations();
        InitializeWafers();
        InitializeStateMachines();

        Log("═══════════════════════════════════════════════════════════");
        Log("CMP Tool Simulator - Orchestrated Forward Priority");
        Log("Using XStateNet State Machines + EventBusOrchestrator");
        Log("Architecture: Pub/Sub pattern for state updates (no polling)");
        Log("SEMI Standards: E87 Carrier Management + E90 Substrate Tracking");
        Log($"Configuration: {TOTAL_WAFERS} wafers, Transfer={TRANSFER}ms, Polish={POLISHING}ms, Clean={CLEANING}ms");
        Log("═══════════════════════════════════════════════════════════");
    }

    private void InitializeStations()
    {
        // Initialize single LoadPort
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

        // Create all wafers for single LoadPort (Carrier 1)
        var carrier1Wafers = new List<Wafer>();
        for (int i = 0; i < TOTAL_WAFERS; i++)
        {
            var wafer = new Wafer(i + 1, colors[i]);
            var loadPort = _stations["LoadPort"];
            var (x, y) = loadPort.GetWaferPosition(i);

            wafer.X = x;
            wafer.Y = y;
            wafer.CurrentStation = "LoadPort";
            wafer.OriginLoadPort = "LoadPort";

            Wafers.Add(wafer);
            carrier1Wafers.Add(wafer);
            _waferOriginalSlots[wafer.Id] = i;
            _stations["LoadPort"].AddWafer(wafer.Id);
        }

        // Initialize E87/E90 carrier (single carrier)
        _carriers["CARRIER_001"] = new Carrier("CARRIER_001") { CurrentLoadPort = "LoadPort" };

        foreach (var wafer in carrier1Wafers)
        {
            _carriers["CARRIER_001"].AddWafer(wafer);
        }
    }

    private void InitializeStateMachines()
    {
        // Unsubscribe from old state machines if they exist
        UnsubscribeFromStateUpdates();

        // Create LoadPort state machine
        _loadPort = new LoadPortMachine("LoadPort", _orchestrator, Log);

        // Create Carrier state machines
        // Default: 2 carriers with 5 wafers each (adjust based on TOTAL_WAFERS)
        int wafersPerCarrier = TOTAL_WAFERS / 2;
        int totalCarriers = 2;

        _carrierMachines.Clear();
        for (int i = 0; i < totalCarriers; i++)
        {
            string carrierId = $"CARRIER_{i + 1:D3}";
            var waferIds = Enumerable.Range(i * wafersPerCarrier + 1, wafersPerCarrier).ToList();
            var carrierMachine = new CarrierMachine(carrierId, waferIds, _orchestrator, Log);
            _carrierMachines.Add(carrierMachine);
        }

        // Create scheduler (either original or declarative)
        if (_useDeclarativeScheduler)
        {
            // Use declarative scheduler with JSON rules
            var rulesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SchedulingRules", "CMP_Scheduling_Rules.json");
            _declarativeScheduler = new DeclarativeSchedulerMachine(rulesPath, _orchestrator, Log, TOTAL_WAFERS);
            Log($"✓ Using DeclarativeSchedulerMachine with rules from: {rulesPath}");
        }
        else
        {
            // Use original hardcoded scheduler
            _scheduler = new SchedulerMachine(_orchestrator, Log, TOTAL_WAFERS);
            Log("✓ Using original SchedulerMachine (hardcoded logic)");
        }

        // Create station state machines
        _polisher = new PolisherMachine("polisher", _orchestrator, POLISHING, Log);
        _cleaner = new CleanerMachine("cleaner", _orchestrator, CLEANING, Log);
        _buffer = new BufferMachine(_orchestrator, Log);
        _r1 = new RobotMachine("R1", _orchestrator, TRANSFER, Log);
        _r2 = new RobotMachine("R2", _orchestrator, TRANSFER, Log);
        _r3 = new RobotMachine("R3", _orchestrator, TRANSFER, Log);

        // Subscribe to LoadPort events
        _loadPort.CarrierDocked += (s, carrierId) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Log($"[LoadPort] 📦 Carrier {carrierId} docked");
                StationStatusChanged?.Invoke(this, EventArgs.Empty);
            });
        };

        _loadPort.CarrierUndocked += (s, carrierId) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Log($"[LoadPort] 📤 Carrier {carrierId} undocked");
                StationStatusChanged?.Invoke(this, EventArgs.Empty);
            });
        };

        // Subscribe to scheduler completion event
        if (_useDeclarativeScheduler)
        {
            _declarativeScheduler!.AllWafersCompleted += (s, e) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Log($"✅ All {TOTAL_WAFERS} wafers completed!");
                    LogTimingStatistics();

                    // Notify LoadPort and Carrier about completion
                    if (_loadPort != null && _carrierMachines.Count > 0)
                    {
                        var currentCarrier = _carrierMachines.FirstOrDefault(c => c.CurrentState == "atLoadPort");
                        if (currentCarrier != null)
                        {
                            currentCarrier.SendAllComplete();
                        }
                        _loadPort.SendComplete();
                    }
                });
            };
        }
        else
        {
            _scheduler!.AllWafersCompleted += (s, e) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Log($"✅ All {TOTAL_WAFERS} wafers completed!");
                    LogTimingStatistics();

                    // Notify LoadPort and Carrier about completion
                    if (_loadPort != null && _carrierMachines.Count > 0)
                    {
                        var currentCarrier = _carrierMachines.FirstOrDefault(c => c.CurrentState == "atLoadPort");
                        if (currentCarrier != null)
                        {
                            currentCarrier.SendAllComplete();
                        }
                        _loadPort.SendComplete();
                    }
                });
            };
        }

        Log($"✓ State machines created (LoadPort + {totalCarriers} Carriers + Scheduler)");

        // Subscribe to state updates
        SubscribeToStateUpdates();
    }

    private void SubscribeToStateUpdates()
    {
        // Subscribe to state machine StateChanged events (direct Pub/Sub pattern)
        _loadPort!.StateChanged += OnStateChanged;
        foreach (var carrier in _carrierMachines)
        {
            carrier.StateChanged += OnStateChanged;
        }
        _polisher!.StateChanged += OnStateChanged;
        _cleaner!.StateChanged += OnStateChanged;
        _buffer!.StateChanged += OnStateChanged;
        _r1!.StateChanged += OnStateChanged;
        _r2!.StateChanged += OnStateChanged;
        _r3!.StateChanged += OnStateChanged;

        Log("✓ Subscribed to state update events (Direct Pub/Sub pattern)");
    }

    private void UnsubscribeFromStateUpdates()
    {
        // Unsubscribe from old state machines if they exist
        if (_loadPort != null) _loadPort.StateChanged -= OnStateChanged;
        foreach (var carrier in _carrierMachines)
        {
            carrier.StateChanged -= OnStateChanged;
        }
        if (_polisher != null) _polisher.StateChanged -= OnStateChanged;
        if (_cleaner != null) _cleaner.StateChanged -= OnStateChanged;
        if (_buffer != null) _buffer.StateChanged -= OnStateChanged;
        if (_r1 != null) _r1.StateChanged -= OnStateChanged;
        if (_r2 != null) _r2.StateChanged -= OnStateChanged;
        if (_r3 != null) _r3.StateChanged -= OnStateChanged;
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
                "LoadPort" => LoadPortStatus,
                "polisher" => PolisherStatus,
                "cleaner" => CleanerStatus,
                "buffer" => BufferStatus,
                "R1" => R1Status,
                "R2" => R2Status,
                "R3" => R3Status,
                _ when e.StateMachineId.StartsWith("CARRIER_") => CurrentCarrierStatus,
                _ => "Unknown"
            };

            // Log state transition (CurrentState should now be correct)
            Log($"[StateChanged] '{e.StateMachineId}': {e.FromState} → {e.ToState} (Event: {e.TriggerEvent}) | CurrentState={currentState}");

            UpdateWaferPositions();
            StationStatusChanged?.Invoke(this, EventArgs.Empty);

            // Notify property changes for status properties
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadPortStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCarrierStatus)));
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

        // Initialize E87/E90 Load Port and Carrier
        if (_carrierManager != null)
        {
            Log("🔧 Initializing SEMI E87/E90 Load Port and Carrier...");
            await _carrierManager.InitializeLoadPortsAsync("LoadPort");

            // Register carrier with E87/E90
            foreach (var kvp in _carriers)
            {
                var carrierId = kvp.Key;
                var carrier = kvp.Value;
                var loadPortId = carrier.CurrentLoadPort ?? "LoadPort";

                Log($"📦 Registering Carrier {carrierId} at {loadPortId} with {carrier.Wafers.Count} wafers");

                // Create a copy of wafers list for E87/E90 registration
                var wafersList = carrier.Wafers.ToList();
                await _carrierManager.CreateAndPlaceCarrierAsync(carrierId, loadPortId, wafersList);
            }

            Log("✓ E87/E90 Carrier initialized");
        }

        // Start all state machines in parallel to avoid sequential startup delay
        var startTasks = new List<Task>
        {
            _loadPort!.StartAsync(),
            _polisher!.StartAsync(),
            _cleaner!.StartAsync(),
            _buffer!.StartAsync(),
            _r1!.StartAsync(),
            _r2!.StartAsync(),
            _r3!.StartAsync()
        };

        // Add scheduler based on configuration
        if (_useDeclarativeScheduler)
        {
            startTasks.Add(_declarativeScheduler!.StartAsync());
        }
        else
        {
            startTasks.Add(_scheduler!.StartAsync());
        }

        // Start all carrier machines
        foreach (var carrier in _carrierMachines)
        {
            startTasks.Add(carrier.StartAsync());
        }

        await Task.WhenAll(startTasks);

        Log("✓ All state machines started");

        // Start carrier workflow: Move first carrier to LoadPort
        if (_carrierMachines.Count > 0)
        {
            var firstCarrier = _carrierMachines[0];
            firstCarrier.SendMoveToLoadPort();
            await Task.Delay(50); // Small delay for state transition
            firstCarrier.SendArriveAtLoadPort();
            await Task.Delay(50);
            _loadPort.SendCarrierArrive(firstCarrier.CarrierId);
            await Task.Delay(50);
            _loadPort.SendDock();
            await Task.Delay(50);
            _loadPort.SendStartProcessing();
        }

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
        // Stop progress timer
        _progressTimer?.Dispose();
        _progressTimer = null;

        // Reload settings to pick up any changes
        var settings = Helpers.SettingsManager.LoadSettings();
        bool waferCountChanged = TOTAL_WAFERS != settings.InitialWaferCount;
        bool timingChanged = TRANSFER != settings.R1TransferTime ||
                            POLISHING != settings.PolisherTime ||
                            CLEANING != settings.CleanerTime;

        // Update configuration from settings
        TRANSFER = settings.R1TransferTime;
        POLISHING = settings.PolisherTime;
        CLEANING = settings.CleanerTime;
        BUFFER_HOLD = settings.BufferHoldTime;
        LOADPORT_RETURN = settings.LoadPortReturnTime;

        Log("↻ Simulation reset - reloading configuration");
        Log($"Configuration: {settings.InitialWaferCount} wafers, Transfer={TRANSFER}ms, Polish={POLISHING}ms, Clean={CLEANING}ms");

        // If wafer count changed, need to reinitialize wafers
        if (waferCountChanged)
        {
            TOTAL_WAFERS = settings.InitialWaferCount;
            Log($"⚙ Wafer count changed to {TOTAL_WAFERS} - reinitializing wafers and state machines");

            // Clear existing wafers
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Wafers.Clear();
                _waferOriginalSlots.Clear();
                _stations["LoadPort"].WaferSlots.Clear();
            });

            // Reinitialize wafers with new count
            InitializeWafers();

            // Recreate state machines with new wafer count
            InitializeStateMachines();
        }
        else if (timingChanged)
        {
            Log($"⚙ Timing changed - recreating state machines");

            // Reset wafer positions and completion status
            foreach (var wafer in Wafers)
            {
                // Reset to origin LoadPort (not always "LoadPort"!)
                wafer.CurrentStation = wafer.OriginLoadPort;
                wafer.IsCompleted = false;
                var slot = _waferOriginalSlots[wafer.Id];
                var loadPortStation = _stations[wafer.OriginLoadPort];
                var (x, y) = loadPortStation.GetWaferPosition(slot);
                wafer.X = x;
                wafer.Y = y;
            }

            // Recreate state machines with new timing
            InitializeStateMachines();
        }
        else
        {
            // No config changes, just reset wafer positions
            foreach (var wafer in Wafers)
            {
                // Reset to origin LoadPort (not always "LoadPort"!)
                wafer.CurrentStation = wafer.OriginLoadPort;
                wafer.IsCompleted = false;
                var slot = _waferOriginalSlots[wafer.Id];
                var loadPortStation = _stations[wafer.OriginLoadPort];
                var (x, y) = loadPortStation.GetWaferPosition(slot);
                wafer.X = x;
                wafer.Y = y;
            }
        }

        // Reset initialization flag to allow settings updates
        _isInitialized = false;

        Log("✓ Reset complete - ready to start");
    }

    // All scheduling logic moved to SchedulerMachine - event-driven architecture

    private void UpdateWaferPositions()
    {
        // Update LoadPort wafers (get lists from SchedulerMachine or DeclarativeSchedulerMachine)
        IReadOnlyList<int>? completedWafers = null;

        if (_useDeclarativeScheduler && _declarativeScheduler != null)
        {
            completedWafers = _declarativeScheduler.Completed;
        }
        else if (_scheduler != null)
        {
            completedWafers = _scheduler.Completed;
        }

        if (completedWafers != null)
        {

            // Pending wafers are wafers 1-TOTAL_WAFERS that haven't been completed yet
            var allWafers = Enumerable.Range(1, TOTAL_WAFERS);
            var pendingWafers = allWafers.Except(completedWafers);
            var loadPortWafers = pendingWafers.Concat(completedWafers);

            foreach (var waferId in loadPortWafers)
            {
                var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
                if (wafer != null)
                {
                    // Return wafer to its origin LoadPort
                    string originLoadPort = wafer.OriginLoadPort;
                    wafer.CurrentStation = originLoadPort;

                    var slot = _waferOriginalSlots[waferId];
                    var loadPortStation = _stations[originLoadPort];
                    var (x, y) = loadPortStation.GetWaferPosition(slot);
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

            // Notify UI of property changes for statistics
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ElapsedTime)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedWafers)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingWafers)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Throughput)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Efficiency)));
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

    public void UpdateSettings(int r1Transfer, int polisher, int r2Transfer, int cleaner, int r3Transfer, int bufferHold, int loadPortReturn)
    {
        // Check if simulation is running
        if (_isInitialized)
        {
            Log("⚠ Cannot update settings while simulation is running. Please reset first.");
            return;
        }

        // Update timing configuration (wafer count is managed by LoadPort)
        TRANSFER = r1Transfer;  // Note: All robot transfers use the same timing
        POLISHING = polisher;
        CLEANING = cleaner;
        BUFFER_HOLD = bufferHold;
        LOADPORT_RETURN = loadPortReturn;

        Log($"✓ Settings updated - recreating state machines with new timing values");
        Log($"  • R1 Transfer: {TRANSFER} ms");
        Log($"  • Polisher: {POLISHING} ms");
        Log($"  • R2 Transfer: {r2Transfer} ms");
        Log($"  • Cleaner: {CLEANING} ms");
        Log($"  • R3 Transfer: {r3Transfer} ms");
        Log($"  • Buffer Hold: {BUFFER_HOLD} ms");
        Log($"  • LoadPort Return: {LOADPORT_RETURN} ms");

        // Recreate state machines with new timing values
        InitializeStateMachines();

        // Update UI
        Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateWaferPositions();
            StationStatusChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        // Dispose progress timer
        _progressTimer?.Dispose();

        // Unsubscribe from state change events
        UnsubscribeFromStateUpdates();
    }
}
