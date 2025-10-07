using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TimelineWPF.PubSub;
using TimelineWPF.ViewModels;
using XStateNet;

namespace TimelineWPF.Demo
{
    public partial class DemoWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly ConcurrentDictionary<string, StateMachine> _stateMachines;
        private readonly Random _random;
        private TimelineManager _timelineManager;
        private double _simulationTime;
        private bool _isRunning;
        private int _eventCount;

        public DemoWindow()
        {
            _stateMachines = new ConcurrentDictionary<string, StateMachine>();

            InitializeComponent();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += Timer_Tick;


            _random = new Random();

            // Check if optimized event bus should be used (from saved settings or default)
            var useOptimized = false; // Default to standard event bus
            if (useOptimized)
            {
                TimelineManager.ConfigureOptimizedEventBus(true);
            }

            _timelineManager = TimelineManager.Instance;

            // Ensure Timeline has a MainViewModel
            if (Timeline.DataContext == null)
            {
                Timeline.DataContext = new MainViewModel();
            }

            // Subscribe the view model to timeline events
            if (Timeline.DataContext is MainViewModel vm)
            {
                _timelineManager.SubscribeViewModel(vm);
            }

            InitializeDemo();
            UpdateStatus();
        }

        private void InitializeDemo()
        {
            // Start with Traffic Light demo
            LoadTrafficLightDemo();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning) return;

            _simulationTime += 50 * SpeedSlider.Value;
            TimeDisplay.Text = TimeSpan.FromMilliseconds(_simulationTime).ToString(@"hh\:mm\:ss\.fff");

            // Simulate random events based on selected demo
            if (_random.NextDouble() < 0.1) // 10% chance per tick
            {
                GenerateRandomEvent();
            }
        }

        private void GenerateRandomEvent()
        {
            if (_stateMachines.Count == 0) return;

            var machines = _stateMachines.Keys.ToList();
            var machineName = machines[_random.Next(machines.Count)];
            var machine = _stateMachines[machineName];

            // Generate event based on current state
            var currentState = machine.GetActiveStateNames();
            if (currentState != null)
            {
                // Simulate state transition
                var eventName = GenerateEventForState(machineName, currentState);
                if (!string.IsNullOrEmpty(eventName))
                {
                    // Send event and let Pub/Sub handle the timeline update
                    machine.Send(eventName);

                    // Manually publish the event and transition since XStateNet doesn't have these events yet
                    _timelineManager.PublishEvent(machineName, eventName);
                    var newState = machine.GetActiveStateNames();
                    _timelineManager.PublishStateTransition(machineName, currentState, newState, eventName);

                    _eventCount++;
                    UpdateStatus();
                }
            }
        }

        private string GenerateEventForState(string machineName, string currentState)
        {
            // Logic for generating appropriate events based on machine type and current state
            if (machineName.Contains("Traffic"))
            {
                return currentState switch
                {
                    "green" => "TIMER",
                    "yellow" => "TIMER",
                    "red" => "TIMER",
                    _ => ""
                };
            }
            else if (machineName.Contains("Elevator"))
            {
                return currentState switch
                {
                    "idle" => _random.NextDouble() < 0.5 ? "CALL_UP" : "CALL_DOWN",
                    "moving_up" => "ARRIVE",
                    "moving_down" => "ARRIVE",
                    "doors_open" => "CLOSE",
                    _ => ""
                };
            }

            return "";
        }

        private void LoadTrafficLightDemo()
        {
            ClearAll();

            // Add traffic light state machines
            AddTrafficLight("Traffic Light North", 0);
            AddTrafficLight("Traffic Light South", 1000);
            AddTrafficLight("Traffic Light East", 2000);
            AddTrafficLight("Traffic Light West", 3000);

            if (StatusText != null)
            {
                StatusText.Text = "Traffic Light System Loaded";
            }
        }

        private void AddTrafficLight(string name, double delay)
        {
            try
            {
                var states = new List<string> { "red", "yellow", "green" };

                var machine = CreateTrafficLightMachine(name);
                if (machine == null)
                {
                    MessageBox.Show($"Failed to create traffic light machine: {name}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // _stateMachines is readonly and initialized in constructor, so this check is not needed
                // Just add the machine

                _stateMachines[name] = machine;

                // Register with TimelineManager for Pub/Sub
                if (_timelineManager != null)
                {
                    _timelineManager.RegisterStateMachine(name, machine, states);
                }

                if (MachineList != null)
                {
                    MachineList.Items.Add(name);
                }

                if (TargetMachineCombo != null)
                {
                    TargetMachineCombo.Items.Add(name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding traffic light '{name}': {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private StateMachine? CreateTrafficLightMachine(string name)
        {
            try
            {
                var script = @"
                {
                    'id': '" + name + @"',
                    'initial': 'red',
                    'states': {
                        'red': {
                            'on': { 'TIMER': 'green' }
                        },
                        'green': {
                            'on': { 'TIMER': 'yellow' }
                        },
                        'yellow': {
                            'on': { 'TIMER': 'red' }
                        }
                    }
                }";

                // CreateFromScript might need actions and guards even if empty
                var actions = new ActionMap();
                var guards = new GuardMap();
                return StateMachineFactory.CreateFromScript(script, threadSafe: false, true, actions, guards);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create state machine from script: {ex.Message}", "Script Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void LoadElevatorDemo()
        {
            ClearAll();

            // Add elevator controllers
            AddElevator("Elevator A");
            AddElevator("Elevator B");
            AddElevator("Elevator C");

            StatusText.Text = "Elevator System Loaded";
        }

        private void AddElevator(string name)
        {
            var states = new List<string> { "idle", "moving_up", "moving_down", "doors_open", "maintenance" };

            var machine = CreateElevatorMachine(name);
            _stateMachines[name] = machine;

            // Register with TimelineManager for Pub/Sub
            _timelineManager.RegisterStateMachine(name, machine, states);

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);
        }

        private StateMachine CreateElevatorMachine(string name)
        {
            var script = @"
            {
                id: '" + name + @"',
                initial: 'idle',
                states: {
                    idle: {
                        on: {
                            CALL_UP: 'moving_up',
                            CALL_DOWN: 'moving_down',
                            MAINTENANCE: 'maintenance'
                        }
                    },
                    moving_up: {
                        on: {
                            ARRIVE: 'doors_open',
                            EMERGENCY: 'idle'
                        }
                    },
                    moving_down: {
                        on: {
                            ARRIVE: 'doors_open',
                            EMERGENCY: 'idle'
                        }
                    },
                    doors_open: {
                        on: {
                            CLOSE: 'idle'
                        }
                    },
                    maintenance: {
                        on: {
                            RESUME: 'idle'
                        }
                    }
                }
            }";

            return StateMachineFactory.CreateFromScript(script);
        }

        private void LoadManufacturingDemo()
        {
            ClearAll();

            // Add manufacturing process machines
            AddManufacturingProcess("Assembly Line 1");
            AddManufacturingProcess("Assembly Line 2");
            AddQualityControl("Quality Control");
            AddPackaging("Packaging Station");

            StatusText.Text = "Manufacturing Process Loaded";
        }

        private void AddManufacturingProcess(string name)
        {
            var states = new List<string> { "idle", "loading", "processing", "unloading", "error" };

            var machine = CreateManufacturingMachine(name);
            _stateMachines[name] = machine;

            // Register with TimelineManager for Pub/Sub
            _timelineManager.RegisterStateMachine(name, machine, states);

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);
        }

        private StateMachine CreateManufacturingMachine(string name)
        {
            var script = @"
            {
                id: '" + name + @"',
                initial: 'idle',
                states: {
                    idle: {
                        on: { START: 'loading' }
                    },
                    loading: {
                        on: {
                            LOADED: 'processing',
                            ERROR: 'error'
                        }
                    },
                    processing: {
                        on: {
                            COMPLETE: 'unloading',
                            ERROR: 'error'
                        }
                    },
                    unloading: {
                        on: {
                            UNLOADED: 'idle',
                            ERROR: 'error'
                        }
                    },
                    error: {
                        on: { RESET: 'idle' }
                    }
                }
            }";

            return StateMachineFactory.CreateFromScript(script);
        }

        private void AddQualityControl(string name)
        {
            var states = new List<string> { "waiting", "inspecting", "pass", "fail", "rework" };

            // Create a simple state machine for quality control
            var machine = CreateQualityControlMachine(name);
            _stateMachines[name] = machine;

            // Register with TimelineManager for Pub/Sub
            _timelineManager.RegisterStateMachine(name, machine, states);

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);
        }

        private StateMachine CreateQualityControlMachine(string name)
        {
            var script = @"
            {
                id: '" + name + @"',
                initial: 'waiting',
                states: {
                    waiting: {
                        on: { INSPECT: 'inspecting' }
                    },
                    inspecting: {
                        on: {
                            PASS: 'pass',
                            FAIL: 'fail'
                        }
                    },
                    pass: {
                        on: { NEXT: 'waiting' }
                    },
                    fail: {
                        on: { REWORK: 'rework' }
                    },
                    rework: {
                        on: { REINSPECT: 'inspecting' }
                    }
                }
            }";

            return StateMachineFactory.CreateFromScript(script);
        }

        private void AddPackaging(string name)
        {
            var states = new List<string> { "ready", "packaging", "sealing", "labeling", "complete" };

            // Create a simple state machine for packaging
            var machine = CreatePackagingMachine(name);
            _stateMachines[name] = machine;

            // Register with TimelineManager for Pub/Sub
            _timelineManager.RegisterStateMachine(name, machine, states);

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);
        }

        private StateMachine CreatePackagingMachine(string name)
        {
            var script = @"
            {
                id: '" + name + @"',
                initial: 'ready',
                states: {
                    ready: {
                        on: { START: 'packaging' }
                    },
                    packaging: {
                        on: { PACKED: 'sealing' }
                    },
                    sealing: {
                        on: { SEALED: 'labeling' }
                    },
                    labeling: {
                        on: { LABELED: 'complete' }
                    },
                    complete: {
                        on: { RESET: 'ready' }
                    }
                }
            }";

            return StateMachineFactory.CreateFromScript(script);
        }

        private void LoadNetworkProtocolDemo()
        {
            ClearAll();

            // Add network protocol state machines
            AddTcpConnection("TCP Connection 1");
            AddTcpConnection("TCP Connection 2");
            AddHttpSession("HTTP Session");

            StatusText.Text = "Network Protocol Demo Loaded";
        }

        private void AddTcpConnection(string name)
        {
            var states = new List<string> { "closed", "listen", "syn_sent", "syn_received", "established", "close_wait", "last_ack", "fin_wait_1", "fin_wait_2", "time_wait" };

            // For simplicity, create a basic TCP state machine
            var machine = CreateTcpMachine(name);
            _stateMachines[name] = machine;

            // Register with TimelineManager for Pub/Sub
            _timelineManager.RegisterStateMachine(name, machine, states);

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);
        }

        private StateMachine CreateTcpMachine(string name)
        {
            var script = @"
            {
                id: '" + name + @"',
                initial: 'closed',
                states: {
                    closed: {
                        on: { LISTEN: 'listen', SYN: 'syn_sent' }
                    },
                    listen: {
                        on: { SYN: 'syn_received', CLOSE: 'closed' }
                    },
                    syn_sent: {
                        on: { SYN_ACK: 'established', TIMEOUT: 'closed' }
                    },
                    syn_received: {
                        on: { ACK: 'established', CLOSE: 'fin_wait_1' }
                    },
                    established: {
                        on: { CLOSE: 'fin_wait_1', FIN: 'close_wait' }
                    },
                    close_wait: {
                        on: { CLOSE: 'last_ack' }
                    },
                    last_ack: {
                        on: { ACK: 'closed' }
                    },
                    fin_wait_1: {
                        on: { FIN: 'closing', ACK: 'fin_wait_2' }
                    },
                    fin_wait_2: {
                        on: { FIN: 'time_wait' }
                    },
                    time_wait: {
                        on: { TIMEOUT: 'closed' }
                    }
                }
            }";

            return StateMachineFactory.CreateFromScript(script);
        }

        private void AddHttpSession(string name)
        {
            var states = new List<string> { "idle", "connecting", "sending", "waiting", "receiving", "processing", "done", "error" };

            var machine = CreateHttpSessionMachine(name);
            _stateMachines[name] = machine;

            // Register with TimelineManager for Pub/Sub
            _timelineManager.RegisterStateMachine(name, machine, states);

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);
        }

        private StateMachine CreateHttpSessionMachine(string name)
        {
            var script = @"
            {
                id: '" + name + @"',
                initial: 'idle',
                states: {
                    idle: {
                        on: { CONNECT: 'connecting' }
                    },
                    connecting: {
                        on: { CONNECTED: 'sending', ERROR: 'error' }
                    },
                    sending: {
                        on: { SENT: 'waiting', ERROR: 'error' }
                    },
                    waiting: {
                        on: { RESPONSE: 'receiving', TIMEOUT: 'error' }
                    },
                    receiving: {
                        on: { RECEIVED: 'processing', ERROR: 'error' }
                    },
                    processing: {
                        on: { COMPLETE: 'done', ERROR: 'error' }
                    },
                    done: {
                        on: { RESET: 'idle' }
                    },
                    error: {
                        on: { RETRY: 'idle' }
                    }
                }
            }";

            return StateMachineFactory.CreateFromScript(script);
        }

        private void ClearAll()
        {
            // Clear TimelineManager registrations if it exists
            _timelineManager?.Clear();

            _stateMachines?.Clear();
            MachineList?.Items.Clear();
            TargetMachineCombo?.Items.Clear();
            _eventCount = 0;
            _simulationTime = 0;

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (MachineCountText != null)
                MachineCountText.Text = _stateMachines?.Count.ToString() ?? "0";
            if (EventCountText != null)
                EventCountText.Text = _eventCount.ToString();
        }

        // Event Handlers
        private void DemoSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DemoSelector.SelectedIndex < 0) return;

            switch (DemoSelector.SelectedIndex)
            {
                case 0: LoadTrafficLightDemo(); break;
                case 1: LoadElevatorDemo(); break;
                case 2: LoadManufacturingDemo(); break;
                case 3: LoadNetworkProtocolDemo(); break;
                case 4: ClearAll(); break;
            }
        }

        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = true;
            _timer.Start();
            // Start simulation in view model
            if (Timeline.DataContext is MainViewModel vm)
                vm.SimulationState = SimulationStatus.Running;
            StatusText.Text = "Running";
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
            _timer.Stop();
            // Pause simulation in view model
            if (Timeline.DataContext is MainViewModel vm)
                vm.SimulationState = SimulationStatus.Paused;
            StatusText.Text = "Paused";
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
            _timer.Stop();
            _simulationTime = 0;
            _eventCount = 0;
            // Reset simulation in view model
            if (Timeline.DataContext is MainViewModel vm)
            {
                vm.SimulationState = SimulationStatus.Stopped;
                vm.SimulationTime = 0;
            }
            TimeDisplay.Text = "00:00:00.000";
            StatusText.Text = "Reset";
            UpdateStatus();
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedText != null)
            {
                SpeedText.Text = $"{SpeedSlider.Value:F1}x";
                if (Timeline.DataContext is MainViewModel vm)
                    vm.PlaybackSpeed = SpeedSlider.Value;
            }
        }

        private void StepModeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (Timeline != null && Timeline.DataContext is MainViewModel vm && StepModeCheck != null)
            {
                vm.IsStepDisplayMode = StepModeCheck.IsChecked ?? false;
            }
        }

        private void ShowEventsCheck_Changed(object sender, RoutedEventArgs e)
        {
            // if (Timeline != null && ShowEventsCheck != null)
            //     Timeline.ShowEvents = ShowEventsCheck.IsChecked ?? true;
        }

        private void ShowActionsCheck_Changed(object sender, RoutedEventArgs e)
        {
            // if (Timeline != null && ShowActionsCheck != null)
            //     Timeline.ShowActions = ShowActionsCheck.IsChecked ?? true;
        }

        private void RealtimeModeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (Timeline != null && Timeline.DataContext is MainViewModel vm && RealtimeModeCheck != null)
            {
                vm.IsRealtimeMode = RealtimeModeCheck.IsChecked ?? true;
            }
        }

        private void UseOptimizedEventBusCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (UseOptimizedEventBusCheck == null)
                return;

            var useOptimized = UseOptimizedEventBusCheck.IsChecked ?? false;

            // Show a message that the application needs to restart to apply changes
            MessageBox.Show(
                "The event bus configuration will be applied on the next application restart.\n\n" +
                $"Optimized Event Bus will be {(useOptimized ? "enabled" : "disabled")} next time.",
                "Configuration Change",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Save the preference for next startup (you could save this to a settings file)
            // For now, we'll just log it
            System.Diagnostics.Debug.WriteLine($"Optimized Event Bus will be {(useOptimized ? "enabled" : "disabled")} on restart");
        }

        private void PerfTestBtn_Click(object sender, RoutedEventArgs e)
        {
            var perfWindow = new PerformanceTestWindow();
            perfWindow.Owner = this;
            perfWindow.ShowDialog();
        }

        private void CircuitBreakerBtn_Click(object sender, RoutedEventArgs e)
        {
            var circuitBreakerWindow = new TimelineWPF.CircuitBreakerSimulatorWindow();
            circuitBreakerWindow.Owner = this;
            circuitBreakerWindow.Show();
        }

        private void AddMachineBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddMachineDialog();
            if (dialog.ShowDialog() == true)
            {
                var name = dialog.MachineName;
                var states = dialog.States.Split(',').Select(s => s.Trim()).ToList();

                Timeline.DataProvider?.AddStateMachine(name, states, states.First());
                MachineList.Items.Add(name);
                TargetMachineCombo.Items.Add(name);

                UpdateStatus();
            }
        }

        private void RemoveMachineBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MachineList.SelectedItem is string machineName)
            {
                Timeline.DataProvider?.RemoveStateMachine(machineName);
                _stateMachines.TryRemove(machineName, out _);
                MachineList.Items.Remove(machineName);
                TargetMachineCombo.Items.Remove(machineName);

                UpdateStatus();
            }
        }

        private void TriggerEventBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TargetMachineCombo.SelectedItem is string machineName)
            {
                var eventName = EventNameText.Text;
                if (string.IsNullOrWhiteSpace(eventName)) return;

                if (EventTypeCombo.SelectedIndex == 0) // State Transition
                {
                    if (_stateMachines.ContainsKey(machineName))
                    {
                        var machine = _stateMachines[machineName];
                        var currentState = machine.GetActiveStateNames();
                        machine.Send(eventName);
                        var newState = machine.GetActiveStateNames();

                        // Publish via Pub/Sub instead of direct update
                        _timelineManager.PublishStateTransition(machineName, currentState, newState, eventName);
                    }
                }
                else if (EventTypeCombo.SelectedIndex == 1) // Event
                {
                    // Publish event via Pub/Sub
                    _timelineManager.PublishEvent(machineName, eventName);
                }
                else if (EventTypeCombo.SelectedIndex == 2) // Action
                {
                    // Publish action via Pub/Sub
                    _timelineManager.PublishAction(machineName, eventName);
                }

                _eventCount++;
                UpdateStatus();
            }
        }
    }
}
