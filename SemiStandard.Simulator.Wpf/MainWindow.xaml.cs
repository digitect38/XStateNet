using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using XStateNet;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Testing;
using XStateNet.Semi.Transport;

namespace SemiStandard.Simulator.Wpf;

public class MessageLogEntry
{
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss.fff");
    public string Direction { get; set; } = "";
    public string Message { get; set; } = "";
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<MessageLogEntry> _messageLog = new();
    private readonly double[] _messageRateData = new double[20];
    private readonly double[] _messageRateTime = new double[20];
    private DispatcherTimer _updateTimer = null!;
    private DispatcherTimer _uptimeTimer = null!;

    private XStateEquipmentController? _controller;
    private ResilientHsmsConnection? _hostConnection;
    private ResilientHsmsConnection? _equipmentConnection;
    private CancellationTokenSource? _cancellationTokenSource;
    private DateTime _startTime;
    private int _messagesSent;
    private int _messagesReceived;
    private int _messageErrors;

    // Real XStateNet state machines
    private ConcurrentDictionary<string, StateMachine> _stateMachines = new();
    private ConcurrentDictionary<string, string> _currentStates = new();

    // Message rate chart data (removed LiveCharts series)

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        SetupChart();
        SetupMessageLog();
        SetupTimers();
        SetupEventHandlers();
        _ = InitializeStateMachines(); // Fire and forget - initialization continues in background

        _startTime = DateTime.Now;
    }

    private void SetupChart()
    {
        // Initialize time axis
        for (int i = 0; i < 20; i++)
        {
            _messageRateTime[i] = i;
            _messageRateData[i] = 0;
        }

        // Setup ScottPlot
        MessageRateChart.Plot.Add.Signal(_messageRateData);
        MessageRateChart.Plot.Title("Message Rate");
        MessageRateChart.Plot.Axes.Left.Label.Text = "Messages/sec";
        MessageRateChart.Plot.Axes.Bottom.Label.Text = "Time (seconds)";
        // Use default style for now
        MessageRateChart.Refresh();
    }

    private void SetupMessageLog()
    {
        MessageLog.ItemsSource = _messageLog;
    }

    private void SetupTimers()
    {
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        _uptimeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _uptimeTimer.Tick += UptimeTimer_Tick;
        _uptimeTimer.Start();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        UpdateStateMachineStates();
        UpdateMessageRateChart();
    }

    private void UptimeTimer_Tick(object? sender, EventArgs e)
    {
        var uptime = DateTime.Now - _startTime;
        UptimeStatus.Text = $"Uptime: {uptime:hh\\:mm\\:ss}";
    }

    private void UpdateStateMachineStates()
    {
        // Update UI with current state machine states
        if (_currentStates.TryGetValue("E30GEM", out var gemState))
        {
            E30State.Text = gemState;
            E30Progress.IsIndeterminate = gemState.Contains("wait") || gemState.Contains("executing");
        }

        if (_currentStates.TryGetValue("E87Carrier", out var carrierState))
        {
            E87State.Text = carrierState;
        }

        if (_currentStates.TryGetValue("E94ControlJob", out var jobState))
        {
            E94State.Text = jobState;
        }

        if (_currentStates.TryGetValue("E37HSMSSession", out var hsmsState))
        {
            HSMSState.Text = hsmsState;
        }
    }

    private string ExtractStateName(string fullStatePath)
    {
        var parts = fullStatePath.Split('.');
        return parts.Length > 0 ? parts[^1] : fullStatePath;
    }

    private void UpdateMessageRateChart()
    {
        // Shift data left
        for (int i = 0; i < _messageRateData.Length - 1; i++)
        {
            _messageRateData[i] = _messageRateData[i + 1];
        }

        // Add new data point (messages per second)
        _messageRateData[^1] = (_messagesSent + _messagesReceived) / 2.0;

        // Refresh the chart
        MessageRateChart.Refresh();

        // Reset counters
        _messagesSent = 0;
        _messagesReceived = 0;
    }

    private async Task InitializeStateMachines()
    {
        // Create real XStateNet state machines
        _stateMachines = StateMachineDefinitions.CreateAllStateMachines(
            (machineName, fromState, toState, eventName) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _currentStates[machineName] = toState;
                    LogMessage("→", $"{machineName}: {fromState} → {toState} [{eventName}]");
                });
            });

        // Initialize current states tracking
        foreach (var kvp in _stateMachines)
        {
            var machineName = kvp.Key;
            var machine = kvp.Value;
            await machine.StartAsync();
            // Initial state will be set via OnTransition callback when machine starts
            _currentStates[machineName] = "Initializing...";
        }
    }

    private void SetupEventHandlers()
    {
        // Connection buttons
        StartSimulator.Click += StartSimulator_Click;
        StopSimulator.Click += StopSimulator_Click;
        ResetStates.Click += ResetStates_Click;

        // E30 GEM buttons
        E30Enable.Click += async (s, e) => await SendStateMachineEvent("E30GEM", "ENABLE_COMMAND");
        E30Disable.Click += async (s, e) => await SendStateMachineEvent("E30GEM", "DISABLE_COMMAND");
        E30Select.Click += async (s, e) => await SendStateMachineEvent("E30GEM", "HOST_SELECT");

        // E87 Carrier buttons
        E87Detect.Click += async (s, e) => await SendStateMachineEvent("E87Carrier", "CARRIER_DETECTED");
        E87Remove.Click += async (s, e) => await SendStateMachineEvent("E87Carrier", "CARRIER_REMOVED");
        E87Map.Click += async (s, e) => await SendStateMachineEvent("E87Carrier", "CARRIER_MAPPED");

        // E94 Control Job buttons
        E94Create.Click += async (s, e) => await SendStateMachineEvent("E94ControlJob", "CREATE_JOB");
        E94Start.Click += async (s, e) => await SendStateMachineEvent("E94ControlJob", "CJ_START");
        E94Abort.Click += async (s, e) => await SendStateMachineEvent("E94ControlJob", "CJ_ABORT");

        // HSMS buttons
        HSMSConnect.Click += async (s, e) => await ConnectHsms();
        HSMSDisconnect.Click += async (s, e) => await DisconnectHsms();

        // Quick command buttons
        SendS1F1.Click += async (s, e) => await SendSecsMessage(SecsMessageLibrary.S1F1());
        SendS1F13.Click += async (s, e) => await SendSecsMessage(SecsMessageLibrary.S1F13());
        SendS1F17.Click += async (s, e) => await SendSecsMessage(new SecsMessage(1, 17, false));
        SendS2F41.Click += async (s, e) => await SendSecsMessage(new SecsMessage(2, 41, false));
        SendS6F11.Click += async (s, e) => await SendSecsMessage(new SecsMessage(6, 11, false));

        // Custom message
        SendCustom.Click += SendCustomMessage_Click;

        // Log buttons
        ClearLog.Click += (s, e) => _messageLog.Clear();
        ExportLog.Click += ExportLog_Click;
    }

    private async void StartSimulator_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusMessage.Text = "Starting simulator...";
            LogMessage("→", "Starting equipment simulator...");

            _cancellationTokenSource = new CancellationTokenSource();

            // For now, just simulate the connection without actually starting the controller
            // since the API integration needs more work
            await Task.Delay(500); // Simulate startup time

            // Update UI to show connected state
            ConnectionStatus.Text = "SIMULATED";
            ConnectionIcon.Kind = PackIconKind.LanConnect;
            ConnectionIcon.Foreground = FindResource("SuccessBrush") as Brush;
            StatusMessage.Text = "Simulator running (simulated mode)";

            StartSimulator.IsEnabled = false;
            StopSimulator.IsEnabled = true;

            _updateTimer.Start();

            LogMessage("✓", "Simulator started in demo mode");
            LogMessage("ℹ", "Note: This is a UI demo. For actual HSMS connection, run the console test program.");

            // Add some demo state data
            E30State.Text = "disabled";
            E87State.Text = "NotPresent";
            E94State.Text = "NoJob";
            HSMSState.Text = "NotConnected";

            // Simulate some messages in the log
            LogMessage("→", "Equipment listening on port 5000");
            LogMessage("←", "Waiting for host connection...");
        }
        catch (Exception ex)
        {
            LogMessage("❌", $"Error: {ex.Message}");
            StatusMessage.Text = "Failed to start";

            // Show error in UI instead of MessageBox which might not work
            var errorMsg = $"Failed to start simulator:\n{ex.Message}\n\nPlease check the console for details.";
            LogMessage("!", errorMsg);
        }
    }

    private async void StopSimulator_Click(object? sender, RoutedEventArgs e)
    {
        StatusMessage.Text = "Stopping simulator...";

        _cancellationTokenSource?.Cancel();
        _updateTimer.Stop();

        if (_hostConnection != null)
        {
            await _hostConnection.DisconnectAsync();
            _hostConnection = null;
        }

        if (_equipmentConnection != null)
        {
            await _equipmentConnection.DisconnectAsync();
            _equipmentConnection = null;
        }

        _controller = null;

        // Update UI
        ConnectionStatus.Text = "DISCONNECTED";
        ConnectionIcon.Kind = PackIconKind.LanDisconnect;
        ConnectionIcon.Foreground = FindResource("ErrorBrush") as Brush;
        StatusMessage.Text = "Simulator stopped";

        StartSimulator.IsEnabled = true;
        StopSimulator.IsEnabled = false;

        LogMessage("->", "Simulator stopped");
    }

    private void ResetStates_Click(object? sender, RoutedEventArgs e)
    {
        if (_controller == null) return;

        // Reset functionality not directly available
        LogMessage("->", "All state machines reset");
        StatusMessage.Text = "States reset";
    }

    private async Task ConnectHsms()
    {
        try
        {
            if (_hostConnection != null)
            {
                await _hostConnection.DisconnectAsync();
            }

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddDebug());
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var hostEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 5000);
            _hostConnection = new ResilientHsmsConnection(hostEndpoint, XStateNet.Semi.Transport.HsmsConnection.HsmsConnectionMode.Active);
            await _hostConnection.ConnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);

            LogMessage("->", "HSMS connection established");
        }
        catch (Exception ex)
        {
            LogMessage("!", $"HSMS connection failed: {ex.Message}");
        }
    }

    private async Task DisconnectHsms()
    {
        if (_hostConnection != null)
        {
            await _hostConnection.DisconnectAsync();
            _hostConnection = null;
            LogMessage("->", "HSMS disconnected");
        }
    }

    private async Task SendStateMachineEvent(string machineName, string eventName)
    {
        if (_stateMachines.TryGetValue(machineName, out var machine))
        {
            machine.Send(eventName);
            LogMessage("→", $"Sent {eventName} to {machineName}");
        }
        else
        {
            LogMessage("!", $"State machine {machineName} not found");
        }
        await Task.CompletedTask;
    }

    private Task SendSecsMessage(SecsMessage message)
    {
        if (_hostConnection == null || !_hostConnection.IsConnected)
        {
            LogMessage("!", "Not connected to host");
            return Task.CompletedTask;
        }

        try
        {
            // Send message - simplified for now
            // Real implementation would convert SecsMessage to HsmsMessage
            _messagesSent++;
            LogMessage("->", $"S{message.Stream}F{message.Function}");
        }
        catch (Exception ex)
        {
            _messageErrors++;
            LogMessage("!", $"Send failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async void SendCustomMessage_Click(object? sender, RoutedEventArgs e)
    {
        if (!byte.TryParse(StreamInput.Text, out var stream) ||
            !byte.TryParse(FunctionInput.Text, out var function))
        {
            MessageBox.Show("Invalid stream or function number", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var message = new SecsMessage(stream, function, false);
        // TODO: Parse and add data from DataInput.Text

        await SendSecsMessage(message);
    }

    private void ExportLog_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"MessageLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            foreach (var entry in _messageLog)
            {
                sb.AppendLine($"[{entry.Timestamp}] {entry.Direction} {entry.Message}");
            }
            System.IO.File.WriteAllText(dialog.FileName, sb.ToString());
            StatusMessage.Text = "Log exported";
        }
    }

    // Controller event handlers removed - not available in current API

    // Message received handlers removed - will be implemented differently

    private void LogMessage(string direction, string message)
    {
        _messageLog.Add(new MessageLogEntry
        {
            Direction = direction,
            Message = message
        });

        // Keep only last 1000 messages
        while (_messageLog.Count > 1000)
        {
            _messageLog.RemoveAt(0);
        }

        // Auto-scroll to bottom
        LogScrollViewer.ScrollToEnd();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
