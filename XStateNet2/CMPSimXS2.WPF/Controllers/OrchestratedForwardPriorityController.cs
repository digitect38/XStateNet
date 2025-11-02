using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Akka.Actor;
using CMPSimXS2.WPF.Controls;
using CMPSimXS2.WPF.Helpers;
using CMPSimXS2.WPF.Models;
using CMPSimXS2.WPF.Schedulers;
using CMPSimXS2.WPF.StateMachines;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace CMPSimXS2.WPF.Controllers;

/// <summary>
/// Orchestrated Forward Priority Controller using XStateNet2.Core
/// Manages the complete CMP simulation with robot scheduling and wafer journey orchestration
/// </summary>
public class OrchestratedForwardPriorityController : IForwardPriorityController, INotifyPropertyChanged
{
    // Akka actor system and state machines
    private ActorSystem? _actorSystem;
    private XStateMachineFactory? _machineFactory;
    private readonly Dictionary<string, IActorRef> _stationActors = new();
    private readonly Dictionary<string, string> _stationStates = new();

    // Schedulers
    private RobotScheduler? _robotScheduler;
    private WaferJourneyScheduler? _journeyScheduler;

    // Simulation state
    private DispatcherTimer? _simulationTimer;
    private DateTime _simulationStartTime;
    private bool _isRunning = false;
    private ExecutionMode _executionMode = ExecutionMode.Async;

    // Internal carrier manager
    private readonly CarrierManager _carrierManager;

    // Station status backing fields
    private string _r1Status = "Idle";
    private string _r2Status = "Idle";
    private string _r3Status = "Idle";
    private string _polisherStatus = "Idle";
    private string _cleanerStatus = "Idle";
    private string _bufferStatus = "Empty";
    private string _loadPortStatus = "Idle";

    // Metrics backing fields
    private string _theoreticalMinTime = "0:00";
    private string _polisherRemainingTime = "0";
    private string _cleanerRemainingTime = "0";
    private string _elapsedTime = "0:00";
    private int _completedWafers = 0;
    private int _pendingWafers = 25;
    private string _throughput = "0.0";
    private string _efficiency = "0%";

    // Station status properties
    public string R1Status { get => _r1Status; private set => SetProperty(ref _r1Status, value); }
    public string R2Status { get => _r2Status; private set => SetProperty(ref _r2Status, value); }
    public string R3Status { get => _r3Status; private set => SetProperty(ref _r3Status, value); }
    public string PolisherStatus { get => _polisherStatus; private set => SetProperty(ref _polisherStatus, value); }
    public string CleanerStatus { get => _cleanerStatus; private set => SetProperty(ref _cleanerStatus, value); }
    public string BufferStatus { get => _bufferStatus; private set => SetProperty(ref _bufferStatus, value); }
    public string LoadPortStatus { get => _loadPortStatus; private set => SetProperty(ref _loadPortStatus, value); }

    // Metrics properties
    public int TOTAL_WAFERS { get; private set; } = 25;
    public string TheoreticalMinTime { get => _theoreticalMinTime; private set => SetProperty(ref _theoreticalMinTime, value); }
    public string PolisherRemainingTime { get => _polisherRemainingTime; private set => SetProperty(ref _polisherRemainingTime, value); }
    public string CleanerRemainingTime { get => _cleanerRemainingTime; private set => SetProperty(ref _cleanerRemainingTime, value); }
    public string ElapsedTime { get => _elapsedTime; private set => SetProperty(ref _elapsedTime, value); }
    public int CompletedWafers { get => _completedWafers; private set => SetProperty(ref _completedWafers, value); }
    public int PendingWafers { get => _pendingWafers; private set => SetProperty(ref _pendingWafers, value); }
    public string Throughput { get => _throughput; private set => SetProperty(ref _throughput, value); }
    public string Efficiency { get => _efficiency; private set => SetProperty(ref _efficiency, value); }

    // Collections
    public ObservableCollection<Wafer> Wafers { get; } = new();

    // Events
    public event EventHandler? StationStatusChanged;
    public event EventHandler<string>? RemoveOldCarrierFromStateTree;
    public event PropertyChangedEventHandler? PropertyChanged;

    public OrchestratedForwardPriorityController()
    {
        _carrierManager = new CarrierManager();
        Initialize();
    }

    private void Initialize()
    {
        Logger.Instance.Info("Controller", "Initializing Orchestrated Forward Priority Controller");

        // Create Akka ActorSystem
        _actorSystem = ActorSystem.Create("CMPSimXS2-WPF");
        _machineFactory = new XStateMachineFactory(_actorSystem);

        // Initialize schedulers
        _robotScheduler = new RobotScheduler();
        _journeyScheduler = new WaferJourneyScheduler(_robotScheduler, Wafers);

        // Create station state machines
        CreateStationStateMachines();

        // Create robot state machines
        CreateRobotStateMachines();

        // Create wafers
        CreateWafers(TOTAL_WAFERS);

        // Setup simulation timer
        _simulationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100) // Process every 100ms
        };
        _simulationTimer.Tick += OnSimulationTick;

        Logger.Instance.Info("Controller", "Initialization complete");
    }

    private void CreateStationStateMachines()
    {
        // Create LoadPort (holds Carrier/FOUP)
        var loadPort = CreateStation("LoadPort", MachineDefinitions.GetLoadPortMachine());
        _stationActors["LoadPort"] = loadPort;
        _journeyScheduler!.RegisterStation("LoadPort", loadPort);

        // Create Polisher
        var polisher = CreateStation("Polisher", MachineDefinitions.GetPolisherMachine());
        _stationActors["Polisher"] = polisher;
        _journeyScheduler.RegisterStation("Polisher", polisher);

        // Create Cleaner
        var cleaner = CreateStation("Cleaner", MachineDefinitions.GetCleanerMachine());
        _stationActors["Cleaner"] = cleaner;
        _journeyScheduler.RegisterStation("Cleaner", cleaner);

        // Create Buffer
        var buffer = CreateStation("Buffer", MachineDefinitions.GetBufferMachine());
        _stationActors["Buffer"] = buffer;
        _journeyScheduler.RegisterStation("Buffer", buffer);

        Logger.Instance.Info("Controller", "Created 4 station state machines (LoadPort, Polisher, Cleaner, Buffer)");
    }

    private IActorRef CreateStation(string stationName, string machineJson)
    {
        var machine = _machineFactory!.FromJson(machineJson)
            .WithAction("storeWafer", (ctx, evtData) =>
            {
                if (evtData is Dictionary<string, object> data && data.ContainsKey("wafer"))
                {
                    var waferId = Convert.ToInt32(data["wafer"]);
                    ctx.Set("currentWafer", waferId);
                    Logger.Instance.Debug("Controller", $"{stationName} stored wafer {waferId}");
                }
            })
            .WithAction("startProcessing", (ctx, evt) =>
            {
                Logger.Instance.Debug("Controller", $"{stationName} started processing");
            })
            .WithAction("completeProcessing", (ctx, evt) =>
            {
                Logger.Instance.Debug("Controller", $"{stationName} completed processing");
            })
            .WithAction("clearWafer", (ctx, evt) =>
            {
                ctx.Set("currentWafer", null);
                Logger.Instance.Debug("Controller", $"{stationName} cleared wafer");
            })
            .WithAction("startCleaning", (ctx, evt) =>
            {
                Logger.Instance.Debug("Controller", $"{stationName} started cleaning");
            })
            .WithAction("completeCleaning", (ctx, evt) =>
            {
                Logger.Instance.Debug("Controller", $"{stationName} completed cleaning");
            })
            .BuildAndStart();

        _stationStates[stationName] = "idle";

        // Subscribe to state changes
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(1));
                    var newState = snapshot.CurrentState ?? "unknown";

                    if (_stationStates[stationName] != newState)
                    {
                        _stationStates[stationName] = newState;
                        UpdateStationStatus(stationName, newState);

                        // Extract current wafer
                        int? currentWafer = null;
                        if (snapshot.Context.ContainsKey("currentWafer"))
                        {
                            var waferValue = snapshot.Context["currentWafer"];
                            if (waferValue != null && waferValue is int wafer)
                                currentWafer = wafer;
                        }

                        _journeyScheduler?.UpdateStationState(stationName, newState, currentWafer);
                    }
                }
                catch
                {
                    // Ignore timeouts during polling
                }

                await Task.Delay(100);
            }
        });

        return machine;
    }

    private void CreateRobotStateMachines()
    {
        var robotIds = new[] { "Robot 1", "Robot 2", "Robot 3" };

        foreach (var robotId in robotIds)
        {
            var robot = _machineFactory!.FromJson(MachineDefinitions.GetRobotMachine())
                .WithAction("pickupWafer", (ctx, evtData) =>
                {
                    if (evtData is Dictionary<string, object> data && data.ContainsKey("waferId"))
                    {
                        var waferId = Convert.ToInt32(data["waferId"]);
                        ctx.Set("currentWafer", waferId);
                        Logger.Instance.Debug("Controller", $"{robotId} picked up wafer {waferId}");

                        // Simulate transfer
                        Task.Run(async () =>
                        {
                            await Task.Delay(1000); // 1 second transfer time
                            _robotScheduler?.UpdateRobotState(robotId, "idle");
                            Logger.Instance.Debug("Controller", $"{robotId} completed transfer");
                        });
                    }
                })
                .WithAction("placeWafer", (ctx, evt) =>
                {
                    Logger.Instance.Debug("Controller", $"{robotId} placed wafer");
                })
                .WithAction("clearWafer", (ctx, evt) =>
                {
                    ctx.Set("currentWafer", null);
                })
                .BuildAndStart();

            _stationActors[robotId] = robot;
            _robotScheduler!.RegisterRobot(robotId, robot);
            _robotScheduler.UpdateRobotState(robotId, "idle");
        }

        Logger.Instance.Info("Controller", "Created 3 robot state machines");
    }

    private void CreateWafers(int count)
    {
        Wafers.Clear();
        for (int i = 1; i <= count; i++)
        {
            var wafer = new Wafer(i, System.Windows.Media.Colors.Gray)
            {
                CurrentStation = "LoadPort",
                ProcessingState = "NotProcessed",
                JourneyStage = "InCarrier",
                IsCompleted = false
            };
            Wafers.Add(wafer);
        }

        PendingWafers = count;
        CompletedWafers = 0;

        Logger.Instance.Info("Controller", $"Created {count} wafers in LoadPort");
    }

    private void UpdateStationStatus(string stationName, string status)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (stationName)
            {
                case "LoadPort":
                    LoadPortStatus = status;
                    break;
                case "Polisher":
                    PolisherStatus = status;
                    break;
                case "Cleaner":
                    CleanerStatus = status;
                    break;
                case "Buffer":
                    BufferStatus = status;
                    break;
            }

            StationStatusChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnSimulationTick(object? sender, EventArgs e)
    {
        // Process wafer journeys
        _journeyScheduler?.ProcessWaferJourneys();

        // Update metrics
        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Update elapsed time
            var elapsed = DateTime.Now - _simulationStartTime;
            ElapsedTime = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

            // Update completed/pending counts
            CompletedWafers = Wafers.Count(w => w.IsCompleted);
            PendingWafers = Wafers.Count - CompletedWafers;

            // Update throughput (wafers per minute)
            if (elapsed.TotalSeconds > 0)
            {
                var throughputValue = (CompletedWafers / elapsed.TotalMinutes);
                Throughput = throughputValue.ToString("F1");
            }

            // Update efficiency (would need theoretical time calculation)
            Efficiency = "N/A";
        });
    }

    public void Start()
    {
        if (_isRunning) return;

        Logger.Instance.Info("Controller", "Starting simulation");
        _simulationStartTime = DateTime.Now;
        _isRunning = true;

        // Trigger carrier arrival with all wafers
        var waferIds = Wafers.Select(w => w.Id).ToList();
        _journeyScheduler?.OnCarrierArrival("C1", waferIds);

        _simulationTimer?.Start();
    }

    public void Stop()
    {
        if (!_isRunning) return;

        Logger.Instance.Info("Controller", "Stopping simulation");
        _isRunning = false;
        _simulationTimer?.Stop();
    }

    public void Reset()
    {
        Logger.Instance.Info("Controller", "Resetting simulation");

        Stop();

        // Reset schedulers
        _robotScheduler = new RobotScheduler();
        _journeyScheduler = new WaferJourneyScheduler(_robotScheduler, Wafers);

        // Re-register stations and robots
        foreach (var (name, actor) in _stationActors)
        {
            if (name.Contains("Robot"))
            {
                _robotScheduler.RegisterRobot(name, actor);
                _robotScheduler.UpdateRobotState(name, "idle");
            }
            else
            {
                _journeyScheduler.RegisterStation(name, actor);
            }
        }

        // Recreate wafers
        CreateWafers(TOTAL_WAFERS);

        // Reset metrics
        ElapsedTime = "0:00";
        Throughput = "0.0";
        Efficiency = "0%";
    }

    public void SetExecutionMode(ExecutionMode mode)
    {
        _executionMode = mode;
        Logger.Instance.Info("Controller", $"Execution mode set to: {mode}");
    }

    public Task StartSimulation()
    {
        Start();
        return Task.CompletedTask;
    }

    public Task ExecuteOneStep()
    {
        _journeyScheduler?.ProcessWaferJourneys();
        UpdateMetrics();
        return Task.CompletedTask;
    }

    public void StopSimulation()
    {
        Stop();
    }

    public void ResetSimulation()
    {
        Reset();
    }

    public void UpdateSettings(int r1Transfer, int polisher, int r2Transfer, int cleaner, int r3Transfer, int bufferHold, int loadPortReturn)
    {
        // TODO: Update timing settings for stations
        Logger.Instance.Info("Controller", $"Settings updated: R1={r1Transfer}ms, Polisher={polisher}ms, R2={r2Transfer}ms, Cleaner={cleaner}ms");
    }

    public CarrierManager GetCarrierManager()
    {
        return _carrierManager;
    }

    public void Dispose()
    {
        Logger.Instance.Info("Controller", "Disposing controller");
        Stop();
        _simulationTimer = null;
        _actorSystem?.Terminate().Wait();
        _actorSystem?.Dispose();
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
