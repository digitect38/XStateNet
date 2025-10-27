using Akka.Actor;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using CMPSimXS2.StateMachines;
using CMPSimXS2.Helpers;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;

namespace CMPSimXS2.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ActorSystem? _actorSystem;
    private XStateMachineFactory? _factory;
    private readonly DispatcherTimer _updateTimer;

    // Hierarchical Schedulers
    private RobotScheduler? _robotScheduler;
    private WaferJourneyScheduler? _waferJourneyScheduler;

    private bool _isRunning;
    private int _waferCount = 25;
    private int _processedWafers;
    private string _status = "Ready";
    private string? _currentCarrierId;
    private int _totalCarriers = 2; // Support 2 carriers (C1: 1-13, C2: 14-25)

    public ObservableCollection<StationViewModel> Stations { get; }
    public ObservableCollection<StationViewModel> Robots { get; }
    public ObservableCollection<Wafer> Wafers { get; }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public int WaferCount
    {
        get => _waferCount;
        set => SetProperty(ref _waferCount, value);
    }

    public int ProcessedWafers
    {
        get => _processedWafers;
        set => SetProperty(ref _processedWafers, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string? CurrentCarrierId
    {
        get => _currentCarrierId;
        set => SetProperty(ref _currentCarrierId, value);
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ProcessWaferCommand { get; }

    public MainViewModel()
    {
        Stations = new ObservableCollection<StationViewModel>();
        Robots = new ObservableCollection<StationViewModel>();
        Wafers = new ObservableCollection<Wafer>();

        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        ResetCommand = new RelayCommand(Reset);
        ProcessWaferCommand = new RelayCommand(ProcessSingleWafer, () => IsRunning);

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        // Log application startup
        Logger.Instance.Info("Application", "=== CMPSimXS2 Application Started ===");
        Logger.Instance.Info("Application", $"Log file location: {Logger.Instance.GetLogFilePath()}");
        Logger.Instance.Info("Application", $"Version: 1.0.0");
        Logger.Instance.Info("Application", $"Start time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        InitializeActorSystem();
        InitializeStations();
        InitializeWafers();
        InitializeSchedulers();
    }

    private void InitializeActorSystem()
    {
        Logger.Instance.Info("MainViewModel", "Initializing Actor System...");
        _actorSystem = ActorSystem.Create("CMPSimulator");
        _factory = new XStateMachineFactory(_actorSystem);
        Status = "Actor system initialized";
        Logger.Instance.Info("MainViewModel", "Actor System initialized successfully");
    }

    private void InitializeStations()
    {
        if (_factory == null) return;

        Logger.Instance.Info("MainViewModel", "=== Initializing Stations ===");

        // Create LoadPort
        Logger.Instance.Info("LoadPort", "Creating LoadPort state machine...");
        var loadPort = new StationViewModel("LoadPort")
        {
            X = 250,
            Y = 30
        };
        loadPort.StateMachine = _factory.FromJson(MachineDefinitions.GetLoadPortMachine())
            .WithAction("storeCarrier", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("carrier"))
                {
                    ctx.Set("carrier", data["carrier"]);
                    Logger.Instance.Info("LoadPort", $"Carrier stored: {data["carrier"]}");
                }
            })
            .WithAction("pickupWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("currentWafer", data["wafer"]);
                    Logger.Instance.Info("LoadPort", $"Wafer picked up: {data["wafer"]}");
                }
            })
            .WithAction("clearCarrier", (ctx, _) =>
            {
                ctx.Set("carrier", null);
                Logger.Instance.Info("LoadPort", "Carrier cleared");
            })
            .BuildAndStart();
        Stations.Add(loadPort);
        Logger.Instance.Info("LoadPort", "LoadPort initialized - State: empty");

        // Create Carrier
        Logger.Instance.Info("Carrier", "Creating Carrier state machine...");
        var carrier = new StationViewModel("Carrier")
        {
            X = 420,
            Y = 30
        };
        carrier.StateMachine = _factory.FromJson(MachineDefinitions.GetCarrierMachine())
            .WithAction("removeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("currentWafer", data["wafer"]);
                    Logger.Instance.Info("Carrier", $"Wafer removed: {data["wafer"]}");
                }
            })
            .WithAction("addWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("currentWafer", data["wafer"]);
                    Logger.Instance.Info("Carrier", $"Wafer added: {data["wafer"]}");
                }
            })
            .BuildAndStart();
        Stations.Add(carrier);
        Logger.Instance.Info("Carrier", "Carrier initialized - State: loaded");

        // Create Polisher
        Logger.Instance.Info("Polisher", "Creating Polisher state machine...");
        var polisher = new StationViewModel("Polisher")
        {
            X = 590,
            Y = 30
        };
        polisher.StateMachine = _factory.FromJson(MachineDefinitions.GetPolisherMachine())
            .WithDelayService("PROCESS_DELAY", 2000) // 2 seconds for demo
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("currentWafer", data["wafer"]);
                    Logger.Instance.Info("Polisher", $"Wafer loaded: {data["wafer"]}");
                }
            })
            .WithAction("startProcessing", (ctx, _) =>
            {
                var wafer = ctx.Get<object>("currentWafer");
                Logger.Instance.Info("Polisher", $"Started processing wafer: {wafer}");
            })
            .WithAction("completeProcessing", (ctx, _) =>
            {
                var wafer = ctx.Get<object>("currentWafer");
                Logger.Instance.Info("Polisher", $"Completed processing wafer: {wafer}");
            })
            .WithAction("clearWafer", (ctx, _) =>
            {
                var wafer = ctx.Get<object>("currentWafer");
                ctx.Set("currentWafer", null);
                Logger.Instance.Info("Polisher", $"Wafer unloaded: {wafer}");
            })
            .BuildAndStart();
        Stations.Add(polisher);
        Logger.Instance.Info("Polisher", "Polisher initialized - State: idle");

        // Create Cleaner
        Logger.Instance.Info("Cleaner", "Creating Cleaner state machine...");
        var cleaner = new StationViewModel("Cleaner")
        {
            X = 760,
            Y = 30
        };
        cleaner.StateMachine = _factory.FromJson(MachineDefinitions.GetCleanerMachine())
            .WithDelayService("CLEAN_DELAY", 1000) // 1 second for demo
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("currentWafer", data["wafer"]);
                    Logger.Instance.Info("Cleaner", $"Wafer loaded: {data["wafer"]}");
                }
            })
            .WithAction("startCleaning", (ctx, _) =>
            {
                var wafer = ctx.Get<object>("currentWafer");
                Logger.Instance.Info("Cleaner", $"Started cleaning wafer: {wafer}");
            })
            .WithAction("completeCleaning", (ctx, _) =>
            {
                var wafer = ctx.Get<object>("currentWafer");
                Logger.Instance.Info("Cleaner", $"Completed cleaning wafer: {wafer}");
            })
            .WithAction("clearWafer", (ctx, _) =>
            {
                var wafer = ctx.Get<object>("currentWafer");
                ctx.Set("currentWafer", null);
                Logger.Instance.Info("Cleaner", $"Wafer unloaded: {wafer}");
            })
            .BuildAndStart();
        Stations.Add(cleaner);
        Logger.Instance.Info("Cleaner", "Cleaner initialized - State: idle");

        // Create Buffer
        Logger.Instance.Info("Buffer", "Creating Buffer state machine...");
        var buffer = new StationViewModel("Buffer")
        {
            X = 930,
            Y = 30
        };
        buffer.StateMachine = _factory.FromJson(MachineDefinitions.GetBufferMachine())
            .WithAction("storeWafer", (ctx, evt) =>
            {
                var data = evt as Dictionary<string, object>;
                if (data != null && data.ContainsKey("wafer"))
                {
                    ctx.Set("currentWafer", data["wafer"]);
                    Logger.Instance.Info("Buffer", $"Wafer stored: {data["wafer"]}");
                }
            })
            .WithAction("clearWafer", (ctx, _) =>
            {
                var wafer = ctx.Get<object>("currentWafer");
                ctx.Set("currentWafer", null);
                Logger.Instance.Info("Buffer", $"Wafer retrieved: {wafer}");
            })
            .BuildAndStart();
        Stations.Add(buffer);
        Logger.Instance.Info("Buffer", "Buffer initialized - State: empty");

        // Create Robots
        Logger.Instance.Info("MainViewModel", "Creating 3 Robot state machines...");
        for (int i = 1; i <= 3; i++)
        {
            var robotId = $"Robot {i}";
            Logger.Instance.Info(robotId, $"Creating {robotId} state machine...");
            var robot = new StationViewModel(robotId)
            {
                X = 350 + (i - 1) * 170, // Position robots horizontally: 350, 520, 690
                Y = 300
            };
            var capturedId = robotId; // Capture for closure
            robot.StateMachine = _factory.FromJson(MachineDefinitions.GetRobotMachine())
                .WithAction("pickupWafer", (ctx, evt) =>
                {
                    var data = evt as Dictionary<string, object>;
                    if (data != null && data.ContainsKey("wafer"))
                    {
                        ctx.Set("currentWafer", data["wafer"]);
                        Logger.Instance.Info(capturedId, $"Picked up wafer: {data["wafer"]} from {data.GetValueOrDefault("from", "?")} to {data.GetValueOrDefault("to", "?")}");
                    }
                })
                .WithAction("placeWafer", (ctx, _) =>
                {
                    var wafer = ctx.Get<object>("currentWafer");
                    Logger.Instance.Info(capturedId, $"Placed wafer: {wafer}");
                })
                .WithAction("clearWafer", (ctx, _) =>
                {
                    ctx.Set("currentWafer", null);
                    Logger.Instance.Debug(capturedId, "Wafer cleared from robot");
                })
                .BuildAndStart($"robot{i}");
            Robots.Add(robot);
            Logger.Instance.Info(robotId, $"{robotId} initialized - State: idle");
        }

        Logger.Instance.Info("MainViewModel", "=== All Stations Initialized Successfully ===");
        Status = "Stations initialized";
    }

    private void InitializeWafers()
    {
        Logger.Instance.Info("MainViewModel", "=== Initializing Wafers ===");

        // Create 25 wafers (5x5 grid)
        Wafers.Clear();
        for (int i = 1; i <= 25; i++)
        {
            var wafer = new Wafer(i)
            {
                CurrentStation = "Carrier",
                ProcessingState = "NotProcessed"
            };
            Wafers.Add(wafer);
        }

        Logger.Instance.Info("MainViewModel", $"Created {Wafers.Count} wafers in Carrier");
        Logger.Instance.Info("MainViewModel", "=== Wafers Initialized Successfully ===");
    }

    private void InitializeSchedulers()
    {
        Logger.Instance.Info("MainViewModel", "=== Initializing Hierarchical Schedulers ===");

        // Create Robot Scheduler
        _robotScheduler = new RobotScheduler();

        // Register robots with scheduler
        foreach (var robot in Robots)
        {
            if (robot.StateMachine != null)
            {
                _robotScheduler.RegisterRobot(robot.Name, robot.StateMachine);
            }
        }

        // Create Wafer Journey Scheduler (Master Scheduler)
        _waferJourneyScheduler = new WaferJourneyScheduler(_robotScheduler, Wafers);

        // Subscribe to carrier completion events
        _waferJourneyScheduler.OnCarrierCompleted += OnCarrierCompletedHandler;

        // Register stations with journey scheduler
        foreach (var station in Stations)
        {
            _waferJourneyScheduler.RegisterStation(station.Name, station);
        }

        Logger.Instance.Info("MainViewModel", "=== Schedulers Initialized Successfully ===");
        Logger.Instance.Info("MainViewModel", "Architecture: WaferJourneyScheduler → RobotScheduler → 3 Robots");
    }

    public void Start()
    {
        Logger.Instance.Info("MainViewModel", "=== SIMULATION STARTED ===");
        IsRunning = true;
        ProcessedWafers = 0;
        _updateTimer.Start();

        // Start two-carrier processing by triggering first carrier arrival
        // C1: wafers 1-13 (13 wafers)
        var c1Wafers = Enumerable.Range(1, 13).ToList();
        OnCarrierArrival("C1", c1Wafers);

        Logger.Instance.Info("MainViewModel", $"Starting two-carrier processing - Total: {WaferCount} wafers");
        Logger.Instance.Info("MainViewModel", $"Carrier C1: 13 wafers (1-13), Carrier C2: 12 wafers (14-25)");
    }

    public void Stop()
    {
        Logger.Instance.Info("MainViewModel", "=== SIMULATION STOPPED ===");
        IsRunning = false;
        _updateTimer.Stop();
        Status = "Stopped";
        Logger.Instance.Info("MainViewModel", $"Wafers processed: {ProcessedWafers}/{WaferCount}");
    }

    public void Reset()
    {
        Logger.Instance.Info("MainViewModel", "=== RESET INITIATED ===");
        Stop();
        ProcessedWafers = 0;

        // Reset schedulers
        _waferJourneyScheduler?.Reset();

        // Clear existing stations and robots
        Logger.Instance.Info("MainViewModel", "Clearing stations and robots...");
        Stations.Clear();
        Robots.Clear();

        // Terminate and recreate actor system to avoid duplicate actor names
        Logger.Instance.Info("MainViewModel", "Terminating actor system...");
        _actorSystem?.Terminate().Wait(TimeSpan.FromSeconds(3));
        Logger.Instance.Info("MainViewModel", "Actor system terminated");

        // Reinitialize actor system, stations, wafers, and schedulers
        InitializeActorSystem();
        InitializeStations();
        InitializeWafers();
        InitializeSchedulers();

        Status = "Reset complete";
        Logger.Instance.Info("MainViewModel", "=== RESET COMPLETE ===");
    }

    private void ProcessSingleWafer()
    {
        if (ProcessedWafers >= WaferCount) return;

        var waferId = ProcessedWafers + 1;

        Logger.Instance.Info("MainViewModel", $">>> Processing Wafer #{waferId}");

        // Update wafer state
        var wafer = Wafers.FirstOrDefault(w => w.Id == waferId);
        if (wafer != null)
        {
            wafer.CurrentStation = "Polisher";
        }

        // Send wafer to polisher
        var polisher = Stations.FirstOrDefault(s => s.Name == "Polisher");
        if (polisher?.StateMachine != null)
        {
            Logger.Instance.Info("MainViewModel", $"Sending LOAD_WAFER event to Polisher for wafer #{waferId}");
            polisher.StateMachine.Tell(new SendEvent("LOAD_WAFER", new Dictionary<string, object>
            {
                ["wafer"] = waferId
            }));
            Status = $"Processing wafer {waferId}";
        }
        else
        {
            Logger.Instance.Error("MainViewModel", "Polisher not found or not initialized!");
        }

        ProcessedWafers++;
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        // Update all station states
        foreach (var station in Stations)
        {
            station.UpdateState();
        }

        foreach (var robot in Robots)
        {
            robot.UpdateState();

            // Update robot scheduler with current robot state
            if (_robotScheduler != null)
            {
                // Map state to scheduler format
                string schedulerState = robot.CurrentState switch
                {
                    "idle" => "idle",
                    "carrying" => "busy",
                    "moving" => "busy",
                    _ => "busy"
                };

                _robotScheduler.UpdateRobotState(robot.Name, schedulerState, robot.CurrentWafer);
            }
        }

        if (!IsRunning) return;

        // Use hierarchical schedulers to process wafer journeys
        _waferJourneyScheduler?.ProcessWaferJourneys();

        // Update processed wafer count
        int completedCount = Wafers.Count(w => w.IsCompleted);
        if (completedCount != ProcessedWafers)
        {
            ProcessedWafers = completedCount;
            Status = $"Processed: {ProcessedWafers}/{WaferCount}";

            if (ProcessedWafers >= WaferCount)
            {
                Logger.Instance.Info("MainViewModel", $"=== ALL {WaferCount} WAFERS COMPLETED! ===");
                Stop();
            }
        }
    }

    public void Shutdown()
    {
        Logger.Instance.Info("Application", "=== Shutting Down Application ===");
        Logger.Instance.Info("Application", $"Shutdown time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Logger.Instance.Info("Application", $"Total wafers processed: {ProcessedWafers}/{WaferCount}");

        _updateTimer.Stop();
        Logger.Instance.Info("Application", "Terminating actor system...");
        _actorSystem?.Terminate().Wait(TimeSpan.FromSeconds(5));
        Logger.Instance.Info("Application", "Actor system terminated");
        Logger.Instance.Info("Application", "=== Application Shutdown Complete ===");
    }

    #region Two-Carrier Lifecycle Management

    /// <summary>
    /// Handle carrier arrival event - registers carrier and its wafers with scheduler
    /// </summary>
    private void OnCarrierArrival(string carrierId, List<int> waferIds)
    {
        CurrentCarrierId = carrierId;
        _waferJourneyScheduler?.OnCarrierArrival(carrierId, waferIds);

        Status = $"Carrier {carrierId} arrived ({waferIds.Count} wafers)";
        Logger.Instance.Info("MainViewModel", $"🚛 Carrier {carrierId} arrived with wafers: {string.Join(", ", waferIds)}");
    }

    /// <summary>
    /// Handle carrier completion event - triggers departure if all wafers processed
    /// </summary>
    private void OnCarrierCompletedHandler(string carrierId)
    {
        Logger.Instance.Info("MainViewModel", $"✅ Carrier {carrierId} completed - All wafers processed!");

        // Trigger carrier departure
        OnCarrierDeparture(carrierId);

        // Check if we need to start next carrier
        StartNextCarrierIfNeeded();
    }

    /// <summary>
    /// Handle carrier departure event - marks carrier as departed and clears current carrier
    /// </summary>
    private void OnCarrierDeparture(string carrierId)
    {
        _waferJourneyScheduler?.OnCarrierDeparture(carrierId);

        if (CurrentCarrierId == carrierId)
        {
            CurrentCarrierId = null;
        }

        Status = $"Carrier {carrierId} departed";
        Logger.Instance.Info("MainViewModel", $"🚚 Carrier {carrierId} departed");
    }

    /// <summary>
    /// Start next carrier if there are more carriers to process
    /// </summary>
    private void StartNextCarrierIfNeeded()
    {
        // For 25 wafers with 2 carriers:
        // C1: wafers 1-13 (13 wafers)
        // C2: wafers 14-25 (12 wafers)

        if (CurrentCarrierId == "C1")
        {
            // Start second carrier
            var c2Wafers = Enumerable.Range(14, 12).ToList(); // 14-25
            OnCarrierArrival("C2", c2Wafers);
        }
        else if (CurrentCarrierId == "C2")
        {
            // All carriers completed
            Logger.Instance.Info("MainViewModel", "🎉 All carriers completed!");
        }
    }

    #endregion
}

// Simple RelayCommand implementation
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}
