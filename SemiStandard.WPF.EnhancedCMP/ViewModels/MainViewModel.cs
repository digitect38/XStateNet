using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using XStateNet.Orchestration;
using XStateNet.Semi.Schedulers;

namespace SemiStandard.WPF.EnhancedCMP.ViewModels;

public class MainViewModel : ViewModelBase
{
    private EventBusOrchestrator? _orchestrator;
    private EnhancedCMPMasterScheduler? _masterScheduler;
    private readonly List<EnhancedCMPToolScheduler> _toolSchedulers = new();
    private System.Threading.Timer? _updateTimer;

    private string _masterState = "Not Started";
    private int _currentWip;
    private int _queueLength;
    private int _totalJobs;
    private double _utilization;
    private double _throughput;
    private bool _isRunning;
    private int _jobsToProcess = 7;

    public MainViewModel()
    {
        Tools = new ObservableCollection<ToolViewModel>
        {
            new() { ToolId = "CMP_TOOL_1" },
            new() { ToolId = "CMP_TOOL_2" },
            new() { ToolId = "CMP_TOOL_3" },
            new() { ToolId = "CMP_TOOL_4" }
        };

        DataCollectionReports = new ObservableCollection<string>();
        EventLog = new ObservableCollection<string>();
        WaferMatrix = new ObservableCollection<WaferStatusRow>();
        WaferJourneys = new ObservableCollection<WaferJourneyViewModel>();

        StartCommand = new RelayCommand(async () => await StartSimulation(), () => !IsRunning);
        StopCommand = new RelayCommand(StopSimulation, () => IsRunning);
        SendJobCommand = new RelayCommand(async () => await SendJob(), () => IsRunning);
    }

    public ObservableCollection<ToolViewModel> Tools { get; }
    public ObservableCollection<string> DataCollectionReports { get; }
    public ObservableCollection<string> EventLog { get; }
    public ObservableCollection<WaferStatusRow> WaferMatrix { get; }
    public ObservableCollection<WaferJourneyViewModel> WaferJourneys { get; }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SendJobCommand { get; }

    public string MasterState
    {
        get => _masterState;
        set => SetProperty(ref _masterState, value);
    }

    public int CurrentWip
    {
        get => _currentWip;
        set => SetProperty(ref _currentWip, value);
    }

    public int QueueLength
    {
        get => _queueLength;
        set => SetProperty(ref _queueLength, value);
    }

    public int TotalJobs
    {
        get => _totalJobs;
        set => SetProperty(ref _totalJobs, value);
    }

    public double Utilization
    {
        get => _utilization;
        set => SetProperty(ref _utilization, value);
    }

    public double Throughput
    {
        get => _throughput;
        set => SetProperty(ref _throughput, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StatusText));
                ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendJobCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public int JobsToProcess
    {
        get => _jobsToProcess;
        set => SetProperty(ref _jobsToProcess, value);
    }

    public string StatusText => IsRunning ? "Running" : "Stopped";

    private async Task StartSimulation()
    {
        try
        {
            AddLog("🔧 Initializing Enhanced CMP System...");

            // Create orchestrator
            var config = new OrchestratorConfig
            {
                EnableLogging = true,
                PoolSize = 8,
                EnableMetrics = true
            };

            _orchestrator = new EventBusOrchestrator(config);

            // Create master scheduler
            AddLog("📋 Creating Enhanced Master Scheduler...");
            _masterScheduler = new EnhancedCMPMasterScheduler("001", _orchestrator, maxWip: 4);
            await _masterScheduler.StartAsync();
            AddLog("   ✅ E40 Process Job management active");
            AddLog("   ✅ E134 Data Collection plans configured");
            AddLog("   ✅ E39 Equipment Metrics defined");

            // Create tool schedulers
            AddLog("🔧 Creating Enhanced CMP Tool Schedulers (4 tools)...");
            _toolSchedulers.Clear();
            for (int i = 0; i < 4; i++)
            {
                var toolId = $"CMP_TOOL_{i + 1}";
                var tool = new EnhancedCMPToolScheduler(toolId, _orchestrator);
                await tool.StartAsync();
                _toolSchedulers.Add(tool);
            }
            AddLog("   ✅ E90 Substrate Tracking ready");
            AddLog("   ✅ E134 Tool-level data collection active");
            AddLog("   ✅ E39 Tool metrics configured");

            // Register tools
            AddLog("📝 Registering 4 tools with master scheduler...");
            await _masterScheduler.RegisterToolAsync(_toolSchedulers[0].MachineId, "CMP", new Dictionary<string, object>
            {
                ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_OXIDE_01" },
                ["maxWaferSize"] = 300,
                ["chamber"] = "A"
            });
            await _masterScheduler.RegisterToolAsync(_toolSchedulers[1].MachineId, "CMP", new Dictionary<string, object>
            {
                ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_METAL_01" },
                ["maxWaferSize"] = 300,
                ["chamber"] = "A"
            });
            await _masterScheduler.RegisterToolAsync(_toolSchedulers[2].MachineId, "CMP", new Dictionary<string, object>
            {
                ["recipes"] = new[] { "CMP_STANDARD_01" },
                ["maxWaferSize"] = 300,
                ["chamber"] = "B"
            });
            await _masterScheduler.RegisterToolAsync(_toolSchedulers[3].MachineId, "CMP", new Dictionary<string, object>
            {
                ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_OXIDE_01" },
                ["maxWaferSize"] = 300,
                ["chamber"] = "B"
            });

            IsRunning = true;
            AddLog("✅ System Initialized - Ready to Process Wafers");

            // Start update timer
            _updateTimer = new System.Threading.Timer(_ => UpdateStatus(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));

            // Auto-start processing jobs with staggered timing
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                for (int i = 0; i < JobsToProcess; i++)
                {
                    await SendJob();
                    await Task.Delay(3000); // 3 second delay between wafer arrivals
                }
            });
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error: {ex.Message}");
        }
    }

    private void StopSimulation()
    {
        _updateTimer?.Dispose();
        _updateTimer = null;

        _orchestrator?.Dispose();
        _orchestrator = null;

        _masterScheduler = null;
        _toolSchedulers.Clear();

        IsRunning = false;
        AddLog("⏹️ Simulation stopped");
    }

    private async Task SendJob()
    {
        if (_orchestrator == null || _masterScheduler == null)
            return;

        var jobId = $"JOB_{DateTime.Now:HHmmssff}";
        var priority = TotalJobs % 4 == 0 ? "High" : "Normal";

        await _orchestrator.SendEventAsync("SYSTEM", _masterScheduler.MachineId, "JOB_ARRIVED", new
        {
            jobId,
            priority,
            waferId = $"W{DateTime.Now:HHmmss}",
            recipeId = "CMP_STANDARD_01"
        });

        AddLog($"📨 Job sent: {jobId} (Priority: {priority})");
    }

    private void UpdateStatus()
    {
        if (_masterScheduler == null)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                MasterState = _masterScheduler.GetCurrentState();
                CurrentWip = _masterScheduler.GetCurrentWip();
                QueueLength = _masterScheduler.GetQueueLength();
                TotalJobs = _masterScheduler.GetTotalJobsProcessed();
                Utilization = _masterScheduler.GetUtilization();
                Throughput = _masterScheduler.GetThroughput();

                // Update tools
                for (int i = 0; i < Math.Min(Tools.Count, _toolSchedulers.Count); i++)
                {
                    var tool = Tools[i];
                    var scheduler = _toolSchedulers[i];

                    tool.State = scheduler.GetCurrentState();
                    tool.WafersProcessed = scheduler.GetWafersProcessed();
                    tool.SlurryLevel = scheduler.GetSlurryLevel();
                    tool.PadWear = scheduler.GetPadWear();
                    tool.AvgCycleTime = scheduler.GetAvgCycleTime();
                    tool.HasWafer = scheduler.HasWafer();
                    tool.CurrentWaferId = scheduler.GetCurrentWaferId();
                    tool.CurrentJobId = scheduler.GetCurrentJobId();
                    tool.RefreshStatusColor();

                    // Find journey for current wafer to get stage info
                    var journey = WaferJourneys.FirstOrDefault(j => j.WaferId == tool.CurrentWaferId);
                    tool.UpdateCycles(journey?.StageDescription);
                }

                // Update wafer matrix
                UpdateWaferMatrix();

                // Update wafer journeys
                UpdateWaferJourneys();
            }
            catch
            {
                // Ignore errors during update
            }
        });
    }

    private void UpdateWaferMatrix()
    {
        // Collect all active wafers from tools
        var activeWafers = new Dictionary<string, WaferStatusRow>();

        for (int i = 0; i < _toolSchedulers.Count; i++)
        {
            var scheduler = _toolSchedulers[i];
            var waferId = scheduler.GetCurrentWaferId();

            if (!string.IsNullOrEmpty(waferId))
            {
                if (!activeWafers.ContainsKey(waferId))
                {
                    activeWafers[waferId] = new WaferStatusRow
                    {
                        WaferId = waferId,
                        CurrentLocation = $"Tool {i + 1}",
                        OverallState = scheduler.GetCurrentState().Split('.').FirstOrDefault() ?? "unknown"
                    };
                }

                var row = activeWafers[waferId];
                var mainState = scheduler.GetCurrentState().Split('.').FirstOrDefault() ?? "";
                var stateSymbol = GetStateSymbol(mainState);

                // Update the appropriate tool column
                switch (i)
                {
                    case 0: row.Tool1Status = stateSymbol; break;
                    case 1: row.Tool2Status = stateSymbol; break;
                    case 2: row.Tool3Status = stateSymbol; break;
                    case 3: row.Tool4Status = stateSymbol; break;
                    case 4: row.Tool5Status = stateSymbol; break;
                    case 5: row.Tool6Status = stateSymbol; break;
                    case 6: row.Tool7Status = stateSymbol; break;
                }
            }
        }

        // Update observable collection
        // Remove wafers that are no longer active
        for (int i = WaferMatrix.Count - 1; i >= 0; i--)
        {
            if (!activeWafers.ContainsKey(WaferMatrix[i].WaferId))
            {
                WaferMatrix.RemoveAt(i);
            }
        }

        // Add or update wafers
        foreach (var kvp in activeWafers)
        {
            var existing = WaferMatrix.FirstOrDefault(w => w.WaferId == kvp.Key);
            if (existing == null)
            {
                WaferMatrix.Add(kvp.Value);
            }
            else
            {
                existing.Tool1Status = kvp.Value.Tool1Status;
                existing.Tool2Status = kvp.Value.Tool2Status;
                existing.Tool3Status = kvp.Value.Tool3Status;
                existing.Tool4Status = kvp.Value.Tool4Status;
                existing.Tool5Status = kvp.Value.Tool5Status;
                existing.Tool6Status = kvp.Value.Tool6Status;
                existing.Tool7Status = kvp.Value.Tool7Status;
                existing.CurrentLocation = kvp.Value.CurrentLocation;
                existing.OverallState = kvp.Value.OverallState;
            }
        }
    }

    private void UpdateWaferJourneys()
    {
        // Track all active wafer journeys
        var activeWaferIds = new HashSet<string>();

        for (int i = 0; i < _toolSchedulers.Count; i++)
        {
            var scheduler = _toolSchedulers[i];
            var waferId = scheduler.GetCurrentWaferId();
            var jobId = scheduler.GetCurrentJobId();
            var state = scheduler.GetCurrentState().Split('.').FirstOrDefault() ?? "";

            if (!string.IsNullOrEmpty(waferId))
            {
                activeWaferIds.Add(waferId);

                var journey = WaferJourneys.FirstOrDefault(j => j.WaferId == waferId);
                if (journey == null)
                {
                    journey = new WaferJourneyViewModel
                    {
                        WaferId = waferId,
                        JobId = jobId ?? "",
                        StartTime = DateTime.Now,
                        CurrentStage = ProcessStage.LoadportLoading,
                        CurrentTool = $"Tool {i + 1}"
                    };
                    WaferJourneys.Add(journey);
                }

                // Update stage based on tool state
                journey.CurrentTool = $"Tool {i + 1}";
                journey.ElapsedTime = DateTime.Now - journey.StartTime;

                // Simulate progressive advancement through all stages
                var toolState = state.ToLower();
                var elapsedSeconds = journey.ElapsedTime.TotalSeconds;

                // Progress through stages based on elapsed time (extended timing for visibility)
                if (elapsedSeconds < 3)
                    journey.CurrentStage = ProcessStage.LoadportLoading;
                else if (elapsedSeconds < 6)
                    journey.CurrentStage = ProcessStage.WTR1ToPolisher;
                else if (elapsedSeconds < 12)
                    journey.CurrentStage = ProcessStage.PolisherProcessing;
                else if (elapsedSeconds < 15)
                    journey.CurrentStage = ProcessStage.WTR2ToCleaner;
                else if (elapsedSeconds < 20)
                    journey.CurrentStage = ProcessStage.CleanerProcessing;
                else if (elapsedSeconds < 23)
                    journey.CurrentStage = ProcessStage.WTR1ToLoadport;
                else if (elapsedSeconds < 26)
                    journey.CurrentStage = ProcessStage.LoadportUnloading;
                else
                    journey.CurrentStage = ProcessStage.Completed;
            }
        }

        // Remove completed wafers after longer time (keep for 30 seconds after completion)
        for (int i = WaferJourneys.Count - 1; i >= 0; i--)
        {
            var journey = WaferJourneys[i];
            if (!activeWaferIds.Contains(journey.WaferId))
            {
                if (journey.CurrentStage == ProcessStage.Completed &&
                    (DateTime.Now - journey.StartTime).TotalSeconds > 35)
                {
                    WaferJourneys.RemoveAt(i);
                }
            }
        }

        // Keep all journeys visible (no limit)
        // while (WaferJourneys.Count > 10)
        // {
        //     WaferJourneys.RemoveAt(0);
        // }
    }

    private string GetStateSymbol(string state)
    {
        return state.ToLower() switch
        {
            "idle" => "○",
            "loading" => "⬇",
            "processing" => "⚙",
            "unloading" => "⬆",
            "maintenance" => "🔧",
            "error" => "❌",
            "requestingconsumables" => "⚠",
            "reportingcomplete" => "✓",
            _ => "·"
        };
    }

    private void AddLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            EventLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            if (EventLog.Count > 100)
                EventLog.RemoveAt(EventLog.Count - 1);
        });
    }
}

public class RelayCommand : ICommand
{
    private readonly Func<Task>? _executeAsync;
    private readonly Action? _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        if (_executeAsync != null)
            await _executeAsync();
        else
            _execute?.Invoke();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
