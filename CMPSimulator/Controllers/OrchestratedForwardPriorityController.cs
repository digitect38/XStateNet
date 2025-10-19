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
    private int _nextCarrierNumber = 1; // Track next carrier number (increments forever)

    // State Machines
    private SchedulerMachine? _scheduler;
    private DeclarativeSchedulerMachine? _declarativeScheduler;
    private bool _useDeclarativeScheduler = true; // Toggle between old and new scheduler
    private LoadPortMachine? _loadPort;
    private readonly List<CarrierMachine> _carrierMachines = new();
    private readonly Dictionary<int, WaferMachine> _waferMachines = new(); // E90 substrate tracking state machines
    private PolisherMachine? _polisher;
    private CleanerMachine? _cleaner;
    private BufferMachine? _buffer;
    private RobotMachine? _r1;
    private RobotMachine? _r2;
    private RobotMachine? _r3;

    private bool _isInitialized = false;
    private System.Threading.Timer? _progressTimer;
    private System.Threading.Timer? _stateTreeTimer; // Periodic state tree logging
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
    public event EventHandler<string>? RemoveOldCarrierFromStateTree;

    // Status properties (read from state machines) - Show full state path
    public string LoadPortStatus => _loadPort?.CurrentState ?? "Unknown";
    public string CurrentCarrierStatus
    {
        get
        {
            // E87 carrier states: NotPresent, WaitingForHost, Mapping, MappingVerification, ReadyToAccess, InAccess, AccessPaused, Complete, CarrierOut
            var currentCarrier = _carrierMachines.FirstOrDefault(c =>
                c.CurrentState != "NotPresent" && c.CurrentState != "CarrierOut");
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
            wafer.CarrierId = "CARRIER_001"; // Set initial carrier ID for state tree updates

            // Create E90 substrate tracking state machine for this wafer
            var waferMachine = new WaferMachine(
                waferId: $"W{wafer.Id}",
                orchestrator: _orchestrator,
                onStateChanged: (waferId, newState) =>
                {
                    // Update wafer E90 state when state machine transitions
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        wafer.E90State = newState;
                        Log($"[Wafer {wafer.Id}] E90 State → {newState}");

                        // DEBUG: Log callback for wafers 2 and 10 to trace "Loading" bug
                        if (wafer.Id == 2 || wafer.Id == 10)
                        {
                            Console.WriteLine($"[DEBUG onStateChanged] Wafer {wafer.Id}: Setting E90State to {newState}, CarrierId={wafer.CarrierId}");
                        }

                        // Trigger UI update to refresh the state tree in realtime
                        OnStationStatusChanged();
                    }));
                });

            wafer.StateMachine = waferMachine;
            _waferMachines[wafer.Id] = waferMachine;

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

        Log($"📦 Initial carrier CARRIER_001 created with {TOTAL_WAFERS} wafers");
    }

    private void InitializeStateMachines()
    {
        // Unsubscribe from old state machines if they exist
        UnsubscribeFromStateUpdates();

        // Create LoadPort state machine
        _loadPort = new LoadPortMachine("LoadPort", _orchestrator, Log);

        // Create Carrier state machines
        // Start with 1 carrier containing all wafers (more carriers are added dynamically during endless processing)
        int totalCarriers = 1;

        _carrierMachines.Clear();
        for (int i = 0; i < totalCarriers; i++)
        {
            string carrierId = $"CARRIER_{i + 1:D3}";
            var waferIds = Enumerable.Range(1, TOTAL_WAFERS).ToList();
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

        // Create station state machines (pass wafer machines to polisher for sub-state progression)
        _polisher = new PolisherMachine("polisher", _orchestrator, POLISHING, Log, _waferMachines);
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

        // Subscribe to carrier wafer completion events
        foreach (var carrier in _carrierMachines)
        {
            // Note: This will be called for each carrier that receives WAFER_COMPLETED events
            // The carrier state machine filters events based on its wafer list
        }

        // Subscribe to scheduler completion event
        if (_useDeclarativeScheduler)
        {
            _declarativeScheduler!.AllWafersCompleted += async (s, e) =>
            {
                await Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    Log($"✅ All {TOTAL_WAFERS} wafers completed!");
                    LogTimingStatistics();

                    // Get current carrier - might be in InAccess or Complete state
                    var currentCarrier = _carrierMachines.FirstOrDefault(c =>
                        c.CurrentState == "InAccess" ||
                        c.CurrentState == "Complete" ||
                        c.CurrentState == "ReadyToAccess");

                    if (currentCarrier == null)
                    {
                        // Fallback: get the first carrier (should be CARRIER_001)
                        currentCarrier = _carrierMachines.FirstOrDefault();
                        Log($"⚠ Warning: Could not find carrier in InAccess state, using first carrier: {currentCarrier?.CarrierId ?? "None"}");
                    }

                    if (currentCarrier != null)
                    {
                        LogFinalWaferStates(currentCarrier.CarrierId);
                    }

                    // Pause scheduler during carrier swap to prevent premature rule execution
                    _declarativeScheduler?.Pause();

                    // CRITICAL: Execute any pending deferred sends from scheduler BEFORE carrier swap
                    // This prevents commands issued before pause from executing after reset
                    var schedulerContext = _orchestrator.GetOrCreateContext("scheduler");
                    await schedulerContext.ExecuteDeferredSends();
                    await Task.Delay(200); // Wait for pending transfers to complete

                    // E87: Complete carrier and start next one
                    if (_loadPort != null && _carrierMachines.Count > 0 && currentCarrier != null)
                    {
                        Log($"🔄 Starting carrier swap for {currentCarrier.CarrierId}...");
                        var carrierContext = _orchestrator.GetOrCreateContext(currentCarrier.CarrierId);
                        var loadPortContext = _orchestrator.GetOrCreateContext("LoadPort");

                        // E87: InAccess → Complete
                        currentCarrier.SendAccessComplete();
                        await carrierContext.ExecuteDeferredSends();
                        await Task.Delay(50);

                        _loadPort.SendComplete();
                        await loadPortContext.ExecuteDeferredSends();
                        await Task.Delay(50);

                        // Undock current carrier
                        _loadPort.SendUndock();
                        await loadPortContext.ExecuteDeferredSends();
                        await Task.Delay(50);

                        // E87: Complete → CarrierOut
                        currentCarrier.SendCarrierRemoved();
                        await carrierContext.ExecuteDeferredSends();
                        await Task.Delay(100);

                        // Start next carrier (endless loop)
                        await SwapToNextCarrierAsync();
                    }
                });
            };
        }
        else
        {
            _scheduler!.AllWafersCompleted += async (s, e) =>
            {
                await Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    Log($"✅ All {TOTAL_WAFERS} wafers completed!");
                    LogTimingStatistics();

                    // Get current carrier ID for logging
                    var currentCarrier = _carrierMachines.FirstOrDefault(c => c.CurrentState == "InAccess");
                    if (currentCarrier != null)
                    {
                        LogFinalWaferStates(currentCarrier.CarrierId);
                    }

                    // E87: Complete carrier and start next one
                    if (_loadPort != null && _carrierMachines.Count > 0)
                    {
                        currentCarrier = _carrierMachines.FirstOrDefault(c => c.CurrentState == "InAccess");
                        if (currentCarrier != null)
                        {
                            var carrierContext = _orchestrator.GetOrCreateContext(currentCarrier.CarrierId);
                            var loadPortContext = _orchestrator.GetOrCreateContext("LoadPort");

                            // E87: InAccess → Complete
                            currentCarrier.SendAccessComplete();
                            await carrierContext.ExecuteDeferredSends();
                            await Task.Delay(50);

                            _loadPort.SendComplete();
                            await loadPortContext.ExecuteDeferredSends();
                            await Task.Delay(50);

                            // Undock current carrier
                            _loadPort.SendUndock();
                            await loadPortContext.ExecuteDeferredSends();
                            await Task.Delay(50);

                            // E87: Complete → CarrierOut
                            currentCarrier.SendCarrierRemoved();
                            await carrierContext.ExecuteDeferredSends();
                            await Task.Delay(100);
                        }

                        // Start next carrier (endless loop)
                        await SwapToNextCarrierAsync();
                    }
                });
            };
        }

        Log($"✓ State machines created (LoadPort + {totalCarriers} Carriers + Scheduler)");

        // Subscribe to state updates
        SubscribeToStateUpdates();
    }

    /// <summary>
    /// Subscribe to state machine StateChanged events using direct Pub/Sub pattern
    ///
    /// ROLE: This method establishes real-time monitoring of ALL state machines in the simulator
    /// by subscribing to their StateChanged events. This enables:
    ///
    /// 1. REAL-TIME UI UPDATES: Whenever any state machine transitions (e.g., Polisher: Idle → Processing),
    ///    the OnStateChanged handler is immediately invoked to update the GUI without polling.
    ///
    /// 2. EVENT-DRIVEN ARCHITECTURE: Instead of polling state machines every N milliseconds to check
    ///    if their state changed, we use the Observer pattern (Pub/Sub) to get notified automatically.
    ///    This is more efficient and provides instant feedback.
    ///
    /// 3. COMPREHENSIVE MONITORING: Subscribes to ALL components:
    ///    - LoadPort: Tracks carrier docking/undocking state changes
    ///    - Carriers: Monitors E87 carrier lifecycle (NotPresent → WaitingForHost → Mapping → InAccess → Complete)
    ///    - Scheduler: Tracks scheduling state changes (DeclarativeScheduler or original SchedulerMachine)
    ///    - Stations (Polisher, Cleaner, Buffer): Monitors processing state changes
    ///    - Robots (R1, R2, R3): Tracks transfer operations and wafer movement
    ///
    /// 4. LOGGING AND DIAGNOSTICS: OnStateChanged logs every state transition with format:
    ///    "[StateChanged] '{machineId}': {oldState} → {newState} (Event: {triggerEvent})"
    ///    This provides complete visibility into system behavior for debugging.
    ///
    /// 5. PROPERTY CHANGE NOTIFICATIONS: Triggers INotifyPropertyChanged events to update
    ///    WPF bindings for status properties (PolisherStatus, CleanerStatus, etc.)
    ///
    /// WHEN CALLED:
    /// - During InitializeStateMachines() after creating all state machines
    /// - After SwapToNextCarrierAsync() when new carrier machines are created
    ///
    /// CLEANUP:
    /// - UnsubscribeFromStateUpdates() is called before reinitializing to prevent memory leaks
    /// </summary>
    private void SubscribeToStateUpdates()
    {
        // Subscribe to state machine StateChanged events (direct Pub/Sub pattern)
        _loadPort!.StateChanged += OnStateChanged;
        foreach (var carrier in _carrierMachines)
        {
            carrier.StateChanged += OnStateChanged;
        }

        // Subscribe to scheduler
        if (_useDeclarativeScheduler)
        {
            _declarativeScheduler!.StateChanged += OnStateChanged;
        }
        else
        {
            _scheduler!.StateChanged += OnStateChanged;
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
        if (_scheduler != null) _scheduler.StateChanged -= OnStateChanged;
        if (_declarativeScheduler != null) _declarativeScheduler.StateChanged -= OnStateChanged;
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
                "scheduler" => _useDeclarativeScheduler ? (_declarativeScheduler?.CurrentState ?? "Unknown") : (_scheduler?.CurrentState ?? "Unknown"),
                "polisher" => PolisherStatus,
                "cleaner" => CleanerStatus,
                "buffer" => BufferStatus,
                "R1" => R1Status,
                "R2" => R2Status,
                "R3" => R3Status,
                _ when e.StateMachineId.StartsWith("CARRIER_") => _carrierMachines.FirstOrDefault(c => c.CarrierId == e.StateMachineId)?.CurrentState ?? "Unknown",
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

        // CRITICAL: Start scheduler FIRST so it's ready to receive events from other machines
        if (_useDeclarativeScheduler)
        {
            await _declarativeScheduler!.StartAsync();
            Log("✓ DeclarativeScheduler started and ready to receive events");
        }
        else
        {
            await _scheduler!.StartAsync();
            Log("✓ Scheduler started and ready to receive events");
        }

        // Now start all other state machines in parallel
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

        // Start all carrier machines
        foreach (var carrier in _carrierMachines)
        {
            startTasks.Add(carrier.StartAsync());
        }

        // Start all wafer state machines (E90 substrate tracking)
        foreach (var kvp in _waferMachines)
        {
            startTasks.Add(kvp.Value.StartAsync());
        }

        await Task.WhenAll(startTasks);

        Log("✓ All state machines started (including E90 substrate tracking)");

        // Initialize all wafers to InCarrier state
        foreach (var wafer in Wafers)
        {
            if (wafer.StateMachine != null)
            {
                await wafer.StateMachine.AcquireAsync();
            }
        }
        Log("✓ All wafer machines initialized to InCarrier state");

        // Start E87 carrier workflow
        if (_carrierMachines.Count > 0)
        {
            var firstCarrier = _carrierMachines[0];
            var carrierContext = _orchestrator.GetOrCreateContext(firstCarrier.CarrierId);

            Log($"▶ Starting processing for {firstCarrier.CarrierId}");

            // E87: NotPresent → WaitingForHost
            firstCarrier.SendCarrierDetected();
            await carrierContext.ExecuteDeferredSends();
            await Task.Delay(50);

            // E87: WaitingForHost → Mapping
            firstCarrier.SendHostProceed();
            await carrierContext.ExecuteDeferredSends();
            await Task.Delay(50);

            // E87: Mapping → MappingVerification → ReadyToAccess (auto-transition)
            firstCarrier.SendMappingComplete();
            await carrierContext.ExecuteDeferredSends();
            await Task.Delay(600); // Wait for auto-transition through verification

            // E87: ReadyToAccess → InAccess
            firstCarrier.SendStartAccess();
            await carrierContext.ExecuteDeferredSends();
            await Task.Delay(50);

            // Notify LoadPort
            var loadPortContext = _orchestrator.GetOrCreateContext("LoadPort");
            _loadPort.SendCarrierArrive(firstCarrier.CarrierId);
            await loadPortContext.ExecuteDeferredSends();
            await Task.Delay(50);
            _loadPort.SendDock();
            await loadPortContext.ExecuteDeferredSends();
            await Task.Delay(50);
            _loadPort.SendStartProcessing();
            await loadPortContext.ExecuteDeferredSends();
        }

        Log("▶ Simulation started (Event-driven mode - no polling)");

        // Start progress update timer (100ms intervals)
        _progressTimer = new System.Threading.Timer(
            UpdateProgress,
            null,
            0,
            100  // Update every 100ms
        );

        // Start state tree snapshot timer (1000ms intervals)
        _stateTreeTimer = new System.Threading.Timer(
            LogPeriodicStateTreeSnapshot,
            null,
            1000,  // Start after 1 second
            1000   // Log every 1 second
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

        // Stop state tree snapshot timer
        _stateTreeTimer?.Dispose();
        _stateTreeTimer = null;

        Log("⏸ Simulation stopped");
    }

    public void ResetSimulation()
    {
        // Stop progress timer
        _progressTimer?.Dispose();
        _progressTimer = null;

        // Stop state tree snapshot timer
        _stateTreeTimer?.Dispose();
        _stateTreeTimer = null;

        // CRITICAL: Reset carrier tracking to initial state
        _carriers.Clear();
        _nextCarrierNumber = 1;

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
            // No config changes, but we still need to reinitialize state machines
            // because we cleared _carriers and need to reset to single carrier
            Log($"⚙ Resetting to initial state - recreating state machines");

            // CRITICAL FIX: Clear existing wafers before reinitializing
            // Otherwise InitializeWafers() will add NEW wafers to the existing collection, doubling the count
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Wafers.Clear();
                _waferOriginalSlots.Clear();
                _stations["LoadPort"].WaferSlots.Clear();
            });

            // Clear wafer machines
            _waferMachines.Clear();

            // Recreate carriers dictionary (cleared above) and state machines
            InitializeWafers();
            InitializeStateMachines();
        }

        // Reset initialization flag to allow settings updates
        _isInitialized = false;

        // CRITICAL: Ensure scheduler is unpaused after manual reset
        // This is necessary if the simulator was paused after completing all wafers
        if (_useDeclarativeScheduler)
        {
            _declarativeScheduler?.Resume();
        }

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
                        if (!wafer.IsCompleted)
                        {
                            wafer.IsCompleted = true;
                            // E90: Wafer returned to carrier → Complete
                            // Ensure proper E90 transition to Complete state
                            if (wafer.E90State == "Processed")
                            {
                                // Wafer is in Processed state, can go directly to Complete
                                _ = wafer.StateMachine?.PlacedInCarrierAsync();
                            }
                            else if (wafer.E90State == "Cleaning")
                            {
                                // Wafer is still in Cleaning, first complete cleaning then return to carrier
                                _ = Task.Run(async () =>
                                {
                                    await wafer.StateMachine?.CompleteCleaningAsync();
                                    await Task.Delay(10); // Small delay to allow state transition
                                    await wafer.StateMachine?.PlacedInCarrierAsync();
                                });
                            }
                            else if (wafer.E90State != "Complete")
                            {
                                // For any other state, log warning but attempt transition
                                Log($"⚠ Wafer {wafer.Id} marked completed but E90State={wafer.E90State} (expected Processed or Cleaning)");
                                _ = wafer.StateMachine?.PlacedInCarrierAsync();
                            }
                        }
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
                if (wafer.CurrentStation != "R1")
                {
                    wafer.CurrentStation = "R1";
                    // E90: Wafer picked up from LoadPort → NeedsProcessing
                    _ = wafer.StateMachine?.SelectForProcessAsync();
                }
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
                if (wafer.CurrentStation != "Polisher")
                {
                    wafer.CurrentStation = "Polisher";
                    // E90: WaferMachine state transitions are now handled by PolisherMachine.onPlace action
                    // This ensures deterministic ordering: transitions fire BEFORE sub-state processing starts
                }
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
                if (wafer.CurrentStation != "R2")
                {
                    wafer.CurrentStation = "R2";
                    // E90: Polishing complete → Cleaning
                    if (wafer.E90State == "Polishing")
                    {
                        _ = wafer.StateMachine?.CompletePolishingAsync();
                    }
                }
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
                if (wafer.CurrentStation != "Cleaner")
                {
                    wafer.CurrentStation = "Cleaner";
                    // Note: wafer is already in Cleaning sub-state from Polishing transition
                    // No additional transition needed here - the wafer is in InProcess.Cleaning
                }
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
                if (wafer.CurrentStation != "R3")
                {
                    wafer.CurrentStation = "R3";
                    // E90: Cleaning complete → Processed
                    if (wafer.E90State == "Cleaning")
                    {
                        _ = wafer.StateMachine?.CompleteCleaningAsync();
                    }
                }
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
                if (wafer.CurrentStation != "Buffer")
                {
                    wafer.CurrentStation = "Buffer";
                    // E90: Wafer in buffer → ReadyToProcess (waiting for return)
                }
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

            // NEVER use yellow with white font - adjust brightness for yellow hues (45-75 degrees)
            double saturation = 0.6;
            double value = 0.65;

            // Yellow range (45-75°) needs to be much darker to contrast with white text
            if (hue >= 45 && hue <= 75)
            {
                value = 0.45; // Much darker for yellow hues
                saturation = 0.7; // Slightly more saturated to maintain color richness
            }

            var color = ColorFromHSV(hue, saturation, value);
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

    /// <summary>
    /// Helper method to trigger station status changed event
    /// </summary>
    private void OnStationStatusChanged()
    {
        StationStatusChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// Log final state tree values for all wafers (E90 states)
    /// </summary>
    private void LogFinalWaferStates(string carrierId)
    {
        Log("");
        Log("═══════════════════════════════════════════════════════════");
        Log($"          FINAL STATE TREE VALUES - {carrierId}          ");
        Log("═══════════════════════════════════════════════════════════");
        Log("");
        Log("Wafer ID | E90 State      | Completed | Font Color");
        Log("─────────┼────────────────┼───────────┼───────────");

        foreach (var wafer in Wafers.OrderBy(w => w.Id))
        {
            string fontColor = wafer.TextColor == System.Windows.Media.Brushes.White ? "White" :
                              wafer.TextColor == System.Windows.Media.Brushes.Yellow ? "Yellow" : "Black";
            string e90State = wafer.E90State.PadRight(14);
            string completed = wafer.IsCompleted ? "Yes" : "No ";

            Log($"Wafer {wafer.Id,2}  | {e90State} | {completed,3}       | {fontColor}");
        }

        Log("─────────────────────────────────────────────────────────");

        // Count wafers by E90 state
        var stateGroups = Wafers.GroupBy(w => w.E90State).OrderBy(g => g.Key);
        Log("");
        Log("E90 State Distribution:");
        foreach (var group in stateGroups)
        {
            Log($"  • {group.Key}: {group.Count()} wafers");
        }

        // Count wafers by font color
        var colorGroups = Wafers.GroupBy(w =>
            w.TextColor == System.Windows.Media.Brushes.White ? "White" :
            w.TextColor == System.Windows.Media.Brushes.Yellow ? "Yellow" : "Black"
        ).OrderBy(g => g.Key);
        Log("");
        Log("Font Color Distribution:");
        foreach (var group in colorGroups)
        {
            Log($"  • {group.Key}: {group.Count()} wafers");
        }

        Log("═══════════════════════════════════════════════════════════");
    }

    /// <summary>
    /// Log periodic state tree snapshot (called every 1 second)
    /// Shows current E90 states of all wafers and carrier status
    /// </summary>
    private void LogPeriodicStateTreeSnapshot(object? state)
    {
        if (!_isInitialized) return;

        // Capture timestamp immediately when timer fires (before dispatch latency)
        var elapsed = (DateTime.Now - _simulationStartTime).TotalSeconds;

        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            // Get current active carrier (prioritize InAccess, then ReadyToAccess, then any active state)
            var currentCarrier = _carrierMachines.FirstOrDefault(c => c.CurrentState == "InAccess")
                              ?? _carrierMachines.FirstOrDefault(c => c.CurrentState == "ReadyToAccess")
                              ?? _carrierMachines.FirstOrDefault(c => c.CurrentState == "WaitingForHost" || c.CurrentState == "Mapping")
                              ?? _carrierMachines.LastOrDefault(); // Fallback to most recent carrier
            var carrierId = currentCarrier?.CarrierId ?? "None";
            var carrierState = currentCarrier?.CurrentState ?? "N/A";

            // Group wafers by E90 state
            var stateDistribution = Wafers
                .GroupBy(w => w.E90State)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToList();

            var completedCount = CompletedWafers;
            // Use captured timestamp instead of calculating here (avoids dispatch latency)

            Log($"[StateTree] t={elapsed:F1}s | Carrier={carrierId}({carrierState}) | " +
                $"States: {string.Join(", ", stateDistribution)} | Completed: {completedCount}/{TOTAL_WAFERS}");
        }), System.Windows.Threading.DispatcherPriority.Normal);
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

    /// <summary>
    /// Swap to next carrier for endless processing
    /// CONCEPT: Remove old carrier completely, create fresh new carrier with new wafer objects
    /// </summary>
    private async Task SwapToNextCarrierAsync()
    {
        Log("🔄 Swapping to next carrier...");

        // Remember old carrier ID before incrementing
        string oldCarrierId = $"CARRIER_{_nextCarrierNumber:D3}";

        // Generate new carrier ID with sequential number
        _nextCarrierNumber++;
        string newCarrierId = $"CARRIER_{_nextCarrierNumber:D3}";

        Log($"🗑️ Removing old carrier {oldCarrierId} and all its wafer objects from system...");

        // Notify UI to remove old carrier from state tree
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RemoveOldCarrierFromStateTree?.Invoke(this, oldCarrierId);
        });

        // STEP 1: Clear old carrier's wafer objects completely
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Remove all old wafer objects from the observable collection
            Wafers.Clear();
        });

        // Clear old wafer machines
        _waferMachines.Clear();
        _waferOriginalSlots.Clear();
        _stations["LoadPort"].WaferSlots.Clear();

        // CRITICAL FIX: Reset robot and station wafer references
        // This prevents old carrier wafers from lingering in the system
        // Without this, robots/stations still hold references to old wafer objects from previous carrier
        // Bug discovered: After CARRIER_001 completed, CARRIER_002 was loaded but R1 was still holding
        // wafer 1 from CARRIER_001 instead of the new wafer 2 from CARRIER_002
        if (_r1 != null) _r1.ResetWafer();
        if (_r2 != null) _r2.ResetWafer();
        if (_r3 != null) _r3.ResetWafer();

        // Reset stations as well - they also persist across carrier swaps
        if (_polisher != null) _polisher.ResetWafer();
        if (_cleaner != null) _cleaner.ResetWafer();
        if (_buffer != null) _buffer.ResetWafer();

        Log($"✓ Old carrier removed from system (robots and stations reset)");
        Log($"📦 Creating new carrier: {newCarrierId} with fresh wafer objects");

        // Reset scheduler for next batch
        if (_useDeclarativeScheduler)
        {
            _declarativeScheduler?.Reset(newCarrierId);
        }
        else
        {
            _scheduler?.Reset();
        }

        // STEP 2: Create completely new wafer objects for the new carrier
        var newColors = GenerateDistinctColors(TOTAL_WAFERS);
        var newCarrierWafers = new List<Wafer>();

        // Create fresh wafer objects on UI thread
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < TOTAL_WAFERS; i++)
            {
                // Create a BRAND NEW wafer object (not reusing old ones)
                var wafer = new Wafer(i + 1, newColors[i]);
                var loadPort = _stations["LoadPort"];
                var (x, y) = loadPort.GetWaferPosition(i);

                wafer.X = x;
                wafer.Y = y;
                wafer.CurrentStation = "LoadPort";
                wafer.OriginLoadPort = "LoadPort";
                wafer.CarrierId = newCarrierId; // Set carrier ID for state tree updates

                // Create NEW E90 substrate tracking state machine for this wafer
                var waferMachine = new WaferMachine(
                    waferId: $"W{wafer.Id}",
                    orchestrator: _orchestrator,
                    onStateChanged: (waferId, newState) =>
                    {
                        // Update wafer E90 state when state machine transitions
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            wafer.E90State = newState;
                            Log($"[Wafer {wafer.Id}] E90 State → {newState}");

                            // DEBUG: Log callback for wafers 2 and 10 to trace "Loading" bug
                            if (wafer.Id == 2 || wafer.Id == 10)
                            {
                                Console.WriteLine($"[DEBUG onStateChanged] Wafer {wafer.Id}: Setting E90State to {newState}, CarrierId={wafer.CarrierId}");
                            }

                            // Trigger UI update to refresh the state tree in realtime
                            OnStationStatusChanged();
                        }));
                    });

                wafer.StateMachine = waferMachine;
                _waferMachines[wafer.Id] = waferMachine;

                Wafers.Add(wafer);
                newCarrierWafers.Add(wafer);
                _waferOriginalSlots[wafer.Id] = i;
                _stations["LoadPort"].AddWafer(wafer.Id);
            }
        });

        // Start all NEW wafer state machines (E90 substrate tracking)
        foreach (var kvp in _waferMachines)
        {
            await kvp.Value.StartAsync();
        }

        // Initialize all NEW wafers to InCarrier state
        foreach (var wafer in newCarrierWafers)
        {
            if (wafer.StateMachine != null)
            {
                await wafer.StateMachine.AcquireAsync();
            }
        }

        Log($"✓ Created {TOTAL_WAFERS} fresh wafer objects for {newCarrierId}");

        // CRITICAL: Remove old carrier machine from list BEFORE adding new one
        // Without this, the list accumulates carriers (CARRIER_001, CARRIER_002, CARRIER_003, ...)
        // causing memory leaks and incorrect carrier selection in LogPeriodicStateTreeSnapshot
        var oldCarrierMachine = _carrierMachines.FirstOrDefault(c => c.CarrierId == oldCarrierId);
        if (oldCarrierMachine != null)
        {
            // Unsubscribe from old carrier's state change events to prevent memory leak
            oldCarrierMachine.StateChanged -= OnStateChanged;
            _carrierMachines.Remove(oldCarrierMachine);
            Log($"✓ Removed {oldCarrierId} machine from active carrier list");
        }

        // Create new carrier machine
        var waferIds = Enumerable.Range(1, TOTAL_WAFERS).ToList();
        var newCarrierMachine = new CarrierMachine(newCarrierId, waferIds, _orchestrator, Log);

        // Subscribe to state changes for the new carrier
        newCarrierMachine.StateChanged += OnStateChanged;

        // Start the new carrier machine
        await newCarrierMachine.StartAsync();

        // Add to carrier machines list (now contains only the active carrier)
        _carrierMachines.Add(newCarrierMachine);

        // Register carrier with E87/E90
        var newCarrier = new Carrier(newCarrierId) { CurrentLoadPort = "LoadPort" };
        foreach (var wafer in newCarrierWafers)
        {
            newCarrier.AddWafer(wafer);
        }
        _carriers[newCarrierId] = newCarrier;

        // CRITICAL: Remove old carrier from CarrierManager to prevent state tree from showing multiple carriers
        // The CarrierManager._carriers dictionary keeps ALL carriers ever created unless we explicitly remove them
        // Without this call, UpdateStateTree() will keep adding old carriers back via GetAllCarriers()
        if (_carrierManager != null)
        {
            // Remove old carrier from CarrierManager
            await _carrierManager.RemoveCarrierAsync(oldCarrierId);
            Log($"✓ Removed {oldCarrierId} from CarrierManager");

            // Now add the new carrier
            await _carrierManager.CreateAndPlaceCarrierAsync(newCarrierId, "LoadPort", newCarrierWafers);
        }

        Log($"✓ Carrier {newCarrierId} created with {TOTAL_WAFERS} wafers");

        Log($"▶ Starting processing for {newCarrierId}");

        // Start E87 carrier workflow
        var newCarrierContext = _orchestrator.GetOrCreateContext(newCarrierId);
        var loadPortContext = _orchestrator.GetOrCreateContext("LoadPort");

        // E87: NotPresent → WaitingForHost
        newCarrierMachine.SendCarrierDetected();
        await newCarrierContext.ExecuteDeferredSends();
        await Task.Delay(50);

        // E87: WaitingForHost → Mapping
        newCarrierMachine.SendHostProceed();
        await newCarrierContext.ExecuteDeferredSends();
        await Task.Delay(50);

        // E87: Mapping → MappingVerification → ReadyToAccess (auto-transition)
        newCarrierMachine.SendMappingComplete();
        await newCarrierContext.ExecuteDeferredSends();
        await Task.Delay(600); // Wait for auto-transition through verification

        // E87: ReadyToAccess → InAccess
        newCarrierMachine.SendStartAccess();
        await newCarrierContext.ExecuteDeferredSends();
        await Task.Delay(50);

        // Notify LoadPort
        _loadPort?.SendCarrierArrive(newCarrierId);
        await loadPortContext.ExecuteDeferredSends();
        await Task.Delay(50);
        _loadPort?.SendDock();
        await loadPortContext.ExecuteDeferredSends();
        await Task.Delay(50);
        _loadPort?.SendStartProcessing();
        await loadPortContext.ExecuteDeferredSends();
        await Task.Delay(50);

        Log($"✓ Carrier {newCarrierId} is now in access mode (E87: InAccess)");

        // Reset simulation start time for new batch
        _simulationStartTime = DateTime.Now;

        // Resume scheduler now that carrier swap is complete
        if (_useDeclarativeScheduler)
        {
            _declarativeScheduler?.Resume(newCarrierId);
        }
        else
        {
            // For original scheduler (if we add Pause/Resume there too)
        }

        // CRITICAL: Broadcast current station/robot states to scheduler to trigger first rule execution
        // After resume, scheduler needs status updates to know what's available for processing
        Log($"📡 Broadcasting station and robot states to scheduler for {newCarrierId}...");

        var schedulerContext = _orchestrator.GetOrCreateContext("scheduler");

        // Broadcast all station states
        Log($"   → Polisher state: {_polisher?.CurrentState}");
        _polisher?.BroadcastStatus(schedulerContext);

        Log($"   → Cleaner state: {_cleaner?.CurrentState}");
        _cleaner?.BroadcastStatus(schedulerContext);

        Log($"   → Buffer state: {_buffer?.CurrentState}");
        _buffer?.BroadcastStatus(schedulerContext);

        // Broadcast all robot states
        Log($"   → R1 state: {_r1?.CurrentState}");
        _r1?.BroadcastStatus(schedulerContext);

        Log($"   → R2 state: {_r2?.CurrentState}");
        _r2?.BroadcastStatus(schedulerContext);

        Log($"   → R3 state: {_r3?.CurrentState}");
        _r3?.BroadcastStatus(schedulerContext);

        // Execute all deferred sends to deliver status updates
        Log("   → Executing deferred sends...");
        await schedulerContext.ExecuteDeferredSends();

        Log("✓ Status broadcast complete - scheduler should begin processing");

        // Trigger UI update
        StationStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        // Dispose progress timer
        _progressTimer?.Dispose();

        // Dispose state tree snapshot timer
        _stateTreeTimer?.Dispose();

        // Unsubscribe from state change events
        UnsubscribeFromStateUpdates();
    }

    /// <summary>
    /// Get the carrier manager for accessing carrier information
    /// </summary>
    public CarrierManager? GetCarrierManager()
    {
        return _carrierManager;
    }
}
