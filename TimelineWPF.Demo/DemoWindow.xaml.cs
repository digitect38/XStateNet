using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TimelineWPF;
using XStateNet;

namespace TimelineWPF.Demo
{
    public partial class DemoWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, StateMachine> _stateMachines;
        private readonly Random _random;
        private double _simulationTime;
        private bool _isRunning;
        private int _eventCount;

        public DemoWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(50);
            _timer.Tick += Timer_Tick;

            _stateMachines = new Dictionary<string, StateMachine>();
            _random = new Random();

            InitializeDemo();
            UpdateStatus();
        }

        private void InitializeDemo()
        {
            // Start with Traffic Light demo
            LoadTrafficLightDemo();
        }

        private void Timer_Tick(object sender, EventArgs e)
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
            var currentState = machine.GetCurrentState();
            if (currentState != null)
            {
                // Simulate state transition
                var eventName = GenerateEventForState(machineName, currentState);
                if (!string.IsNullOrEmpty(eventName))
                {
                    machine.SendEvent(eventName);

                    // Update timeline
                    var newState = machine.GetCurrentState();
                    Timeline.AddStateTransition(machineName, currentState, newState, _simulationTime * 1000);

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

            StatusText.Text = "Traffic Light System Loaded";
        }

        private void AddTrafficLight(string name, double delay)
        {
            var states = new List<string> { "red", "yellow", "green" };
            Timeline.AddStateMachine(name, states, "red");

            var machine = CreateTrafficLightMachine(name);
            _stateMachines[name] = machine;

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);

            // Add initial state
            Timeline.AddStateTransition(name, "", "red", delay);
        }

        private StateMachine CreateTrafficLightMachine(string name)
        {
            var script = @"
            {
                id: '" + name + @"',
                initial: 'red',
                states: {
                    red: {
                        on: { TIMER: 'green' }
                    },
                    green: {
                        on: { TIMER: 'yellow' }
                    },
                    yellow: {
                        on: { TIMER: 'red' }
                    }
                }
            }";

            return StateMachine.CreateFromScript(script);
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
            Timeline.AddStateMachine(name, states, "idle");

            var machine = CreateElevatorMachine(name);
            _stateMachines[name] = machine;

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);

            Timeline.AddStateTransition(name, "", "idle", 0);
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

            return StateMachine.CreateFromScript(script);
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
            Timeline.AddStateMachine(name, states, "idle");

            var machine = CreateManufacturingMachine(name);
            _stateMachines[name] = machine;

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);

            Timeline.AddStateTransition(name, "", "idle", 0);
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

            return StateMachine.CreateFromScript(script);
        }

        private void AddQualityControl(string name)
        {
            var states = new List<string> { "waiting", "inspecting", "pass", "fail", "rework" };
            Timeline.AddStateMachine(name, states, "waiting");

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);

            Timeline.AddStateTransition(name, "", "waiting", 0);
        }

        private void AddPackaging(string name)
        {
            var states = new List<string> { "ready", "packaging", "sealing", "labeling", "complete" };
            Timeline.AddStateMachine(name, states, "ready");

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);

            Timeline.AddStateTransition(name, "", "ready", 0);
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
            Timeline.AddStateMachine(name, states, "closed");

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);

            Timeline.AddStateTransition(name, "", "closed", 0);
        }

        private void AddHttpSession(string name)
        {
            var states = new List<string> { "idle", "connecting", "sending", "waiting", "receiving", "processing", "done", "error" };
            Timeline.AddStateMachine(name, states, "idle");

            MachineList.Items.Add(name);
            TargetMachineCombo.Items.Add(name);

            Timeline.AddStateTransition(name, "", "idle", 0);
        }

        private void ClearAll()
        {
            _stateMachines.Clear();
            MachineList.Items.Clear();
            TargetMachineCombo.Items.Clear();
            Timeline.ClearStateMachines();
            _eventCount = 0;
            _simulationTime = 0;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            MachineCountText.Text = _stateMachines.Count.ToString();
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
            Timeline.StartSimulation();
            StatusText.Text = "Running";
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
            _timer.Stop();
            Timeline.PauseSimulation();
            StatusText.Text = "Paused";
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
            _timer.Stop();
            _simulationTime = 0;
            _eventCount = 0;
            Timeline.ResetSimulation();
            TimeDisplay.Text = "00:00:00.000";
            StatusText.Text = "Reset";
            UpdateStatus();
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedText != null)
            {
                SpeedText.Text = $"{SpeedSlider.Value:F1}x";
                Timeline.SetPlaybackSpeed(SpeedSlider.Value);
            }
        }

        private void StepModeCheck_Changed(object sender, RoutedEventArgs e)
        {
            Timeline.SetStepDisplayMode(StepModeCheck.IsChecked ?? false);
        }

        private void ShowEventsCheck_Changed(object sender, RoutedEventArgs e)
        {
            // Timeline.ShowEvents = ShowEventsCheck.IsChecked ?? true;
        }

        private void ShowActionsCheck_Changed(object sender, RoutedEventArgs e)
        {
            // Timeline.ShowActions = ShowActionsCheck.IsChecked ?? true;
        }

        private void RealtimeModeCheck_Changed(object sender, RoutedEventArgs e)
        {
            Timeline.SetRealtimeMode(RealtimeModeCheck.IsChecked ?? true);
        }

        private void AddMachineBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddMachineDialog();
            if (dialog.ShowDialog() == true)
            {
                var name = dialog.MachineName;
                var states = dialog.States.Split(',').Select(s => s.Trim()).ToList();

                Timeline.AddStateMachine(name, states, states.First());
                MachineList.Items.Add(name);
                TargetMachineCombo.Items.Add(name);

                UpdateStatus();
            }
        }

        private void RemoveMachineBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MachineList.SelectedItem is string machineName)
            {
                Timeline.RemoveStateMachine(machineName);
                _stateMachines.Remove(machineName);
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
                        var currentState = machine.GetCurrentState();
                        machine.SendEvent(eventName);
                        var newState = machine.GetCurrentState();

                        Timeline.AddStateTransition(machineName, currentState, newState, _simulationTime * 1000);
                    }
                }
                else if (EventTypeCombo.SelectedIndex == 1) // Event
                {
                    Timeline.AddEvent(machineName, eventName, _simulationTime * 1000);
                }
                else if (EventTypeCombo.SelectedIndex == 2) // Action
                {
                    Timeline.AddAction(machineName, eventName, _simulationTime * 1000);
                }

                _eventCount++;
                UpdateStatus();
            }
        }
    }
}