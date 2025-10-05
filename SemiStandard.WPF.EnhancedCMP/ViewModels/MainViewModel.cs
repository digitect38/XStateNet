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
    private int _jobsToProcess = 12;

    public MainViewModel()
    {
        Tools = new ObservableCollection<ToolViewModel>
        {
            new() { ToolId = "CMP_TOOL_1" },
            new() { ToolId = "CMP_TOOL_2" },
            new() { ToolId = "CMP_TOOL_3" }
        };

        DataCollectionReports = new ObservableCollection<string>();
        EventLog = new ObservableCollection<string>();

        StartCommand = new RelayCommand(async () => await StartSimulation(), () => !IsRunning);
        StopCommand = new RelayCommand(StopSimulation, () => IsRunning);
        SendJobCommand = new RelayCommand(async () => await SendJob(), () => IsRunning);
    }

    public ObservableCollection<ToolViewModel> Tools { get; }
    public ObservableCollection<string> DataCollectionReports { get; }
    public ObservableCollection<string> EventLog { get; }

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
            _masterScheduler = new EnhancedCMPMasterScheduler("001", _orchestrator, maxWip: 3);
            await _masterScheduler.StartAsync();
            AddLog("   ✅ E40 Process Job management active");
            AddLog("   ✅ E134 Data Collection plans configured");
            AddLog("   ✅ E39 Equipment Metrics defined");

            // Create tool schedulers
            AddLog("🔧 Creating Enhanced CMP Tool Schedulers...");
            _toolSchedulers.Clear();
            for (int i = 0; i < 3; i++)
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
            AddLog("📝 Registering tools with master scheduler...");
            _masterScheduler.RegisterTool("CMP_TOOL_1", "CMP", new Dictionary<string, object>
            {
                ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_OXIDE_01" }
            });
            _masterScheduler.RegisterTool("CMP_TOOL_2", "CMP", new Dictionary<string, object>
            {
                ["recipes"] = new[] { "CMP_STANDARD_01", "CMP_METAL_01" }
            });
            _masterScheduler.RegisterTool("CMP_TOOL_3", "CMP", new Dictionary<string, object>
            {
                ["recipes"] = new[] { "CMP_STANDARD_01" }
            });

            IsRunning = true;
            AddLog("✅ System Initialized - Ready to Process Wafers");

            // Start update timer
            _updateTimer = new System.Threading.Timer(_ => UpdateStatus(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));

            // Auto-start processing jobs
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                for (int i = 0; i < JobsToProcess; i++)
                {
                    await SendJob();
                    await Task.Delay(1500);
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
                    tool.RefreshStatusColor();
                }
            }
            catch
            {
                // Ignore errors during update
            }
        });
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
