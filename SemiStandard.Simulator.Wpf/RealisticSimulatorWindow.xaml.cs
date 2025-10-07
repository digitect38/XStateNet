using Microsoft.Extensions.Logging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using XStateNet;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Testing;
using XStateNet.Semi.Transport;

namespace SemiStandard.Simulator.Wpf
{
    public partial class RealisticSimulatorWindow : Window
    {
        private RealisticEquipmentSimulator? _simulator;
        private ResilientHsmsConnection? _hostConnection;
        private ILogger<RealisticSimulatorWindow>? _logger;
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _clockTimer;
        private DispatcherTimer? _schedulerTimer;
        private CancellationTokenSource? _cts;

        // Observable collections for UI binding
        private readonly ObservableCollection<CarrierViewModel> _carriers = new();
        private readonly ObservableCollection<EventViewModel> _events = new();
        private readonly ObservableCollection<AlarmViewModel> _alarms = new();
        private readonly ObservableCollection<TimelineEntry> _timeline = new();
        private readonly ObservableCollection<StateTransitionViewModel> _stateTransitions = new();

        // XState machine definitions
        private readonly ConcurrentDictionary<string, string> _xStateScripts = new();

        // State-time tracking
        private UmlTimingDiagramWindow? _umlTimingWindow;
        private TimelineWPF.TimelineWindow? _timelineWindow;
        private StateMachineTimelineWindow? _debugTimelineWindow;
        private readonly ConcurrentDictionary<string, ConcurrentBag<(string fromState, string toState, DateTime timestamp)>> _stateHistory = new();

        // Charts
        private PlotModel _temperaturePlotModel = null!;
        private PlotModel _pressurePlotModel = null!;
        private LineSeries _temperatureSeries = null!;
        private LineSeries _pressureSeries = null!;

        // Statistics
        private int _totalProcessed = 0;
        private int _totalFailed = 0;
        private int _messageCount = 0;
        private DateTime _startTime;
        private int _eventCounter = 0;
        private Random _random = new Random();
        private int _currentProcessingSlot = -1;
        private DateTime _processingStartTime;
        private bool _isProcessing = false;
        private ManufacturingScheduler _scheduler = new ManufacturingScheduler();
        private int _totalTransitions = 0;
        private DateTime _stateMachineStartTime = DateTime.Now;
        private DispatcherTimer? _stateLogTimer;
        private Lot? _currentLot = null;

        // Track current states for each state machine
        private readonly ConcurrentDictionary<string, string> _currentStates =
            new ConcurrentDictionary<string, string>(
                new Dictionary<string, string>
                {
                    { "EquipmentController", "OFFLINE" },
                    { "ProcessManager", "NOT_READY" },
                    { "TransportHandler", "IDLE" },
                    { "RecipeExecutor", "WAITING" }
                });

        // XStateNet state machines
        private ConcurrentDictionary<string, StateMachine> _stateMachines = new();

        // State color mapping
        private readonly ConcurrentDictionary<string, Brush> _stateColors =
            new ConcurrentDictionary<string, Brush>(
                new Dictionary<string, Brush>
                {
                    // Common states
                    { "IDLE", Brushes.LightBlue },
                    { "READY", Brushes.LimeGreen },
                    { "BUSY", Brushes.Orange },
                    { "PROCESSING", Brushes.Gold },
                    { "ERROR", Brushes.Red },
                    { "OFFLINE", Brushes.Gray },
                    { "ONLINE", Brushes.SpringGreen },
                    { "INITIALIZING", Brushes.Yellow },
                    { "SETUP", Brushes.Cyan },
                    { "EXECUTING", Brushes.Magenta },
                    { "LOADING", Brushes.SkyBlue },
                    { "UNLOADING", Brushes.LightSalmon },
                    { "WAITING", Brushes.LightGray },
                    { "MOVING", Brushes.HotPink },
                    { "STOPPED", Brushes.IndianRed },
                    { "PAUSED", Brushes.Khaki },
                    { "COMPLETED", Brushes.MediumSeaGreen },
                    { "NOT_READY", Brushes.Tomato },
                    { "TRANSFER_COMPLETE", Brushes.DarkSeaGreen },
                    { "RECIPE_DONE", Brushes.MediumPurple },
                    { "WAFER_IN", Brushes.Aquamarine },
                    { "WAFER_OUT", Brushes.PaleGreen },
                    { "PROCESS_COMPLETE", Brushes.LightGreen },
                    { "INIT_SUCCESS", Brushes.GreenYellow },
                    { "INIT_COMPLETE", Brushes.YellowGreen },
                    { "SYSTEM_READY", Brushes.Chartreuse }
                });

        // Wafer slot visualization
        private readonly List<Ellipse> _waferSlots = new();
        private double? _lastTemp = null;
        private double? _lastPressure = null;

        public RealisticSimulatorWindow()
        {
            Logger.Log("[REALISTIC] RealisticSimulatorWindow constructor");
            InitializeComponent();
            Logger.Log("[REALISTIC] Window initialized");

            // Set up timers
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _updateTimer.Tick += UpdateTimer_Tick;

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();

            // Initialize XState scripts
            InitializeXStateScripts();

            // Bind collections
            CarrierListBox.ItemsSource = _carriers;
            EventsItemsControl.ItemsSource = _events;
            AlarmsItemsControl.ItemsSource = _alarms;
            TimelineItemsControl.ItemsSource = _timeline;
            StateLogItemsControl.ItemsSource = _stateTransitions;

            // Start state monitoring
            InitializeStateMonitoring();

            // Initialize charts
            InitializeCharts();

            // Initialize wafer map
            InitializeWaferMap();

            // Set up logger
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            _logger = loggerFactory.CreateLogger<RealisticSimulatorWindow>();

            Logger.Log("[REALISTIC] Window setup complete");

            // Auto-start simulator after window loads
            Loaded += async (s, e) =>
            {
                Logger.Log("[REALISTIC] Window loaded, auto-starting simulator...");
                await Task.Delay(500); // Small delay for UI to render
                await StartSimulator();
            };
        }

        private void InitializeCharts()
        {
            // Temperature chart
            _temperaturePlotModel = new PlotModel
            {
                Title = "Temperature (°C)",
                Background = OxyColors.Transparent
            };
            var tempXAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                Title = "Time",
                IntervalType = DateTimeIntervalType.Seconds,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColors.LightGray,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColors.LightGray
            };
            _temperaturePlotModel.Axes.Add(tempXAxis);
            var tempYAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Temperature (°C)",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColors.LightGray,
                StringFormat = "F1",
                IsZoomEnabled = false,
                IsPanEnabled = false
            };
            _temperaturePlotModel.Axes.Add(tempYAxis);
            _temperatureSeries = new LineSeries
            {
                Title = "Chamber Temp",
                Color = OxyColors.Red,
                StrokeThickness = 2,
                MarkerType = MarkerType.None,
                LineStyle = LineStyle.Solid
            };
            _temperaturePlotModel.Series.Add(_temperatureSeries);
            TemperaturePlot.Model = _temperaturePlotModel;

            // Pressure chart
            _pressurePlotModel = new PlotModel
            {
                Title = "Pressure (hPa)",
                Background = OxyColors.Transparent
            };
            var pressureXAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                Title = "Time",
                IntervalType = DateTimeIntervalType.Seconds,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColors.LightGray,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColors.LightGray
            };
            _pressurePlotModel.Axes.Add(pressureXAxis);
            var pressureYAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Pressure (hPa)",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColors.LightGray,
                StringFormat = "F1",
                IsZoomEnabled = false,
                IsPanEnabled = false
            };
            _pressurePlotModel.Axes.Add(pressureYAxis);
            _pressureSeries = new LineSeries
            {
                Title = "Chamber Pressure",
                Color = OxyColors.Blue,
                StrokeThickness = 2,
                MarkerType = MarkerType.None,
                LineStyle = LineStyle.Solid
            };
            _pressurePlotModel.Series.Add(_pressureSeries);
            PressurePlot.Model = _pressurePlotModel;
        }

        private void InitializeWaferMap()
        {
            WaferSlotGrid.Children.Clear();
            _waferSlots.Clear();

            // Create 5x5 grid for 25 wafer slots
            for (int row = 0; row < 5; row++)
            {
                WaferSlotGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }
            for (int col = 0; col < 5; col++)
            {
                WaferSlotGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Create wafer slot indicators
            for (int i = 0; i < 25; i++)
            {
                var slot = new Ellipse
                {
                    Width = 30,
                    Height = 30,
                    Fill = Brushes.LightGray,
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 2,
                    Margin = new Thickness(2)
                };

                int row = i / 5;
                int col = i % 5;
                Grid.SetRow(slot, row);
                Grid.SetColumn(slot, col);

                WaferSlotGrid.Children.Add(slot);
                _waferSlots.Add(slot);

                // Add slot number
                var label = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    FontSize = 10,
                    FontWeight = System.Windows.FontWeights.Bold
                };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, col);
                WaferSlotGrid.Children.Add(label);
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_simulator == null)
            {
                await StartSimulator();
            }
            else
            {
                await StopSimulator();
            }
        }

        private async Task StartSimulator()
        {
            try
            {
                Logger.Log("[REALISTIC] Starting simulator...");
                ConnectButton.IsEnabled = false;
                ConnectButton.Content = "CONNECTING...";

                var endpoint = new IPEndPoint(IPAddress.Loopback, 5556);

                // Create and start simulator
                Logger.Log("[REALISTIC] Creating RealisticEquipmentSimulator on port 5556");
                _simulator = new RealisticEquipmentSimulator(endpoint, null);
                _simulator.MessageReceived += OnSimulatorMessageReceived;
                _simulator.MessageSent += OnSimulatorMessageSent;
                Logger.Log("[REALISTIC] Simulator created");

                _cts = new CancellationTokenSource();
                Logger.Log("[REALISTIC] Starting simulator async task");
                _ = Task.Run(() => _simulator.StartAsync(_cts.Token));

                await Task.Delay(1000);
                Logger.Log("[REALISTIC] Simulator started");

                // Connect host
                Logger.Log("[REALISTIC] Creating host connection");
                _hostConnection = new ResilientHsmsConnection(
                    endpoint,
                    HsmsConnection.HsmsConnectionMode.Active,
                    null);

                _hostConnection.MessageReceived += OnHostMessageReceived;
                Logger.Log("[REALISTIC] Connecting to host...");
                await _hostConnection.ConnectAsync();
                Logger.Log("[REALISTIC] Host connected");

                // Wait for selection
                await Task.Delay(1000);

                // Establish communication
                Logger.Log("[REALISTIC] Sending S1F13 (Establish Communications Request)");
                var s1f13 = SecsMessageLibrary.S1F13();
                await SendHostMessage(s1f13);
                Logger.Log("[REALISTIC] Communications established");

                _startTime = DateTime.Now;
                _updateTimer.Start();

                ConnectButton.Content = "DISCONNECT";
                ConnectButton.IsEnabled = true;
                ConnectionStatusText.Text = "Connected";
                EquipmentStateText.Text = "LOCAL";

                Logger.Log("[REALISTIC] System fully initialized and ready");
                AddTimelineEntry("System connected and initialized");

                // Generate some test events
                GenerateTestEvents();

                // Generate test alarms
                GenerateTestAlarms();

                // Initialize chart with some data points
                InitializeChartData();

                // Initialize carriers and recipes
                InitializeCarriersAndRecipes();

                // Initialize scheduler with test lots
                InitializeScheduler();

                // Start scheduler update timer
                _schedulerTimer = new DispatcherTimer();
                _schedulerTimer.Interval = TimeSpan.FromSeconds(5);
                _schedulerTimer.Tick += (s, e) => UpdateSchedulerDisplay();
                _schedulerTimer.Start();

                // Initialize wafer display but don't start processing
                InitializeWaferDisplay();

                // Enable controls
                EnableControls(true);
            }
            catch (Exception ex)
            {
                Logger.Log($"[REALISTIC] ERROR: Failed to start simulator - {ex.Message}");
                _logger?.LogError(ex, "Failed to start simulator");
                MessageBox.Show($"Failed to start simulator: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectButton.Content = "CONNECT";
                ConnectButton.IsEnabled = true;
            }
        }

        private async Task StopSimulator()
        {
            try
            {
                ConnectButton.IsEnabled = false;
                _updateTimer.Stop();

                if (_hostConnection != null)
                {
                    await _hostConnection.DisconnectAsync();
                    _hostConnection.Dispose();
                    _hostConnection = null;
                }

                if (_simulator != null)
                {
                    _cts?.Cancel();
                    await _simulator.StopAsync();
                    _simulator.Dispose();
                    _simulator = null;
                }

                ConnectionStatusText.Text = "Disconnected";
                EquipmentStateText.Text = "OFFLINE";
                ProcessStateText.Text = "IDLE";
                ConnectButton.Content = "CONNECT";
                ConnectButton.IsEnabled = true;

                EnableControls(false);
                AddTimelineEntry("System disconnected");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping simulator");
            }
        }

        private void EnableControls(bool enabled)
        {
            InitButton.IsEnabled = enabled;
            RemoteButton.IsEnabled = enabled;
            StartButton.IsEnabled = enabled;
            StopButton.IsEnabled = enabled;
            PauseButton.IsEnabled = enabled;
            ResumeButton.IsEnabled = enabled;
            LoadCarrierButton.IsEnabled = enabled;
            UnloadCarrierButton.IsEnabled = enabled;
            MapButton.IsEnabled = enabled;
            LoadRecipeButton.IsEnabled = enabled;
        }

        private async void InitButton_Click(object sender, RoutedEventArgs e)
        {
            await SendHostCommand("INIT");
            AddTimelineEntry("Equipment initialization started");
        }

        private async void RemoteButton_Click(object sender, RoutedEventArgs e)
        {
            await SendHostCommand("REMOTE");
            EquipmentStateText.Text = "REMOTE";
            AddTimelineEntry("Equipment set to REMOTE mode");
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await SendHostCommand("START");

            // Get next lot from scheduler
            var nextLot = _scheduler.GetNextLot();
            if (nextLot != null)
            {
                Logger.Log($"[SCHEDULER] Starting lot {nextLot.LotId} with priority {nextLot.Priority}");
                AddTimelineEntry($"Processing lot {nextLot.LotId} (Priority: {nextLot.Priority})");

                // Update lot state
                _scheduler.UpdateLotState(nextLot.LotId, LotState.Processing);

                // Update UI with lot info
                CurrentWaferIdText.Text = $"Lot: {nextLot.LotId}";

                // Add event for lot start
                var lotStartEvent = new EventViewModel
                {
                    EventId = 9002,
                    EventName = "Lot Processing Started",
                    Data = $"Lot {nextLot.LotId} - Recipe: {nextLot.RecipeId} - Priority: {nextLot.Priority}",
                    Background = nextLot.Priority >= LotPriority.HotLot ?
                        new SolidColorBrush(Color.FromArgb(40, 255, 100, 0)) :
                        new SolidColorBrush(Color.FromArgb(20, 0, 255, 0))
                };
                _events.Insert(0, lotStartEvent);
                if (_events.Count > 50) _events.RemoveAt(_events.Count - 1);
            }

            // Start actual wafer processing if not already running
            if (!_isProcessing)
            {
                StartWaferProcessingSimulation();
            }

            ProcessStateText.Text = "PROCESSING";
            AddTimelineEntry("Processing started");

            // Add start event
            var startEvent = new EventViewModel
            {
                EventId = 9001,
                EventName = "Processing Started",
                Data = "Wafer processing initiated by operator",
                Background = new SolidColorBrush(Color.FromArgb(20, 0, 255, 0))
            };
            _events.Insert(0, startEvent);
            if (_events.Count > 50) _events.RemoveAt(_events.Count - 1);

            // Add synchronized state transition
            if (_currentStates["ProcessManager"] == "IDLE")
            {
                AddStateTransition("ProcessManager", "IDLE", "SETUP", "START_PROCESS", "isLotReady", "Processing started");
            }
            if (_currentStates["EquipmentController"] == "ONLINE")
            {
                AddStateTransition("EquipmentController", "ONLINE", "PROCESSING", "PROCESS_START", "", "Operator initiated");
            }

            // Update scheduler display
            UpdateSchedulerDisplay();
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await SendHostCommand("STOP");

            // Stop wafer processing
            _isProcessing = false;

            ProcessStateText.Text = "IDLE";
            CurrentWaferStatusText.Text = "Status: Stopped";
            AddTimelineEntry("Processing stopped");

            // Add stop event
            var stopEvent = new EventViewModel
            {
                EventId = 9002,
                EventName = "Processing Stopped",
                Data = "Wafer processing stopped by operator",
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 0, 0))
            };
            _events.Insert(0, stopEvent);
            if (_events.Count > 50) _events.RemoveAt(_events.Count - 1);

            // Add synchronized state transitions for stop
            if (_currentStates["EquipmentController"] == "PROCESSING")
            {
                AddStateTransition("EquipmentController", "PROCESSING", "ONLINE", "PROCESS_STOP", "", "Operator stopped");
            }
            if (_currentStates["ProcessManager"] == "EXECUTING")
            {
                AddStateTransition("ProcessManager", "EXECUTING", "IDLE", "STOP_PROCESS", "", "Processing stopped");
            }
        }

        private async void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            await SendHostCommand("PAUSE");

            // Pause processing
            _isProcessing = false;

            ProcessStateText.Text = "PAUSED";
            CurrentWaferStatusText.Text = "Status: Paused";
            AddTimelineEntry("Processing paused");
        }

        private async void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            await SendHostCommand("RESUME");

            // Resume processing
            _isProcessing = true;
            _processingStartTime = DateTime.Now; // Reset timer for current wafer

            ProcessStateText.Text = "PROCESSING";
            CurrentWaferStatusText.Text = "Status: Active";
            AddTimelineEntry("Processing resumed");
        }

        private async void LoadCarrierButton_Click(object sender, RoutedEventArgs e)
        {
            string carrierId = CarrierIdTextBox.Text;
            int loadPort = LoadPortComboBox.SelectedIndex + 1;

            // Add lot to scheduler
            var priority = _random.NextDouble() switch
            {
                > 0.9 => LotPriority.SuperHot,
                > 0.7 => LotPriority.HotLot,
                > 0.5 => LotPriority.Rush,
                _ => LotPriority.Normal
            };

            var newLot = new Lot
            {
                LotId = carrierId,
                RecipeId = RecipeComboBox.SelectedItem is ComboBoxItem item ?
                    item.Content.ToString()?.Split('-')[0].Trim() ?? "ASML-193nm-DUV" : "ASML-193nm-DUV",
                Priority = priority,
                DueDate = DateTime.Now.AddHours(4 + _random.Next(20)),
                ProcessingTimeMinutes = 45 + _random.Next(30),
                WaferCount = 25
            };
            _scheduler.AddLot(newLot);
            UpdateSchedulerDisplay();

            // Add carrier load event
            var loadEvent = new EventViewModel
            {
                EventId = 4000 + (uint)loadPort,
                EventName = "Carrier Loaded",
                Data = $"Carrier: {carrierId}, Port: LP{loadPort}",
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 200, 0))
            };
            _events.Insert(0, loadEvent);
            if (_events.Count > 100) _events.RemoveAt(_events.Count - 1);
            Logger.Log($"[REALISTIC] Carrier load event added for {carrierId}");

            // Add synchronized state transition for carrier load
            if (_currentStates["TransportHandler"] == "READY")
            {
                AddStateTransition("TransportHandler", "READY", "MOVING", "CARRIER_ARRIVE", "carrierDetected", $"Carrier {carrierId} at LP{loadPort}");
            }

            await SendCarrierAction(carrierId, "LOAD", loadPort);

            var carrier = new CarrierViewModel
            {
                CarrierId = carrierId,
                LoadPort = $"LP{loadPort}",
                State = "Loading",
                StatusText = "25 wafers"
            };
            _carriers.Add(carrier);

            AddTimelineEntry($"Carrier {carrierId} loaded to LP{loadPort}");

            // Auto-increment carrier ID
            if (carrierId.StartsWith("LOT"))
            {
                int num = int.Parse(carrierId.Substring(3));
                CarrierIdTextBox.Text = $"LOT{num + 1:D3}";
            }
        }

        private async void UnloadCarrierButton_Click(object sender, RoutedEventArgs e)
        {
            string carrierId = CarrierIdTextBox.Text;
            await SendCarrierAction(carrierId, "UNLOAD", 0);

            var carrier = _carriers.FirstOrDefault(c => c.CarrierId == carrierId);
            if (carrier != null)
            {
                _carriers.Remove(carrier);
            }

            AddTimelineEntry($"Carrier {carrierId} unloaded");
        }

        private async void MapButton_Click(object sender, RoutedEventArgs e)
        {
            string carrierId = CarrierIdTextBox.Text;
            await SendCarrierAction(carrierId, "MAP", 0);

            var carrier = _carriers.FirstOrDefault(c => c.CarrierId == carrierId);
            if (carrier != null)
            {
                carrier.State = "Mapped";
            }

            AddTimelineEntry($"Carrier {carrierId} mapped");
        }

        private async void LoadRecipeButton_Click(object sender, RoutedEventArgs e)
        {
            if (RecipeComboBox.SelectedItem is ComboBoxItem item)
            {
                string recipeText = item.Content.ToString() ?? "";
                string recipeId = recipeText.Split('-')[0].Trim();

                await SendRecipeLoad(recipeId);
                CurrentRecipeText.Text = $"Current: {recipeId}";
                AddTimelineEntry($"Recipe {recipeId} loaded");
            }
        }

        private void ClearEventsButton_Click(object sender, RoutedEventArgs e)
        {
            _events.Clear();
        }

        private void ClearStateLogButton_Click(object sender, RoutedEventArgs e)
        {
            _stateTransitions.Clear();
            _totalTransitions = 0;
            TotalTransitionsText.Text = "0";
        }

        private void ExportStateLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"StateLog_{timestamp}.csv";
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);

                using (var writer = new System.IO.StreamWriter(path))
                {
                    writer.WriteLine("Timestamp,StateMachine,FromState,ToState,Event,Context");
                    foreach (var transition in _stateTransitions)
                    {
                        writer.WriteLine($"{transition.Timestamp},{transition.StateMachine},{transition.FromState},{transition.ToState},{transition.Event},{transition.Context}");
                    }
                }

                Logger.Log($"[STATE_LOG] Exported to {path}");
                AddTimelineEntry($"State log exported to {filename}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[STATE_LOG] Export failed: {ex.Message}");
            }
        }

        private void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStateLogVisibility();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            FilterEquipmentController.IsChecked = true;
            FilterProcessManager.IsChecked = true;
            FilterTransportHandler.IsChecked = true;
            FilterRecipeExecutor.IsChecked = true;
            UpdateStateLogVisibility();
        }

        private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            FilterEquipmentController.IsChecked = false;
            FilterProcessManager.IsChecked = false;
            FilterTransportHandler.IsChecked = false;
            FilterRecipeExecutor.IsChecked = false;
            UpdateStateLogVisibility();
        }

        private void InitializeXStateScripts()
        {
            // Create real XStateNet state machines instead of just storing scripts
            _stateMachines = StateMachineDefinitions.CreateAllStateMachines(
                (machineName, fromState, toState, eventName) =>
                {
                    // This callback is called whenever a state transition occurs
                    Dispatcher.Invoke(() =>
                    {
                        // Update current states tracking
                        if (_currentStates.ContainsKey(machineName))
                        {
                            _currentStates[machineName] = toState;
                        }
                        else
                        {
                            _currentStates[machineName] = toState;
                        }

                        // Add the transition to the visual log
                        AddStateTransition(machineName, fromState, toState, eventName, "", "");
                    });
                });

            // Start all state machines
            foreach (var machine in _stateMachines.Values)
            {
                machine.Start();
            }

            // Initialize current states - the actual states will be set via OnTransition callback
            foreach (var kvp in _stateMachines)
            {
                if (!_currentStates.ContainsKey(kvp.Key))
                {
                    _currentStates[kvp.Key] = "Initializing...";
                }
            }

            return; // The old script definitions are no longer needed

#pragma warning disable CS0162 // Unreachable code detected
            // OLD CODE BELOW (kept for reference but not executed)
            _xStateScripts["EquipmentController"] = @"{
  id: 'EquipmentController',
  initial: 'OFFLINE',
  states: {
    OFFLINE: {
      on: {
        POWER_ON: 'INITIALIZING'
      }
    },
    INITIALIZING: {
      on: {
        INIT_SUCCESS: {
          target: 'ONLINE',
          actions: ['updateStatus', 'notifyHost']
        },
        INIT_FAILURE: 'ERROR'
      }
    },
    ONLINE: {
      on: {
        GO_OFFLINE: 'OFFLINE',
        START_PROCESSING: 'PROCESSING',
        ERROR_OCCURRED: 'ERROR'
      }
    },
    PROCESSING: {
      on: {
        PROCESS_COMPLETE: 'ONLINE',
        PROCESS_ABORT: 'ONLINE',
        ERROR_OCCURRED: 'ERROR'
      },
      activities: ['monitorProcess']
    },
    ERROR: {
      on: {
        CLEAR_ERROR: 'OFFLINE',
        RESET: 'INITIALIZING'
      }
    }
  }
}";

            _xStateScripts["ProcessManager"] = @"{
  id: 'ProcessManager',
  initial: 'NOT_READY',
  states: {
    NOT_READY: {
      on: {
        SYSTEM_READY: 'IDLE'
      }
    },
    IDLE: {
      on: {
        START_PROCESS: {
          target: 'SETUP',
          guard: 'isLotReady'
        }
      }
    },
    SETUP: {
      on: {
        PROCESS_START: {
          target: 'EXECUTING',
          guard: 'tempInRange'
        },
        SETUP_FAILURE: 'ERROR'
      },
      activities: ['prepareRecipe', 'checkParameters']
    },
    EXECUTING: {
      on: {
        PROCESS_DONE: 'CLEANUP',
        PROCESS_ABORT: 'CLEANUP',
        ERROR_OCCURRED: 'ERROR'
      },
      activities: ['executeRecipe', 'monitorProgress']
    },
    CLEANUP: {
      on: {
        CLEANUP_DONE: 'IDLE'
      },
      activities: ['cleanChamber', 'resetParameters']
    },
    ERROR: {
      on: {
        ERROR_CLEARED: 'IDLE',
        RESET: 'NOT_READY'
      }
    }
  }
}";

            _xStateScripts["TransportHandler"] = @"{
  id: 'TransportHandler',
  initial: 'IDLE',
  states: {
    IDLE: {
      on: {
        INIT_COMPLETE: 'READY'
      }
    },
    READY: {
      on: {
        WAFER_TRANSFER: {
          target: 'MOVING',
          guard: 'waferPresent'
        },
        LOAD_LOCK: 'LOADING'
      }
    },
    MOVING: {
      on: {
        MOVE_COMPLETE: 'READY',
        MOVE_ERROR: 'ERROR'
      },
      activities: ['moveRobot', 'trackPosition']
    },
    LOADING: {
      on: {
        LOAD_COMPLETE: 'READY',
        UNLOAD_REQUEST: 'UNLOADING'
      }
    },
    UNLOADING: {
      on: {
        UNLOAD_COMPLETE: 'READY'
      }
    },
    ERROR: {
      on: {
        ERROR_RECOVERY: 'READY',
        RESET: 'IDLE'
      }
    }
  }
}";

            _xStateScripts["RecipeExecutor"] = @"{
  id: 'RecipeExecutor',
  initial: 'WAITING',
  states: {
    WAITING: {
      on: {
        LOAD_RECIPE: {
          target: 'LOADING',
          guard: 'recipeValid'
        }
      }
    },
    LOADING: {
      on: {
        RECIPE_LOADED: 'EXECUTING',
        LOAD_ERROR: 'ERROR'
      },
      activities: ['parseRecipe', 'validateParams']
    },
    EXECUTING: {
      on: {
        STEP_COMPLETE: 'VERIFYING',
        EXECUTION_ERROR: 'ERROR'
      },
      activities: ['executeStep', 'updateProgress']
    },
    VERIFYING: {
      on: {
        VERIFICATION_OK: 'EXECUTING',
        ALL_STEPS_DONE: 'COMPLETE',
        VERIFICATION_FAIL: 'ERROR'
      }
    },
    COMPLETE: {
      on: {
        RECIPE_DONE: 'WAITING'
      },
      entry: ['notifyCompletion', 'generateReport']
    },
    ERROR: {
      on: {
        RETRY: 'EXECUTING',
        ABORT: 'WAITING'
      }
    }
  }
}";
        }


        private void ShowTimelineButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if window already exists
            if (_timelineWindow != null && _timelineWindow.IsLoaded)
            {
                _timelineWindow.Activate();
                _timelineWindow.Focus();
            }
            else
            {
                // Create new Timeline window
                _timelineWindow = new TimelineWPF.TimelineWindow()
                {
                    Owner = this
                };

                // Clear any demo data
                _timelineWindow.ClearStateMachines();

                // Register all state machines for real-time monitoring
                foreach (var kvp in _stateMachines)
                {
                    var machineName = kvp.Key;
                    var machine = kvp.Value;

                    // Register for real-time monitoring
                    _timelineWindow.RegisterStateMachine(machine, machineName);
                }

                // Optionally, populate with historical state transitions if needed
                // The real-time adapter will capture all future transitions automatically

                // Remove from tracking when closed
                _timelineWindow.Closed += (s, args) =>
                {
                    _timelineWindow = null;
                };

                _timelineWindow.Show();
            }
        }

        private void ShowDebugTimelineButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if window already exists
            if (_debugTimelineWindow != null && _debugTimelineWindow.IsLoaded)
            {
                _debugTimelineWindow.Activate();
                _debugTimelineWindow.Focus();
            }
            else
            {
                // Create new Debug Timeline window
                _debugTimelineWindow = new StateMachineTimelineWindow()
                {
                    Owner = this
                };

                // Remove from tracking when closed
                _debugTimelineWindow.Closed += (s, args) =>
                {
                    _debugTimelineWindow = null;
                };

                _debugTimelineWindow.Show();
            }
#pragma warning restore CS0162
        }

        private void ShowUmlTimingButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if window already exists
            if (_umlTimingWindow != null && _umlTimingWindow.IsLoaded)
            {
                _umlTimingWindow.Activate();
                _umlTimingWindow.Focus();
            }
            else
            {
                // Create new UML timing diagram window with real state machines
                _umlTimingWindow = new UmlTimingDiagramWindow(_stateMachines)
                {
                    Owner = this
                };

                // Populate with historical state transitions from all state machines
                foreach (var kvp in _stateHistory)
                {
                    var machineName = kvp.Key;
                    var history = kvp.Value;

                    foreach (var (fromState, toState, timestamp) in history)
                    {
                        _umlTimingWindow.AddStateTransition(machineName, fromState, toState, timestamp);
                    }
                }

                // Also add current states
                foreach (var kvp in _currentStates)
                {
                    _umlTimingWindow.SetCurrentState(kvp.Key, kvp.Value);
                }

                // Remove from tracking when closed
                _umlTimingWindow.Closed += (s, args) =>
                {
                    _umlTimingWindow = null;
                };

                _umlTimingWindow.Show();
            }
        }

        private void StateMachineName_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is StateTransitionViewModel transition)
            {
                var machineName = transition.StateMachine;
                if (_xStateScripts.TryGetValue(machineName, out var script))
                {
                    var scriptWindow = new XStateScriptWindow(machineName, script)
                    {
                        Owner = this
                    };
                    scriptWindow.ShowDialog();
                }
                else
                {
                    MessageBox.Show($"No XState script found for {machineName}", "Script Not Found",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void UpdateStateLogVisibility()
        {
            if (_stateTransitions == null) return;

            foreach (var transition in _stateTransitions)
            {
                bool shouldShow = transition.StateMachine switch
                {
                    "EquipmentController" => FilterEquipmentController?.IsChecked ?? true,
                    "ProcessManager" => FilterProcessManager?.IsChecked ?? true,
                    "TransportHandler" => FilterTransportHandler?.IsChecked ?? true,
                    "RecipeExecutor" => FilterRecipeExecutor?.IsChecked ?? true,
                    _ => true
                };

                transition.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ClearAlarmsButton_Click(object sender, RoutedEventArgs e)
        {
            _alarms.Clear();
            UpdateAlarmCount();
        }

        private void AddLotButton_Click(object sender, RoutedEventArgs e)
        {
            // Generate a new lot
            _scheduler.GenerateTestLots(1);
            UpdateSchedulerDisplay();
            Logger.Log("[UI] Added new lot via UI button");
            AddTimelineEntry("New lot added to scheduler queue");
        }

        private void UpdateSchedulerPanel(List<Lot> scheduledQueue, ConcurrentDictionary<string, object> metrics)
        {
            // Update lot queue display
            var lotViewModels = new ObservableCollection<LotViewModel>();
            int position = 1;

            foreach (var lot in scheduledQueue.Take(10)) // Show top 10 lots
            {
                var vm = new LotViewModel
                {
                    QueuePosition = position++,
                    LotId = lot.LotId,
                    RecipeId = lot.RecipeId.Length > 15 ? lot.RecipeId.Substring(0, 15) + "..." : lot.RecipeId,
                    Priority = lot.Priority.ToString(),
                    Score = lot.Score,
                    PriorityColor = lot.Priority switch
                    {
                        LotPriority.SuperHot => new SolidColorBrush(Colors.Red),
                        LotPriority.HotLot => new SolidColorBrush(Colors.OrangeRed),
                        LotPriority.Rush => new SolidColorBrush(Colors.Orange),
                        _ => new SolidColorBrush(Colors.Gray)
                    }
                };
                lotViewModels.Add(vm);
            }

            SchedulerItemsControl.ItemsSource = lotViewModels;

            // Update metrics
            SchedulerWaitingText.Text = $"Waiting: {metrics["WaitingLots"]}";
            SchedulerProcessingText.Text = $"Processing: {metrics["ProcessingLots"]}";
            SchedulerCompletedText.Text = $"Completed: {metrics["CompletedLots"]}";
        }

        private void InitializeStateMonitoring()
        {
            Logger.Log("[STATE_LOG] Initializing state monitoring");

            // Start uptime timer
            _stateLogTimer = new DispatcherTimer();
            _stateLogTimer.Interval = TimeSpan.FromSeconds(1);
            _stateLogTimer.Tick += (s, e) =>
            {
                var uptime = DateTime.Now - _stateMachineStartTime;
                UptimeText.Text = uptime.ToString(@"hh\:mm\:ss");
            };
            _stateLogTimer.Start();

            // Generate some demo state transitions
            GenerateDemoStateTransitions();
        }

        private Brush GetStateColor(string stateName)
        {
            if (_stateColors.TryGetValue(stateName.ToUpper(), out var color))
                return color;

            // Generate a consistent color based on state name hash if not in dictionary
            var hash = stateName.GetHashCode();
            var hue = (hash & 0xFF) / 255.0 * 360;
            var saturation = 0.7 + ((hash >> 8) & 0xFF) / 255.0 * 0.3;
            var lightness = 0.5 + ((hash >> 16) & 0xFF) / 255.0 * 0.3;

            // Convert HSL to RGB
            var rgb = HslToRgb(hue, saturation, lightness);
            return new SolidColorBrush(Color.FromRgb(rgb.Item1, rgb.Item2, rgb.Item3));
        }

        private (byte, byte, byte) HslToRgb(double h, double s, double l)
        {
            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h / 360 + 1.0 / 3.0);
                g = HueToRgb(p, q, h / 360);
                b = HueToRgb(p, q, h / 360 - 1.0 / 3.0);
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private void AddStateTransition(string stateMachine, string fromState, string toState, string eventName, string guard = "", string context = "")
        {
            Dispatcher.Invoke(() =>
            {
                // Check filter first - don't add if not interested
                bool shouldShow = stateMachine switch
                {
                    "EquipmentController" => FilterEquipmentController?.IsChecked ?? true,
                    "ProcessManager" => FilterProcessManager?.IsChecked ?? true,
                    "TransportHandler" => FilterTransportHandler?.IsChecked ?? true,
                    "RecipeExecutor" => FilterRecipeExecutor?.IsChecked ?? true,
                    _ => true
                };

                // Skip adding this transition if the state machine is not checked
                if (!shouldShow)
                {
                    // Still update state tracking even if not showing
                    if (_currentStates.ContainsKey(stateMachine))
                    {
                        _currentStates[stateMachine] = toState;
                    }
                    else
                    {
                        _currentStates[stateMachine] = toState;
                    }
                    return;
                }

                // Update current state tracking
                if (_currentStates.ContainsKey(stateMachine))
                {
                    // Only add transition if the from state matches current state
                    if (_currentStates[stateMachine] != fromState)
                    {
                        // Skip invalid transition
                        return;
                    }
                    _currentStates[stateMachine] = toState;
                }
                else
                {
                    _currentStates[stateMachine] = toState;
                }

                var transition = new StateTransitionViewModel
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                    StateMachine = stateMachine,
                    FromState = fromState,
                    ToState = toState,
                    Event = eventName,
                    Guard = guard,
                    Context = context,
                    Background = Brushes.Transparent,
                    BorderColor = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    MachineColor = stateMachine switch
                    {
                        "EquipmentController" => Brushes.DeepSkyBlue,
                        "ProcessManager" => Brushes.LimeGreen,
                        "TransportHandler" => Brushes.Orange,
                        "RecipeExecutor" => Brushes.MediumPurple,
                        _ => Brushes.White
                    },
                    FromStateColor = GetStateColor(fromState),
                    ToStateColor = GetStateColor(toState),
                    Visibility = Visibility.Visible
                };

                _stateTransitions.Insert(0, transition);

                // Track state history for time chart
                if (!_stateHistory.ContainsKey(stateMachine))
                {
                    _stateHistory[stateMachine] = new ConcurrentBag<(string, string, DateTime)>();
                }
                _stateHistory[stateMachine].Add((fromState, toState, DateTime.Now));


                // Also update UML timing diagram if it's open
                if (_umlTimingWindow != null && _umlTimingWindow.IsLoaded)
                {
                    _umlTimingWindow.AddStateTransition(stateMachine, fromState, toState, DateTime.Now);
                }
                if (_stateTransitions.Count > 500) // Keep only last 500 transitions
                    _stateTransitions.RemoveAt(_stateTransitions.Count - 1);

                _totalTransitions++;
                TotalTransitionsText.Text = _totalTransitions.ToString();

                // Update active states count
                var activeStates = _stateTransitions.Take(10)
                    .Select(t => t.ToState)
                    .Distinct()
                    .Count();
                ActiveStatesText.Text = activeStates.ToString();

                // Calculate event rate
                var recentTransitions = _stateTransitions.Take(10).Count();
                var timeSpan = 10.0; // Last 10 seconds
                EventRateText.Text = (recentTransitions / timeSpan).ToString("F1");

                // Auto-scroll if enabled
                if (AutoScrollCheckBox?.IsChecked == true)
                {
                    StateLogScrollViewer?.ScrollToTop();
                }
            });
        }

        private async void GenerateDemoStateTransitions()
        {
            // Use real XStateNet state machines to trigger transitions
            if (_stateMachines.Count == 0)
            {
                // State machines not initialized yet
                return;
            }

            // Initial state machine setup using real state machines
            await (_stateMachines["EquipmentController"]?.SendAsync("POWER_ON") ?? Task.CompletedTask);
            await (_stateMachines["TransportHandler"]?.SendAsync("INIT_COMPLETE") ?? Task.CompletedTask);
            await Task.Delay(100);
            await (_stateMachines["ProcessManager"]?.SendAsync("SYSTEM_READY") ?? Task.CompletedTask);
            await Task.Delay(100);
            await (_stateMachines["EquipmentController"]?.SendAsync("INIT_SUCCESS") ?? Task.CompletedTask);

            // Initialize SEMI standard state machines using real events
            await (_stateMachines["E30GEM"]?.SendAsync("ENABLE_COMMAND") ?? Task.CompletedTask);
            await (_stateMachines["E87Carrier"]?.SendAsync("CARRIER_DETECTED") ?? Task.CompletedTask);
            await (_stateMachines["E94ControlJob"]?.SendAsync("CREATE_JOB") ?? Task.CompletedTask);
            await (_stateMachines["E37HSMSSession"]?.SendAsync("CONNECT") ?? Task.CompletedTask);
            await (_stateMachines["ProcessControl"]?.SendAsync("START_REQUEST") ?? Task.CompletedTask);
            await (_stateMachines["MaterialHandling"]?.SendAsync("LOAD_START") ?? Task.CompletedTask);
            await (_stateMachines["AlarmManager"]?.SendAsync("WARNING_DETECTED") ?? Task.CompletedTask);

            // Simulate periodic state changes
            var stateTimer = new DispatcherTimer();
            stateTimer.Interval = TimeSpan.FromMilliseconds(800); // More frequent state changes
            stateTimer.Tick += (s, e) =>
            {
                if (_isProcessing)
                {
                    // Define valid transitions based on current states
                    var possibleTransitions = new List<(string, string, string, string, string, string)>();

                    // Check current states and add valid transitions for main state machines
                    if (_currentStates["ProcessManager"] == "IDLE")
                        possibleTransitions.Add(("ProcessManager", "IDLE", "SETUP", "START_PROCESS", "isLotReady", $"Lot:{_scheduler.GetAllLots().FirstOrDefault(l => l.State == LotState.Processing)?.LotId ?? "N/A"}"));
                    if (_currentStates["ProcessManager"] == "SETUP")
                        possibleTransitions.Add(("ProcessManager", "SETUP", "EXECUTING", "PROCESS_START", "tempInRange", $"Temp:{_lastTemp:F1}°C"));
                    if (_currentStates["RecipeExecutor"] == "WAITING")
                        possibleTransitions.Add(("RecipeExecutor", "WAITING", "LOADING", "LOAD_RECIPE", "recipeValid", "Recipe:ASML-193nm-DUV"));
                    if (_currentStates["RecipeExecutor"] == "LOADING")
                        possibleTransitions.Add(("RecipeExecutor", "LOADING", "EXECUTING", "RECIPE_LOADED", "", ""));
                    if (_currentStates["TransportHandler"] == "READY")
                        possibleTransitions.Add(("TransportHandler", "READY", "MOVING", "WAFER_TRANSFER", "waferPresent", $"Slot:{_currentProcessingSlot + 1}"));
                    if (_currentStates["EquipmentController"] == "ONLINE")
                        possibleTransitions.Add(("EquipmentController", "ONLINE", "PROCESSING", "WAFER_IN", "vacuumOK", $"Pressure:{_lastPressure:F2}hPa"));

                    // Add transitions for SEMI standard state machines
                    if (_currentStates.ContainsKey("E30GEM"))
                    {
                        if (_currentStates["E30GEM"] == "enabled")
                            possibleTransitions.Add(("E30GEM", "enabled", "selected", "HOST_SELECT", "", ""));
                        else if (_currentStates["E30GEM"] == "selected")
                            possibleTransitions.Add(("E30GEM", "selected", "executing", "START_PROCESSING", "", ""));
                        else if (_currentStates["E30GEM"] == "executing")
                            possibleTransitions.Add(("E30GEM", "executing", "selected", "PROCESS_COMPLETE", "", ""));
                    }

                    if (_currentStates.ContainsKey("E87Carrier"))
                    {
                        if (_currentStates["E87Carrier"] == "Present")
                            possibleTransitions.Add(("E87Carrier", "Present", "Mapped", "CARRIER_MAPPED", "", $"Slots:25"));
                        else if (_currentStates["E87Carrier"] == "Mapped")
                            possibleTransitions.Add(("E87Carrier", "Mapped", "Processing", "START_CARRIER", "", ""));
                        else if (_currentStates["E87Carrier"] == "Processing")
                            possibleTransitions.Add(("E87Carrier", "Processing", "Mapped", "CARRIER_COMPLETE", "", ""));
                    }

                    if (_currentStates.ContainsKey("E94ControlJob"))
                    {
                        if (_currentStates["E94ControlJob"] == "Created")
                            possibleTransitions.Add(("E94ControlJob", "Created", "Running", "CJ_START", "", $"JobID:CJ-{_random.Next(1000, 9999)}"));
                        else if (_currentStates["E94ControlJob"] == "Running")
                            possibleTransitions.Add(("E94ControlJob", "Running", "Complete", "CJ_COMPLETE", "", ""));
                        else if (_currentStates["E94ControlJob"] == "Complete")
                            possibleTransitions.Add(("E94ControlJob", "Complete", "Created", "NEW_JOB", "", ""));
                    }

                    if (_currentStates.ContainsKey("ProcessControl"))
                    {
                        if (_currentStates["ProcessControl"] == "STARTING")
                            possibleTransitions.Add(("ProcessControl", "STARTING", "PROCESSING", "PROCESS_READY", "", ""));
                        else if (_currentStates["ProcessControl"] == "PROCESSING")
                            possibleTransitions.Add(("ProcessControl", "PROCESSING", "STOPPING", "STOP_REQUEST", "", ""));
                        else if (_currentStates["ProcessControl"] == "STOPPING")
                            possibleTransitions.Add(("ProcessControl", "STOPPING", "IDLE", "STOPPED", "", ""));
                        else if (_currentStates["ProcessControl"] == "IDLE")
                            possibleTransitions.Add(("ProcessControl", "IDLE", "STARTING", "START_REQUEST", "", ""));
                    }

                    if (_currentStates.ContainsKey("MaterialHandling"))
                    {
                        if (_currentStates["MaterialHandling"] == "LOADING")
                            possibleTransitions.Add(("MaterialHandling", "LOADING", "LOADED", "LOAD_COMPLETE", "", $"Material:W{_random.Next(100, 999)}"));
                        else if (_currentStates["MaterialHandling"] == "LOADED")
                            possibleTransitions.Add(("MaterialHandling", "LOADED", "UNLOADING", "UNLOAD_START", "", ""));
                        else if (_currentStates["MaterialHandling"] == "UNLOADING")
                            possibleTransitions.Add(("MaterialHandling", "UNLOADING", "NO_MATERIAL", "UNLOAD_COMPLETE", "", ""));
                        else if (_currentStates["MaterialHandling"] == "NO_MATERIAL")
                            possibleTransitions.Add(("MaterialHandling", "NO_MATERIAL", "LOADING", "LOAD_START", "", ""));
                    }

                    if (possibleTransitions.Count > 0)
                    {
                        // Generate 1-2 transitions per tick when processing
                        var numTransitions = _random.Next(1, Math.Min(3, possibleTransitions.Count + 1));
                        for (int i = 0; i < numTransitions && possibleTransitions.Count > 0; i++)
                        {
                            var index = _random.Next(possibleTransitions.Count);
                            var transition = possibleTransitions[index];
                            AddStateTransition(transition.Item1, transition.Item2, transition.Item3, transition.Item4, transition.Item5, transition.Item6);

                            // Remove the used transition to avoid duplicates in same tick
                            possibleTransitions.RemoveAt(index);
                        }
                    }
                }
                else
                {
                    // Define valid idle transitions based on current states
                    var possibleIdleTransitions = new List<(string, string, string, string, string, string)>();

                    // Main state machines idle transitions
                    if (_currentStates["EquipmentController"] == "PROCESSING")
                        possibleIdleTransitions.Add(("EquipmentController", "PROCESSING", "ONLINE", "PROCESS_COMPLETE", "", ""));
                    if (_currentStates["ProcessManager"] == "EXECUTING")
                        possibleIdleTransitions.Add(("ProcessManager", "EXECUTING", "IDLE", "WAFER_OUT", "", ""));
                    if (_currentStates["TransportHandler"] == "MOVING")
                        possibleIdleTransitions.Add(("TransportHandler", "MOVING", "READY", "TRANSFER_COMPLETE", "", ""));
                    if (_currentStates["RecipeExecutor"] == "EXECUTING")
                        possibleIdleTransitions.Add(("RecipeExecutor", "EXECUTING", "WAITING", "RECIPE_DONE", "", ""));

                    // SEMI standard state machines idle transitions
                    if (_currentStates.ContainsKey("E30GEM") && _currentStates["E30GEM"] == "selected")
                        possibleIdleTransitions.Add(("E30GEM", "selected", "enabled", "HOST_DESELECT", "", ""));

                    if (_currentStates.ContainsKey("E87Carrier") && _currentStates["E87Carrier"] == "Mapped")
                        possibleIdleTransitions.Add(("E87Carrier", "Mapped", "Present", "CARRIER_UNMAPPED", "", ""));

                    if (_currentStates.ContainsKey("E37HSMSSession"))
                    {
                        if (_currentStates["E37HSMSSession"] == "Connected")
                            possibleIdleTransitions.Add(("E37HSMSSession", "Connected", "Selected", "SELECT", "", ""));
                        else if (_currentStates["E37HSMSSession"] == "Selected")
                            possibleIdleTransitions.Add(("E37HSMSSession", "Selected", "Active", "ACTIVATE", "", ""));
                        else if (_currentStates["E37HSMSSession"] == "Active")
                            possibleIdleTransitions.Add(("E37HSMSSession", "Active", "Selected", "DEACTIVATE", "", ""));
                    }

                    if (_currentStates.ContainsKey("AlarmManager"))
                    {
                        if (_currentStates["AlarmManager"] == "WARNING")
                            possibleIdleTransitions.Add(("AlarmManager", "WARNING", "NO_ALARM", "ALARM_CLEARED", "", ""));
                        else if (_currentStates["AlarmManager"] == "ERROR")
                            possibleIdleTransitions.Add(("AlarmManager", "ERROR", "WARNING", "ERROR_RESOLVED", "", ""));
                        else if (_currentStates["AlarmManager"] == "CRITICAL")
                            possibleIdleTransitions.Add(("AlarmManager", "CRITICAL", "ERROR", "CRITICAL_RESOLVED", "", ""));
                        else if (_currentStates["AlarmManager"] == "NO_ALARM" && _random.Next(5) == 0)
                            possibleIdleTransitions.Add(("AlarmManager", "NO_ALARM", "WARNING", "WARNING_DETECTED", "", "Temp exceeds limit"));
                    }

                    if (possibleIdleTransitions.Count > 0 && _random.Next(3) == 0) // 33% chance of transition when idle
                    {
                        var transition = possibleIdleTransitions[_random.Next(possibleIdleTransitions.Count)];
                        AddStateTransition(transition.Item1, transition.Item2, transition.Item3, transition.Item4, transition.Item5, transition.Item6);
                    }
                }
            };
            stateTimer.Start();
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Update statistics
            if (_totalProcessed > 0 || _totalFailed > 0)
            {
                TotalProcessedText.Text = _totalProcessed.ToString();
                TotalFailedText.Text = _totalFailed.ToString();

                double yield = _totalProcessed > 0 ?
                    (double)_totalProcessed / (_totalProcessed + _totalFailed) * 100 : 0;
                YieldText.Text = $"{yield:F1}%";

                var elapsed = DateTime.Now - _startTime;
                double throughput = elapsed.TotalHours > 0 ?
                    _totalProcessed / elapsed.TotalHours : 0;
                ThroughputText.Text = $"{throughput:F0} WPH";
            }

            // Update message count
            MessageCountText.Text = $"Messages: {_messageCount}";

            // Generate periodic events (every 10 ticks = 1 second)
            _eventCounter++;
            if (_eventCounter % 10 == 0)
            {
                GeneratePeriodicEvent();
            }

            // Generate occasional alarms (every 10 seconds with 30% chance)
            if (_eventCounter % 100 == 0 && _random.NextDouble() > 0.7)
            {
                GenerateRandomAlarm();
            }

            // Update wafer processing simulation
            UpdateWaferProcessing();

            // Generate and add chart data synchronized with processing state
            double baseTemp = 22.5;  // Room temperature when idle
            double processingTemp = 185.0;  // Photolithography processing temperature
            double basePressure = 1013.0;  // Atmospheric pressure when idle
            double processingPressure = 0.1;  // Vacuum pressure during processing (in hPa)

            // Calculate temperature and pressure based on processing state
            double targetTemp = baseTemp;
            double targetPressure = basePressure;

            if (_isProcessing && _currentProcessingSlot >= 0)
            {
                // During processing, use process conditions
                var elapsed = (DateTime.Now - _processingStartTime).TotalSeconds;
                double waferProgress = (elapsed % 3.0) / 3.0; // Progress within current wafer (3 sec per wafer)

                // Temperature profile for photolithography
                if (waferProgress < 0.2)
                {
                    // Ramp up phase
                    targetTemp = baseTemp + (processingTemp - baseTemp) * (waferProgress / 0.2);
                    targetPressure = basePressure + (processingPressure - basePressure) * (waferProgress / 0.2);
                }
                else if (waferProgress < 0.8)
                {
                    // Stable processing phase with minor oscillations
                    targetTemp = processingTemp + Math.Sin(waferProgress * Math.PI * 4) * 2;
                    targetPressure = processingPressure + Math.Cos(waferProgress * Math.PI * 4) * 0.02;
                }
                else
                {
                    // Cool down phase
                    double coolProgress = (waferProgress - 0.8) / 0.2;
                    targetTemp = processingTemp - (processingTemp - baseTemp) * coolProgress * 0.3;
                    targetPressure = processingPressure + (basePressure - processingPressure) * coolProgress * 0.3;
                }
            }
            else
            {
                // Idle state - ambient conditions with minor fluctuations
                targetTemp = baseTemp + Math.Sin(DateTime.Now.Ticks / 10000000.0) * 0.5;
                targetPressure = basePressure + Math.Cos(DateTime.Now.Ticks / 10000000.0) * 2;
            }

            // Smooth transition using exponential moving average
            if (!_lastTemp.HasValue) _lastTemp = targetTemp;
            if (!_lastPressure.HasValue) _lastPressure = targetPressure;

            double smoothingFactor = 0.15; // How quickly to respond to changes
            double currentTemp = _lastTemp.Value + (targetTemp - _lastTemp.Value) * smoothingFactor;
            double currentPressure = _lastPressure.Value + (targetPressure - _lastPressure.Value) * smoothingFactor;

            // Add small random noise for realism
            currentTemp += (_random.NextDouble() - 0.5) * 0.3;
            currentPressure += (_random.NextDouble() - 0.5) * 0.05;

            _lastTemp = currentTemp;
            _lastPressure = currentPressure;

            AddTemperaturePoint(currentTemp);
            AddPressurePoint(currentPressure);

            // Update charts with less frequent refresh for better performance
            if (_eventCounter % 5 == 0) // Update every 500ms instead of 100ms
            {
                _temperaturePlotModel.InvalidatePlot(true);
                _pressurePlotModel.InvalidatePlot(true);
            }

            // Update current values display in the UI
            // Note: Temperature and pressure values are displayed in the charts
        }

        private void ClockTimer_Tick(object? sender, EventArgs e)
        {
            TimeText.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void OnSimulatorMessageReceived(object? sender, SecsMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                _messageCount++;
                LastMessageText.Text = $"Last: RX {message.SxFy}";
            });
        }

        private void OnSimulatorMessageSent(object? sender, SecsMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                _messageCount++;
                LastMessageText.Text = $"Last: TX {message.SxFy}";
            });
        }

        private void OnHostMessageReceived(object? sender, HsmsMessage hsmsMessage)
        {
            // Decode to SECS message
            var message = SecsMessage.Decode(
                hsmsMessage.Stream,
                hsmsMessage.Function,
                hsmsMessage.Data ?? Array.Empty<byte>(),
                false);

            Dispatcher.Invoke(() =>
            {
                // Handle events
                if (message.Stream == 6 && message.Function == 11)
                {
                    HandleEventReport(message);
                }
                // Handle alarms
                else if (message.Stream == 5 && message.Function == 1)
                {
                    HandleAlarmReport(message);
                }
            });
        }

        private void HandleEventReport(SecsMessage message)
        {
            Logger.Log($"[REALISTIC] HandleEventReport called with S{message.Stream}F{message.Function}");
            if (message.Data is SecsList list && list.Items.Count >= 2)
            {
                var eventId = (list.Items[0] as SecsU4)?.Value ?? 0;
                string eventName = "Event";
                Logger.Log($"[REALISTIC] Processing event ID: {eventId}");
                var data = new ConcurrentDictionary<string, string>();

                // Parse event data
                for (int i = 1; i < list.Items.Count - 1; i += 2)
                {
                    var key = (list.Items[i] as SecsAscii)?.Value ?? "";
                    var value = GetItemValue(list.Items[i + 1]);

                    if (key == "WAFER_START" || key == "WAFER_COMPLETE")
                    {
                        eventName = key;
                        UpdateWaferStatus(value, key == "WAFER_COMPLETE");
                    }
                    else if (key == "PROCESSING_STARTED")
                    {
                        eventName = "Processing Started";
                    }
                    else if (key == "TEMPERATURE")
                    {
                        if (double.TryParse(value, out double temp))
                        {
                            AddTemperaturePoint(temp);
                        }
                    }
                    else if (key == "PRESSURE")
                    {
                        if (double.TryParse(value, out double pressure))
                        {
                            AddPressurePoint(pressure);
                        }
                    }
                    else if (key == "WAFERS_PROCESSED")
                    {
                        if (int.TryParse(value, out int processed))
                        {
                            _totalProcessed = processed;
                        }
                    }
                    else if (key == "WAFERS_FAILED")
                    {
                        if (int.TryParse(value, out int failed))
                        {
                            _totalFailed = failed;
                        }
                    }

                    data[key] = value;
                }

                // Add to events list
                var eventVm = new EventViewModel
                {
                    EventId = eventId,
                    EventName = eventName,
                    Data = string.Join(", ", data.Select(kvp => $"{kvp.Key}: {kvp.Value}")),
                    Background = new SolidColorBrush(Color.FromArgb(20, 0, 150, 200))
                };

                Logger.Log($"[REALISTIC] Adding event to UI: {eventName} (ID: {eventId})");
                _events.Insert(0, eventVm);
                if (_events.Count > 100) _events.RemoveAt(_events.Count - 1);
                Logger.Log($"[REALISTIC] Total events in list: {_events.Count}");
            }
        }

        private void HandleAlarmReport(SecsMessage message)
        {
            if (message.Data is SecsList list && list.Items.Count >= 3)
            {
                var alarmSet = (list.Items[0] as SecsU1)?.Value == 1;
                var alarmId = (list.Items[1] as SecsU4)?.Value ?? 0;
                var alarmText = (list.Items[2] as SecsAscii)?.Value ?? "";

                if (alarmSet)
                {
                    var alarm = new AlarmViewModel
                    {
                        AlarmId = alarmId,
                        AlarmText = alarmText,
                        Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                        Icon = "Alert",
                        IconColor = Brushes.Red,
                        Background = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0))
                    };
                    _alarms.Insert(0, alarm);
                }
                else
                {
                    var alarm = _alarms.FirstOrDefault(a => a.AlarmId == alarmId);
                    if (alarm != null)
                    {
                        alarm.Icon = "CheckCircle";
                        alarm.IconColor = Brushes.Green;
                        alarm.Background = new SolidColorBrush(Color.FromArgb(30, 0, 255, 0));
                    }
                }

                UpdateAlarmCount();
            }
        }

        private void UpdateAlarmCount()
        {
            int activeCount = _alarms.Count(a => a.Icon == "Alert");
            AlarmCountText.Text = $"({activeCount} Active)";
            AlarmCountText.Foreground = activeCount > 0 ? Brushes.Red : Brushes.Gray;
        }

        private void UpdateWaferStatus(string waferId, bool complete)
        {
            if (!string.IsNullOrEmpty(waferId))
            {
                CurrentWaferIdText.Text = waferId;

                // Extract slot number
                if (waferId.Contains("_W"))
                {
                    string slotStr = waferId.Substring(waferId.IndexOf("_W") + 2);
                    if (int.TryParse(slotStr, out int slot) && slot > 0 && slot <= 25)
                    {
                        CurrentWaferSlotText.Text = $"Slot: {slot}";

                        // Update visual
                        var slotVisual = _waferSlots[slot - 1];
                        if (complete)
                        {
                            slotVisual.Fill = Brushes.LightGreen;
                            WaferProgressBar.Value = 100;
                            CurrentWaferStatusText.Text = "Status: Complete";
                        }
                        else
                        {
                            slotVisual.Fill = Brushes.Yellow;
                            WaferProgressBar.Value = 50;
                            CurrentWaferStatusText.Text = "Status: Processing";
                        }
                    }
                }
            }
        }

        private void AddTemperaturePoint(double temperature)
        {
            var now = DateTimeAxis.ToDouble(DateTime.Now);
            _temperatureSeries.Points.Add(new DataPoint(now, temperature));

            // Keep only last 300 points (5 minutes of data at 1 update per second)
            while (_temperatureSeries.Points.Count > 300)
            {
                _temperatureSeries.Points.RemoveAt(0);
            }

            // Update axes
            if (_temperaturePlotModel.Axes.Count >= 2)
            {
                // Update X-axis to show last 5 minutes (300 seconds)
                var xAxis = _temperaturePlotModel.Axes[0] as DateTimeAxis;
                if (xAxis != null)
                {
                    var maxTime = DateTime.Now;
                    var minTime = maxTime.AddMinutes(-5); // Show 5 minutes of data
                    xAxis.Minimum = DateTimeAxis.ToDouble(minTime);
                    xAxis.Maximum = DateTimeAxis.ToDouble(maxTime);
                }

                // Auto-scale Y-axis based on visible data
                var yAxis = _temperaturePlotModel.Axes[1] as LinearAxis;
                if (yAxis != null && _temperatureSeries.Points.Count > 0)
                {
                    var visiblePoints = _temperatureSeries.Points;
                    if (visiblePoints.Any())
                    {
                        double minTemp = visiblePoints.Min(p => p.Y);
                        double maxTemp = visiblePoints.Max(p => p.Y);
                        double padding = (maxTemp - minTemp) * 0.1; // 10% padding
                        if (padding < 0.5) padding = 0.5; // Minimum padding

                        yAxis.Minimum = minTemp - padding;
                        yAxis.Maximum = maxTemp + padding;
                    }
                }
            }
        }

        private void AddPressurePoint(double pressure)
        {
            var now = DateTimeAxis.ToDouble(DateTime.Now);
            _pressureSeries.Points.Add(new DataPoint(now, pressure));

            // Keep only last 300 points (5 minutes of data at 1 update per second)
            while (_pressureSeries.Points.Count > 300)
            {
                _pressureSeries.Points.RemoveAt(0);
            }

            // Update axes
            if (_pressurePlotModel.Axes.Count >= 2)
            {
                // Update X-axis to show last 5 minutes (300 seconds)
                var xAxis = _pressurePlotModel.Axes[0] as DateTimeAxis;
                if (xAxis != null)
                {
                    var maxTime = DateTime.Now;
                    var minTime = maxTime.AddMinutes(-5); // Show 5 minutes of data
                    xAxis.Minimum = DateTimeAxis.ToDouble(minTime);
                    xAxis.Maximum = DateTimeAxis.ToDouble(maxTime);
                }

                // Auto-scale Y-axis based on visible data
                var yAxis = _pressurePlotModel.Axes[1] as LinearAxis;
                if (yAxis != null && _pressureSeries.Points.Count > 0)
                {
                    var visiblePoints = _pressureSeries.Points;
                    if (visiblePoints.Any())
                    {
                        double minPressure = visiblePoints.Min(p => p.Y);
                        double maxPressure = visiblePoints.Max(p => p.Y);
                        double padding = (maxPressure - minPressure) * 0.1; // 10% padding
                        if (padding < 1) padding = 1; // Minimum padding for pressure

                        yAxis.Minimum = minPressure - padding;
                        yAxis.Maximum = maxPressure + padding;
                    }
                }
            }
        }

        private void GenerateTestEvents()
        {
            Logger.Log("[REALISTIC] Generating test events");

            // Add some test events to demonstrate functionality
            Dispatcher.Invoke(() =>
            {
                // Add equipment online event
                var onlineEvent = new EventViewModel
                {
                    EventId = 1001,
                    EventName = "Equipment Online",
                    Data = "State: LOCAL, Mode: PRODUCTION",
                    Background = new SolidColorBrush(Color.FromArgb(20, 0, 255, 0))
                };
                _events.Add(onlineEvent);

                // Add load port ready event
                var loadPortEvent = new EventViewModel
                {
                    EventId = 2001,
                    EventName = "Load Port Ready",
                    Data = "Port: LP1, Status: READY",
                    Background = new SolidColorBrush(Color.FromArgb(20, 0, 150, 200))
                };
                _events.Add(loadPortEvent);

                // Add process chamber event
                var chamberEvent = new EventViewModel
                {
                    EventId = 3001,
                    EventName = "Process Chamber Ready",
                    Data = "Temperature: 22.5°C, Pressure: 1.01 bar",
                    Background = new SolidColorBrush(Color.FromArgb(20, 100, 100, 255))
                };
                _events.Add(chamberEvent);

                Logger.Log($"[REALISTIC] Added {_events.Count} test events to display");
            });
        }

        private void GenerateTestAlarms()
        {
            Logger.Log("[REALISTIC] Generating test alarms");

            Dispatcher.Invoke(() =>
            {
                // Add a warning alarm
                var warningAlarm = new AlarmViewModel
                {
                    AlarmId = 1001,
                    AlarmText = "Temperature deviation warning",
                    Severity = "WARNING",
                    Icon = "Alert",
                    IconColor = Brushes.Orange,
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 165, 0))
                };
                _alarms.Add(warningAlarm);

                // Add info alarm
                var infoAlarm = new AlarmViewModel
                {
                    AlarmId = 1002,
                    AlarmText = "Maintenance reminder: Clean chamber",
                    Severity = "INFO",
                    Icon = "Info",
                    IconColor = Brushes.Blue,
                    Background = new SolidColorBrush(Color.FromArgb(30, 0, 100, 255))
                };
                _alarms.Add(infoAlarm);

                UpdateAlarmCount();
                Logger.Log($"[REALISTIC] Added {_alarms.Count} test alarms");
            });
        }

        private void InitializeCarriersAndRecipes()
        {
            Logger.Log("[REALISTIC] Initializing carriers and recipes");

            Dispatcher.Invoke(() =>
            {
                // Initialize recipe combo box if empty
                if (RecipeComboBox != null && RecipeComboBox.Items.Count == 0)
                {
                    RecipeComboBox.Items.Clear();
                    RecipeComboBox.Items.Add(new ComboBoxItem { Content = "ASML-193nm-DUV - Deep UV Lithography", IsSelected = true });
                    RecipeComboBox.Items.Add(new ComboBoxItem { Content = "ASML-EUV-13.5nm - Extreme UV" });
                    RecipeComboBox.Items.Add(new ComboBoxItem { Content = "ASML-ArF-Immersion - 193nm Immersion" });
                    RecipeComboBox.Items.Add(new ComboBoxItem { Content = "ASML-KrF-248nm - Krypton Fluoride" });
                    RecipeComboBox.SelectedIndex = 0;

                    // Set current recipe display
                    if (CurrentRecipeText != null)
                    {
                        CurrentRecipeText.Text = "Current: ASML-193nm-DUV";
                    }

                    Logger.Log("[REALISTIC] Recipes loaded");
                }

                // Initialize carrier ID field
                if (CarrierIdTextBox != null)
                {
                    CarrierIdTextBox.Text = "LOT001";
                }

                // Initialize load port combo box
                if (LoadPortComboBox != null && LoadPortComboBox.Items.Count == 0)
                {
                    LoadPortComboBox.Items.Clear();
                    LoadPortComboBox.Items.Add(new ComboBoxItem { Content = "Load Port 1", IsSelected = true });
                    LoadPortComboBox.Items.Add(new ComboBoxItem { Content = "Load Port 2" });
                    LoadPortComboBox.Items.Add(new ComboBoxItem { Content = "Load Port 3" });
                    LoadPortComboBox.SelectedIndex = 0;
                }

                // Auto-load first carrier after a delay
                Task.Run(async () =>
                {
                    await Task.Delay(1500);

                    Dispatcher.Invoke(() =>
                    {
                        // Create initial carrier
                        var carrier = new CarrierViewModel
                        {
                            CarrierId = "LOT001",
                            LoadPort = "LP1",
                            State = "Ready",
                            StatusText = "25 wafers"
                        };
                        _carriers.Add(carrier);

                        // Add event for carrier load
                        var loadEvent = new EventViewModel
                        {
                            EventId = 4001,
                            EventName = "Carrier Auto-Loaded",
                            Data = "Carrier: LOT001, Port: LP1, Recipe: ASML-193nm-DUV",
                            Background = new SolidColorBrush(Color.FromArgb(20, 0, 255, 100))
                        };
                        _events.Insert(0, loadEvent);
                        if (_events.Count > 50) _events.RemoveAt(_events.Count - 1);

                        // Add timeline entry
                        AddTimelineEntry("Carrier LOT001 loaded with recipe ASML-193nm-DUV");

                        // Update carrier ID for next one
                        if (CarrierIdTextBox != null)
                        {
                            CarrierIdTextBox.Text = "LOT002";
                        }

                        Logger.Log("[REALISTIC] Initial carrier LOT001 loaded");
                    });
                });
            });
        }

        private void InitializeScheduler()
        {
            Logger.Log("[SCHEDULER] Initializing manufacturing scheduler");

            // Generate some test lots
            _scheduler.GenerateTestLots(5);

            // Display scheduler metrics
            UpdateSchedulerDisplay();

            Logger.Log("[SCHEDULER] Scheduler initialized with test lots");
        }

        private void UpdateSchedulerDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                var metrics = _scheduler.GetSchedulerMetrics();
                var scheduledQueue = _scheduler.GetScheduledQueue();

                // Update scheduler panel UI
                UpdateSchedulerPanel(scheduledQueue, metrics);

                // Update metrics display
                if (metrics.ContainsKey("WaitingLots"))
                {
                    var waitingLots = metrics["WaitingLots"];
                    var processingLots = metrics["ProcessingLots"];
                    var completedLots = metrics["CompletedLots"];
                    var hotLots = metrics["HotLots"];
                    var overdueLots = metrics["OverdueLots"];
                    var avgWaitTime = metrics["AverageWaitTime"];

                    // Update alarm display with scheduler metrics
                    var schedulerAlarm = new AlarmViewModel
                    {
                        AlarmId = 7000,
                        AlarmText = $"Scheduler: Waiting: {waitingLots} | Processing: {processingLots} | Hot: {hotLots} | Overdue: {overdueLots}",
                        Severity = overdueLots.ToString() != "0" ? "WARNING" : "INFO",
                        Background = overdueLots.ToString() != "0" ?
                            new SolidColorBrush(Color.FromArgb(40, 255, 200, 0)) :
                            new SolidColorBrush(Color.FromArgb(20, 0, 200, 255))
                    };

                    // Remove old scheduler status and add new one
                    var oldSchedulerAlarm = _alarms.FirstOrDefault(a => a.AlarmId == 7000);
                    if (oldSchedulerAlarm != null) _alarms.Remove(oldSchedulerAlarm);
                    _alarms.Insert(0, schedulerAlarm);

                    // Show next 3 lots in queue as events
                    int lotIndex = 0;
                    foreach (var lot in scheduledQueue.Take(3))
                    {
                        var queueEvent = new EventViewModel
                        {
                            EventId = (uint)(8000 + lotIndex),
                            EventName = $"Queue #{lotIndex + 1}",
                            Data = $"Lot {lot.LotId} - Priority: {lot.Priority} - Score: {lot.Score:F2}",
                            Background = lot.Priority >= LotPriority.HotLot ?
                                new SolidColorBrush(Color.FromArgb(30, 255, 100, 0)) :
                                new SolidColorBrush(Color.FromArgb(20, 100, 100, 255))
                        };

                        // Update or add queue event
                        var existingEvent = _events.FirstOrDefault(e => e.EventId == (uint)(8000 + lotIndex));
                        if (existingEvent != null)
                        {
                            var index = _events.IndexOf(existingEvent);
                            _events[index] = queueEvent;
                        }
                        else
                        {
                            _events.Add(queueEvent);
                        }
                        lotIndex++;
                    }

                    Logger.Log($"[SCHEDULER] Waiting: {waitingLots}, Processing: {processingLots}, Hot: {hotLots}, Overdue: {overdueLots}, Avg Wait: {avgWaitTime:F1} min");
                }
            });
        }

        private void InitializeWaferDisplay()
        {
            Logger.Log("[REALISTIC] Initializing wafer display");
            _isProcessing = false; // Don't start processing yet
            _currentProcessingSlot = -1;

            // Initialize all wafers as waiting
            Dispatcher.Invoke(() =>
            {
                for (int i = 0; i < _waferSlots.Count; i++)
                {
                    _waferSlots[i].Fill = Brushes.LightGray;
                }
                CurrentWaferIdText.Text = "Ready";
                CurrentWaferStatusText.Text = "Status: Waiting for START";
                WaferProgressBar.Value = 0;
                ProcessStateText.Text = "IDLE";
            });
        }

        private void StartWaferProcessingSimulation()
        {
            Logger.Log("[REALISTIC] Starting wafer processing simulation");
            _isProcessing = true;
            _currentProcessingSlot = 0;
            _processingStartTime = DateTime.Now;

            // Update display to show processing started
            Dispatcher.Invoke(() =>
            {
                CurrentWaferIdText.Text = "Processing...";
                CurrentWaferStatusText.Text = "Status: Active";
                ProcessStateText.Text = "PROCESSING";
            });
        }

        private void UpdateWaferProcessing()
        {
            if (!_isProcessing || _currentProcessingSlot < 0)
                return;

            var elapsed = (DateTime.Now - _processingStartTime).TotalSeconds;

            // Each wafer takes 3 seconds to process
            const double processingTimePerWafer = 3.0;

            Dispatcher.Invoke(() =>
            {
                // Calculate current progress
                double progress = (elapsed % processingTimePerWafer) / processingTimePerWafer * 100;
                WaferProgressBar.Value = progress;

                // Check if current wafer is complete
                if (elapsed >= processingTimePerWafer)
                {
                    // Mark current wafer as complete
                    if (_currentProcessingSlot < _waferSlots.Count)
                    {
                        _waferSlots[_currentProcessingSlot].Fill = Brushes.LightGreen;

                        // Add completion event
                        var completeEvent = new EventViewModel
                        {
                            EventId = (uint)(6000 + _currentProcessingSlot),
                            EventName = "Wafer Processed",
                            Data = $"Slot {_currentProcessingSlot + 1} completed successfully",
                            Background = new SolidColorBrush(Color.FromArgb(20, 0, 255, 0))
                        };
                        _events.Insert(0, completeEvent);
                        if (_events.Count > 50) _events.RemoveAt(_events.Count - 1);

                        // Add synchronized state transition for wafer completion
                        if (_currentStates["RecipeExecutor"] == "EXECUTING")
                        {
                            AddStateTransition("RecipeExecutor", "EXECUTING", "WAITING", "WAFER_COMPLETE", "", $"Slot {_currentProcessingSlot + 1} done");
                        }
                        if (_currentStates["TransportHandler"] == "MOVING")
                        {
                            AddStateTransition("TransportHandler", "MOVING", "READY", "TRANSFER_COMPLETE", "", $"Wafer {_currentProcessingSlot + 1} processed");
                        }

                        // Update statistics
                        _totalProcessed++;
                        if (_random.NextDouble() > 0.98) // 2% failure rate
                        {
                            _totalFailed++;
                            _waferSlots[_currentProcessingSlot].Fill = Brushes.LightCoral;
                        }

                        // Update carrier progress
                        if (_carriers.Count > 0)
                        {
                            var currentCarrier = _carriers[0];
                            int processedInCarrier = (_currentProcessingSlot + 1) % 25;
                            if (processedInCarrier == 0) processedInCarrier = 25;
                            currentCarrier.StatusText = $"{processedInCarrier}/25 processed";
                            currentCarrier.State = processedInCarrier == 25 ? "Complete" : "Processing";
                        }
                    }

                    // Move to next wafer
                    _currentProcessingSlot++;
                    _processingStartTime = DateTime.Now;

                    // Check if all wafers are processed
                    if (_currentProcessingSlot >= 25)
                    {
                        // Reset for continuous simulation
                        _currentProcessingSlot = 0;

                        // Reset all wafers to waiting state
                        for (int i = 0; i < _waferSlots.Count; i++)
                        {
                            _waferSlots[i].Fill = Brushes.LightGray;
                        }

                        AddTimelineEntry("Batch processing complete, starting new batch");
                    }
                    else
                    {
                        // Highlight current processing wafer
                        _waferSlots[_currentProcessingSlot].Fill = Brushes.Yellow;
                    }
                }
                else if (_currentProcessingSlot < _waferSlots.Count)
                {
                    // Update current wafer status
                    _waferSlots[_currentProcessingSlot].Fill = Brushes.Yellow;
                    CurrentWaferIdText.Text = $"W{_currentProcessingSlot + 1:D3}";
                    CurrentWaferSlotText.Text = $"Slot: {_currentProcessingSlot + 1}";
                    CurrentWaferStatusText.Text = $"Status: Processing ({progress:F0}%)";

                    // Animate processing wafer with pulsing effect
                    double opacity = 0.5 + 0.5 * Math.Sin(elapsed * Math.PI);
                    _waferSlots[_currentProcessingSlot].Opacity = opacity;
                }
            });
        }

        private void InitializeChartData()
        {
            Logger.Log("[REALISTIC] Initializing chart data");

            // Add initial data points to make charts visible
            var now = DateTime.Now;
            for (int i = -20; i <= 0; i++)
            {
                var time = now.AddSeconds(i);
                var timeDouble = DateTimeAxis.ToDouble(time);

                // Add temperature points
                double temp = 22.5 + Math.Sin(i * 0.1) * 0.3;
                _temperatureSeries.Points.Add(new DataPoint(timeDouble, temp));

                // Add pressure points  
                double pressure = 1013 + Math.Cos(i * 0.1) * 3;
                _pressureSeries.Points.Add(new DataPoint(timeDouble, pressure));
            }

            _temperaturePlotModel.InvalidatePlot(true);
            _pressurePlotModel.InvalidatePlot(true);
        }

        private void GeneratePeriodicEvent()
        {
            var eventTypes = new[]
            {
                ("Temperature Update", $"Chamber: {20 + _random.Next(5)}.{_random.Next(10)}°C"),
                ("Pressure Update", $"Chamber: {1.0 + _random.NextDouble() * 0.1:F2} bar"),
                ("Robot Movement", $"Position: Slot {_random.Next(1, 26)}"),
                ("Process Status", "Status: IDLE"),
                ("Sensor Reading", $"Value: {_random.Next(100, 200)}"),
                ("System Health", "All systems operational")
            };

            var selectedEvent = eventTypes[_random.Next(eventTypes.Length)];

            Dispatcher.Invoke(() =>
            {
                var periodicEvent = new EventViewModel
                {
                    EventId = (uint)(5000 + _eventCounter),
                    EventName = selectedEvent.Item1,
                    Data = selectedEvent.Item2,
                    Background = new SolidColorBrush(Color.FromArgb(10, 100, 100, 100))
                };

                _events.Insert(0, periodicEvent);
                if (_events.Count > 50) _events.RemoveAt(_events.Count - 1);
            });
        }

        private void GenerateRandomAlarm()
        {
            var alarmTypes = new[]
            {
                ("High temperature warning", "WARNING", Brushes.Orange),
                ("Low pressure detected", "WARNING", Brushes.Orange),
                ("Door interlock activated", "INFO", Brushes.Blue),
                ("Process deviation alert", "ERROR", Brushes.Red),
                ("Vacuum level optimal", "INFO", Brushes.Green)
            };

            var selectedAlarm = alarmTypes[_random.Next(alarmTypes.Length)];

            Dispatcher.Invoke(() =>
            {
                var alarm = new AlarmViewModel
                {
                    AlarmId = (uint)(2000 + _random.Next(1000)),
                    AlarmText = selectedAlarm.Item1,
                    Severity = selectedAlarm.Item2,
                    Icon = selectedAlarm.Item2 == "ERROR" ? "Error" : selectedAlarm.Item2 == "INFO" ? "Info" : "Alert",
                    IconColor = selectedAlarm.Item3,
                    Background = new SolidColorBrush(Color.FromArgb(30,
                        selectedAlarm.Item3 == Brushes.Red ? (byte)255 : (byte)100,
                        selectedAlarm.Item3 == Brushes.Green ? (byte)255 : (byte)100,
                        selectedAlarm.Item3 == Brushes.Blue ? (byte)255 : (byte)0))
                };

                _alarms.Insert(0, alarm);
                if (_alarms.Count > 20) _alarms.RemoveAt(_alarms.Count - 1);

                UpdateAlarmCount();
                AddTimelineEntry($"ALARM: {selectedAlarm.Item1}");
            });
        }

        private void AddTimelineEntry(string message)
        {
            var entry = new TimelineEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Message = message
            };

            _timeline.Insert(0, entry);
            if (_timeline.Count > 50) _timeline.RemoveAt(_timeline.Count - 1);
        }

        private async Task<SecsMessage?> SendHostMessage(SecsMessage message)
        {
            if (_hostConnection == null) return null;

            try
            {
                message.SystemBytes = (uint)Random.Shared.Next(1, 65536);

                var hsmsMessage = new HsmsMessage
                {
                    Stream = message.Stream,
                    Function = message.Function,
                    MessageType = HsmsMessageType.DataMessage,
                    SystemBytes = message.SystemBytes,
                    Data = message.Encode()
                };

                await _hostConnection.SendMessageAsync(hsmsMessage);
                return message;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send message");
                return null;
            }
        }

        private async Task SendHostCommand(string command)
        {
            var message = new SecsMessage(2, 41, true)
            {
                Data = new SecsList(
                    new SecsAscii(command),
                    new SecsList()
                )
            };
            await SendHostMessage(message);
        }

        private async Task SendCarrierAction(string carrierId, string action, int loadPort)
        {
            var message = new SecsMessage(3, 17, true)
            {
                Data = new SecsList(
                    new SecsAscii(carrierId),
                    new SecsAscii(action),
                    new SecsU4((uint)loadPort)
                )
            };
            await SendHostMessage(message);
        }

        private async Task SendRecipeLoad(string recipeId)
        {
            var message = new SecsMessage(7, 1, true)
            {
                Data = new SecsList(
                    new SecsAscii(recipeId),
                    new SecsList()
                )
            };
            await SendHostMessage(message);
        }

        private string GetItemValue(SecsItem item)
        {
            return item switch
            {
                SecsAscii ascii => ascii.Value,
                SecsU1 u1 => u1.Value.ToString(),
                SecsU4 u4 => u4.Value.ToString(),
                SecsF8 f8 => f8.Value.ToString("F2"),
                _ => item.ToString() ?? ""
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _ = StopSimulator();
            base.OnClosing(e);
        }
    }

    // View Models
    public class CarrierViewModel : INotifyPropertyChanged
    {
        private string _state = "";

        public string CarrierId { get; set; } = "";
        public string LoadPort { get; set; } = "";

        public string State
        {
            get => _state;
            set
            {
                _state = value;
                OnPropertyChanged();
            }
        }

        public string StatusText { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class EventViewModel
    {
        public uint EventId { get; set; }
        public string EventName { get; set; } = "";
        public string Data { get; set; } = "";
        public Brush Background { get; set; } = Brushes.Transparent;
    }

    public class AlarmViewModel
    {
        public uint AlarmId { get; set; }
        public string AlarmText { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Severity { get; set; } = "WARNING";
        public string Icon { get; set; } = "Alert";
        public Brush IconColor { get; set; } = Brushes.Red;
        public Brush Background { get; set; } = Brushes.Transparent;
    }

    public class LotViewModel
    {
        public int QueuePosition { get; set; }
        public string LotId { get; set; } = "";
        public string RecipeId { get; set; } = "";
        public string Priority { get; set; } = "";
        public double Score { get; set; }
        public Brush PriorityColor { get; set; } = Brushes.Gray;
    }

    public class StateTransitionViewModel : INotifyPropertyChanged
    {
        private Visibility _visibility = Visibility.Visible;

        public string Timestamp { get; set; } = "";
        public string StateMachine { get; set; } = "";
        public string FromState { get; set; } = "";
        public string ToState { get; set; } = "";
        public string Event { get; set; } = "";
        public string Guard { get; set; } = "";
        public string TransitionLabel => !string.IsNullOrEmpty(Guard) ? $"{Event}/{Guard}" : Event;
        public string Context { get; set; } = "";
        public Brush Background { get; set; } = Brushes.Transparent;
        public Brush BorderColor { get; set; } = Brushes.LightGray;
        public Brush MachineColor { get; set; } = Brushes.Black;
        public Brush FromStateColor { get; set; } = Brushes.White;
        public Brush ToStateColor { get; set; } = Brushes.White;

        public Visibility Visibility
        {
            get => _visibility;
            set
            {
                _visibility = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TimelineEntry
    {
        public string Timestamp { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
