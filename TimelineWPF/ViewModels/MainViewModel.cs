using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using TimelineWPF.Models;

namespace TimelineWPF.ViewModels
{
    public enum SimulationStatus { Stopped, Running, Paused }

    public class MainViewModel : INotifyPropertyChanged, ITimelineDataProvider
    {
        private SimulationStatus _simulationState;
        private double _simulationTime;
        private double _playbackSpeed = 1.0;
        private bool _isRealtimeMode = true;
        private bool _isStepDisplayMode;
        private double _viewOffset;
        private double _zoomFactor = 0.01;
        private double _playbackZoomFactor = 0.01;

        private readonly Random _random = new Random();
        private DateTime _lastTimestamp;

        public ObservableCollection<StateMachineDefinition> StateMachines { get; } = new ObservableCollection<StateMachineDefinition>();

        public SimulationStatus SimulationState
        {
            get => _simulationState;
            set { _simulationState = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsStartEnabled)); OnPropertyChanged(nameof(IsPauseEnabled)); OnPropertyChanged(nameof(IsResetEnabled)); }
        }

        public double SimulationTime { get => _simulationTime; set { _simulationTime = value; OnPropertyChanged(); } }
        public double PlaybackSpeed { get => _playbackSpeed; set { _playbackSpeed = value; OnPropertyChanged(); } }
        public bool IsRealtimeMode
        {
            get => _isRealtimeMode;
            set
            {
                if (_isRealtimeMode != value)
                {
                    _isRealtimeMode = value;
                    OnPropertyChanged();
                    OnRealtimeModeChanged();
                }
            }
        }
        public bool IsStepDisplayMode { get => _isStepDisplayMode; set { _isStepDisplayMode = value; OnPropertyChanged(); } }
        public double ViewOffset { get => _viewOffset; set { _viewOffset = value; OnPropertyChanged(); } }
        public double ZoomFactor { get => _zoomFactor; set { _zoomFactor = value; OnPropertyChanged(); } }

        public bool IsStartEnabled => SimulationState != SimulationStatus.Running;
        public bool IsPauseEnabled => SimulationState == SimulationStatus.Running;
        public bool IsResetEnabled => SimulationState != SimulationStatus.Stopped;

        public ICommand StartCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand ResetCommand { get; }

        public MainViewModel()
        {
            StartCommand = new RelayCommand(_ => StartSimulation());
            PauseCommand = new RelayCommand(_ => PauseSimulation());
            ResetCommand = new RelayCommand(_ => ResetSimulation());

            // Don't initialize with hardcoded state machines anymore
            // Clients will add them dynamically
            ResetSimulation();
            // Set initial ZoomFactor to show 10 seconds in a 1200px width
            _zoomFactor = 1200.0 / 10000000.0;
            _playbackZoomFactor = _zoomFactor;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (SimulationState != SimulationStatus.Running) return;

            var now = DateTime.Now;
            var deltaTime = (now - _lastTimestamp).TotalMilliseconds;
            _lastTimestamp = now;

            SimulationTime += deltaTime * 1000 * PlaybackSpeed;

            if (!IsRealtimeMode)
            {
                ViewOffset = SimulationTime;
            }
        }

        private void StartSimulation()
        {
            if (SimulationState == SimulationStatus.Running) return;

            SimulationState = SimulationStatus.Running;
            _lastTimestamp = DateTime.Now;
            CompositionTarget.Rendering += OnRendering;
        }

        private void PauseSimulation()
        {
            if (SimulationState != SimulationStatus.Running) return;

            SimulationState = SimulationStatus.Paused;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void ResetSimulation()
        {
            SimulationState = SimulationStatus.Stopped;
            CompositionTarget.Rendering -= OnRendering;

            SimulationTime = 0;
            ViewOffset = 0;

            // Only generate demo data if no state machines are defined
            // Commented out to not interfere with tests and real-time usage
            // if (StateMachines.Count == 0)
            // {
            //     GenerateDemoData();
            // }
            // Force property change notification for collections
            OnPropertyChanged(nameof(StateMachines));
        }

        private void GenerateDemoData()
        {
            // Add demo state machines for testing
            AddStateMachine("Demo SM1", new List<string> { "idle", "active" }, "idle");
            AddStateMachine("Demo SM2", new List<string> { "stopped", "running" }, "stopped");
            AddStateMachine("Demo SM3", new List<string> { "off", "on" }, "off");

            // Generate some demo transitions
            double time = 0;
            var random = new Random();
            for (int i = 0; i < 50; i++)
            {
                time += random.Next(500000, 2000000); // 0.5 to 2 seconds
                AddStateTransition("Demo SM1", "idle", "active", time);
                time += random.Next(500000, 2000000);
                AddStateTransition("Demo SM1", "active", "idle", time);

                AddEvent("Demo SM2", "START", time);
                AddStateTransition("Demo SM2", "stopped", "running", time);
                time += random.Next(1000000, 3000000);
                AddAction("Demo SM2", "process", time);
                AddStateTransition("Demo SM2", "running", "stopped", time);
            }
        }

        private void OnRealtimeModeChanged()
        {
            if (!IsRealtimeMode) // Switched to Playback
            {
                ViewOffset = SimulationTime;
                ZoomFactor = _playbackZoomFactor; // Restore user-set zoom
            }
            else // Switched to Realtime
            {
                // This will be handled by the control's size change logic
                // We just need to trigger a redraw/recalc
                OnPropertyChanged(nameof(ZoomFactor));
                if (SimulationState != SimulationStatus.Running)
                {
                    StartSimulation();
                }
            }
        }

        public void UpdateZoom(double delta, double mouseX, double controlWidth)
        {
            if (IsRealtimeMode) return;

            double timeAtMouse = ViewOffset + (mouseX - controlWidth / 2) / ZoomFactor;

            double zoomIntensity = 0.1;
            double zoomMultiplier = delta > 0 ? (1 - zoomIntensity) : (1 + zoomIntensity);

            double newZoomFactor = ZoomFactor * zoomMultiplier;
            newZoomFactor = Math.Max(0.0001, newZoomFactor);
            newZoomFactor = Math.Min(1, newZoomFactor);

            ZoomFactor = newZoomFactor;
            _playbackZoomFactor = newZoomFactor; // Save for playback mode

            ViewOffset = timeAtMouse - (mouseX - controlWidth / 2) / ZoomFactor;
        }

        public void Pan(double dx)
        {
            if (IsRealtimeMode) return;

            if (SimulationState == SimulationStatus.Running) PauseSimulation();

            ViewOffset -= dx / ZoomFactor;
            SimulationTime = ViewOffset;
        }

        public void SetDefaultZoomFactor(double controlWidth)
        {
            if (controlWidth > 0)
            {
                // 10 seconds (10,000,000 us) should fit in the view
                ZoomFactor = controlWidth / 10000000.0;
                _playbackZoomFactor = ZoomFactor; // Also update playback zoom factor
            }
        }

        private long GetJitteryDuration(long baseDuration = 1000000, double jitter = 0.4)
        {
            return (long)(baseDuration * (1 - jitter) + _random.NextDouble() * (baseDuration * jitter * 2));
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region ITimelineDataProvider Implementation

        public void AddStateMachine(string name, List<string> states, string initialState)
        {
            var existingMachine = StateMachines.FirstOrDefault(sm => sm.Name == name);
            if (existingMachine != null)
            {
                // Update existing machine
                existingMachine.States = states;
                existingMachine.InitialState = initialState;
            }
            else
            {
                // Add new machine
                var newMachine = new StateMachineDefinition
                {
                    Name = name,
                    States = states,
                    InitialState = initialState
                };
                StateMachines.Add(newMachine);
            }
            OnPropertyChanged(nameof(StateMachines));
        }

        public void RemoveStateMachine(string name)
        {
            var machine = StateMachines.FirstOrDefault(sm => sm.Name == name);
            if (machine != null)
            {
                StateMachines.Remove(machine);
                OnPropertyChanged(nameof(StateMachines));
            }
        }

        public void ClearStateMachines()
        {
            StateMachines.Clear();
            OnPropertyChanged(nameof(StateMachines));
        }

        public void AddStateTransition(string machineName, string fromState, string toState, double timestamp)
        {
            var machine = StateMachines.FirstOrDefault(sm => sm.Name == machineName);
            if (machine != null)
            {
                // End the previous state
                var lastState = machine.Data.LastOrDefault(item => item.Type == TimelineItemType.State);
                if (lastState != null && lastState.Duration == 0)
                {
                    lastState.Duration = (long)timestamp - lastState.Time;
                }

                // Add new state
                var newItem = new TimelineItem
                {
                    Time = (long)timestamp,
                    Type = TimelineItemType.State,
                    Name = toState,
                    Duration = 0 // Will be set when the next transition occurs
                };
                machine.Data.Add(newItem);
            }
        }

        public void AddEvent(string machineName, string eventName, double timestamp)
        {
            var machine = StateMachines.FirstOrDefault(sm => sm.Name == machineName);
            if (machine != null)
            {
                machine.Data.Add(new TimelineItem
                {
                    Time = (long)timestamp,
                    Type = TimelineItemType.Event,
                    Name = eventName
                });
            }
        }

        public void AddAction(string machineName, string actionName, double timestamp)
        {
            var machine = StateMachines.FirstOrDefault(sm => sm.Name == machineName);
            if (machine != null)
            {
                machine.Data.Add(new TimelineItem
                {
                    Time = (long)timestamp,
                    Type = TimelineItemType.Action,
                    Name = actionName
                });
            }
        }

        public void ClearTimelineData(string machineName)
        {
            var machine = StateMachines.FirstOrDefault(sm => sm.Name == machineName);
            if (machine != null)
            {
                machine.Data.Clear();
                // Add initial state
                machine.Data.Add(new TimelineItem
                {
                    Time = 0,
                    Type = TimelineItemType.State,
                    Name = machine.InitialState,
                    Duration = 0
                });
            }
        }

        public void ClearAllTimelineData()
        {
            foreach (var machine in StateMachines)
            {
                machine.Data.Clear();
                // Add initial state
                machine.Data.Add(new TimelineItem
                {
                    Time = 0,
                    Type = TimelineItemType.State,
                    Name = machine.InitialState,
                    Duration = 0
                });
            }
        }

        public void SetCurrentState(string machineName, string state, double timestamp)
        {
            AddStateTransition(machineName, "", state, timestamp);
        }

        public IEnumerable<StateMachineDefinition> GetStateMachines()
        {
            return StateMachines;
        }

        #endregion
    }
}