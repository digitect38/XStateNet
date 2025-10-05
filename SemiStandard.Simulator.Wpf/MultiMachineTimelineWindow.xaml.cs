using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SemiStandard.Simulator.Wpf
{
    public partial class MultiMachineTimelineWindow : Window
    {
        private readonly ConcurrentDictionary<string, List<StateTransition>> _machineTransitions = new();
        private readonly ConcurrentDictionary<string, object> _transitionLocks = new();
        private readonly ConcurrentDictionary<string, string> _currentMachineStates = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastMachineStateChangeTimes = new();
        private readonly ConcurrentDictionary<string, Color> _stateColorCache = new();

        private DispatcherTimer _continuousUpdateTimer;
        private DateTime _sessionStartTime = DateTime.Now;
        private const int MAX_TRANSITION_HISTORY = 2000;
        private const double PIXELS_PER_SECOND = 50;

        public MultiMachineTimelineWindow()
        {
            InitializeComponent();

            // Setup continuous update timer
            _continuousUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // Update every 100ms
            };
            _continuousUpdateTimer.Tick += ContinuousUpdateTimer_Tick;
            _continuousUpdateTimer.Start();
        }

        public void AddStateTransition(string machineName, string fromState, string toState, DateTime timestamp)
        {
            var lockObj = _transitionLocks.GetOrAdd(machineName, _ => new object());
            lock (lockObj)
            {
                // Initialize machine list if needed
                var transitions = _machineTransitions.GetOrAdd(machineName, _ => new List<StateTransition>());

                // Add transition
                transitions.Add(new StateTransition
                {
                    MachineName = machineName,
                    FromState = fromState,
                    ToState = toState,
                    Timestamp = timestamp
                });
            }

            // Update current state (thread-safe)
            _currentMachineStates[machineName] = toState;
            _lastMachineStateChangeTimes[machineName] = timestamp;

            // Trim old data
            TrimOldData(machineName);

            // Update display
            UpdateTimeline();
        }

        private void ContinuousUpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateTimeline();
        }

        private void UpdateTimeline()
        {
            TimelineCanvas.Children.Clear();

            if (_machineTransitions.Count == 0) return;

            var currentTime = DateTime.Now;
            var allTransitions = _machineTransitions.SelectMany(kvp => kvp.Value).ToList();
            if (allTransitions.Count == 0) return;

            // Calculate time range
            var minTime = allTransitions.Min(t => t.Timestamp);
            var maxTime = currentTime;
            var timeSpan = maxTime - minTime;
            if (timeSpan.TotalSeconds < 1) timeSpan = TimeSpan.FromSeconds(10);

            // Build machine-state hierarchy
            var machineStateMap = new ConcurrentDictionary<string, List<string>>();
            var stateYPositions = new ConcurrentDictionary<string, double>();

            foreach (var machine in _machineTransitions.Keys.OrderBy(k => k))
            {
                var states = _machineTransitions[machine]
                    .SelectMany(t => new[] { t.FromState, t.ToState })
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                machineStateMap[machine] = states;
            }

            // Canvas dimensions
            double leftMargin = 280;  // Space for machine.state names
            double rightMargin = 50;
            double topMargin = 40;
            double bottomMargin = 20;
            double stateHeight = 25;  // Height per state row
            double machineSpacing = 30; // Spacing between machines
            double canvasWidth = Math.Max(2000, leftMargin + rightMargin + (timeSpan.TotalSeconds * PIXELS_PER_SECOND));

            // Calculate canvas height
            double currentY = topMargin;
            foreach (var machine in machineStateMap.Keys.OrderBy(k => k))
            {
                if (currentY > topMargin) currentY += machineSpacing;

                foreach (var state in machineStateMap[machine])
                {
                    var key = $"{machine}.{state}";
                    stateYPositions[key] = currentY;
                    currentY += stateHeight;
                }
            }

            double canvasHeight = currentY + bottomMargin;
            TimelineCanvas.Width = canvasWidth;
            TimelineCanvas.Height = canvasHeight;

            double plotWidth = canvasWidth - leftMargin - rightMargin;

            // Draw background grid
            DrawBackgroundGrid(canvasWidth, canvasHeight, leftMargin, topMargin, bottomMargin, minTime, maxTime, plotWidth);

            // Draw state labels and guide lines
            DrawStateLabels(machineStateMap, stateYPositions, leftMargin, canvasWidth, rightMargin, stateHeight, machineSpacing);

            // Draw state transitions for each machine
            DrawStateTransitions(stateYPositions, leftMargin, minTime, timeSpan, plotWidth, currentTime);

            // Draw current time indicator
            DrawCurrentTimeIndicator(leftMargin, plotWidth, topMargin, canvasHeight - bottomMargin, currentTime, minTime, timeSpan);

            // Update status
            MachineCountText.Text = $"{_machineTransitions.Count} Machines";
            LastUpdateText.Text = $"Last Update: {currentTime:HH:mm:ss}";

            // Auto-scroll
            if (AutoScrollCheck?.IsChecked == true)
            {
                TimelineScroller.ScrollToRightEnd();
            }
        }

        private void DrawBackgroundGrid(double canvasWidth, double canvasHeight, double leftMargin,
            double topMargin, double bottomMargin, DateTime minTime, DateTime maxTime, double plotWidth)
        {
            // Vertical time grid lines (every 10 seconds)
            var timeSpan = maxTime - minTime;
            var secondsPerLine = Math.Max(10, (int)(timeSpan.TotalSeconds / 20));

            for (int i = 0; i <= timeSpan.TotalSeconds; i += secondsPerLine)
            {
                double x = leftMargin + (i / timeSpan.TotalSeconds) * plotWidth;

                var gridLine = new Line
                {
                    X1 = x,
                    Y1 = topMargin,
                    X2 = x,
                    Y2 = canvasHeight - bottomMargin,
                    Stroke = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 4 }
                };
                TimelineCanvas.Children.Add(gridLine);

                // Time label
                var time = minTime.AddSeconds(i);
                var timeLabel = new TextBlock
                {
                    Text = time.ToString("HH:mm:ss"),
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    FontSize = 9
                };
                Canvas.SetLeft(timeLabel, x - 25);
                Canvas.SetTop(timeLabel, canvasHeight - bottomMargin + 5);
                TimelineCanvas.Children.Add(timeLabel);
            }
        }

        private void DrawStateLabels(ConcurrentDictionary<string, List<string>> machineStateMap,
            ConcurrentDictionary<string, double> stateYPositions, double leftMargin, double canvasWidth,
            double rightMargin, double stateHeight, double machineSpacing)
        {
            // Create a fixed background panel for labels that doesn't scroll
            var labelBackground = new Rectangle
            {
                Width = leftMargin,
                Height = TimelineCanvas.Height,
                Fill = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Stroke = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(labelBackground, 0);
            Canvas.SetTop(labelBackground, 0);
            Canvas.SetZIndex(labelBackground, 1);
            TimelineCanvas.Children.Add(labelBackground);

            string lastMachine = "";

            foreach (var machine in machineStateMap.Keys.OrderBy(k => k))
            {
                // Draw machine separator
                if (lastMachine != "")
                {
                    var firstStateY = stateYPositions[$"{machine}.{machineStateMap[machine].First()}"];
                    var separatorLine = new Line
                    {
                        X1 = leftMargin,  // Start from left margin instead of 0
                        Y1 = firstStateY - machineSpacing / 2,
                        X2 = canvasWidth,
                        Y2 = firstStateY - machineSpacing / 2,
                        Stroke = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                        StrokeThickness = 1
                    };
                    TimelineCanvas.Children.Add(separatorLine);
                }

                // Draw machine name
                if (machineStateMap[machine].Count > 0)
                {
                    var firstStateY = stateYPositions[$"{machine}.{machineStateMap[machine].First()}"];

                    // Truncate machine name if too long
                    string displayName = machine.Length > 20 ? machine.Substring(0, 17) + "..." : machine;

                    var machineLabel = new TextBlock
                    {
                        Text = displayName,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 255)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 13,
                        ClipToBounds = true
                    };
                    Canvas.SetLeft(machineLabel, 10);
                    Canvas.SetTop(machineLabel, firstStateY - 5);
                    Canvas.SetZIndex(machineLabel, 2);
                    TimelineCanvas.Children.Add(machineLabel);
                }

                // Draw state labels
                foreach (var state in machineStateMap[machine])
                {
                    var key = $"{machine}.{state}";
                    if (!stateYPositions.ContainsKey(key)) continue;

                    double y = stateYPositions[key];

                    // Truncate state name if too long
                    string displayState = state.Length > 15 ? state.Substring(0, 12) + "..." : state;

                    // State label with clipping
                    var stateLabel = new TextBlock
                    {
                        Text = displayState,
                        Foreground = new SolidColorBrush(GetStateColor(state)),
                        FontSize = 11,
                        TextAlignment = TextAlignment.Right,
                        ClipToBounds = true,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Canvas.SetLeft(stateLabel, 140);
                    Canvas.SetTop(stateLabel, y - 8);
                    stateLabel.Width = leftMargin - 150;
                    stateLabel.MaxWidth = leftMargin - 150;
                    Canvas.SetZIndex(stateLabel, 2);
                    TimelineCanvas.Children.Add(stateLabel);

                    // Horizontal guide line
                    var guideLine = new Line
                    {
                        X1 = leftMargin,
                        Y1 = y,
                        X2 = canvasWidth - rightMargin,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                        StrokeThickness = 0.3
                    };
                    TimelineCanvas.Children.Add(guideLine);
                }

                lastMachine = machine;
            }

            // Add vertical separator line between labels and timeline
            var verticalSeparator = new Line
            {
                X1 = leftMargin,
                Y1 = 0,
                X2 = leftMargin,
                Y2 = TimelineCanvas.Height,
                Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                StrokeThickness = 1
            };
            Canvas.SetZIndex(verticalSeparator, 3);
            TimelineCanvas.Children.Add(verticalSeparator);
        }

        private void DrawStateTransitions(ConcurrentDictionary<string, double> stateYPositions, double leftMargin,
            DateTime minTime, TimeSpan timeSpan, double plotWidth, DateTime currentTime)
        {
            foreach (var machineKvp in _machineTransitions)
            {
                var machineName = machineKvp.Key;
                var transitions = machineKvp.Value.OrderBy(t => t.Timestamp).ToList();

                if (transitions.Count == 0) continue;

                string currentState = "";
                DateTime lastTime = minTime;

                foreach (var transition in transitions)
                {
                    double x = leftMargin + ((transition.Timestamp - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;

                    if (!string.IsNullOrEmpty(currentState))
                    {
                        var currentKey = $"{machineName}.{currentState}";
                        if (stateYPositions.ContainsKey(currentKey))
                        {
                            double stateY = stateYPositions[currentKey];
                            double lastX = leftMargin + ((lastTime - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;

                            // Draw state line
                            var stateLine = new Line
                            {
                                X1 = lastX,
                                Y1 = stateY,
                                X2 = x,
                                Y2 = stateY,
                                Stroke = new SolidColorBrush(GetStateColor(currentState)),
                                StrokeThickness = 3,
                                SnapsToDevicePixels = true
                            };
                            TimelineCanvas.Children.Add(stateLine);

                            // Draw vertical transition line
                            var newKey = $"{machineName}.{transition.ToState}";
                            if (stateYPositions.ContainsKey(newKey) && newKey != currentKey)
                            {
                                double newStateY = stateYPositions[newKey];
                                var transitionLine = new Line
                                {
                                    X1 = x,
                                    Y1 = stateY,
                                    X2 = x,
                                    Y2 = newStateY,
                                    Stroke = Brushes.White,
                                    StrokeThickness = 1,
                                    Opacity = 0.8,
                                    SnapsToDevicePixels = true
                                };
                                TimelineCanvas.Children.Add(transitionLine);
                            }
                        }
                    }

                    currentState = transition.ToState;
                    lastTime = transition.Timestamp;
                }

                // Extend current state to now
                if (!string.IsNullOrEmpty(currentState) && _lastMachineStateChangeTimes.ContainsKey(machineName))
                {
                    var currentKey = $"{machineName}.{currentState}";
                    if (stateYPositions.ContainsKey(currentKey))
                    {
                        double stateY = stateYPositions[currentKey];
                        double lastX = leftMargin + ((lastTime - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;
                        double nowX = leftMargin + ((currentTime - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;

                        // Draw active state line
                        var currentStateLine = new Line
                        {
                            X1 = lastX,
                            Y1 = stateY,
                            X2 = nowX,
                            Y2 = stateY,
                            Stroke = new SolidColorBrush(GetStateColor(currentState)),
                            StrokeThickness = 3,
                            Opacity = 0.9,
                            SnapsToDevicePixels = true
                        };
                        TimelineCanvas.Children.Add(currentStateLine);

                        // Add active indicator
                        var activeDot = new Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(GetStateColor(currentState))
                        };
                        Canvas.SetLeft(activeDot, nowX - 4);
                        Canvas.SetTop(activeDot, stateY - 4);
                        TimelineCanvas.Children.Add(activeDot);
                    }
                }
            }
        }

        private void DrawCurrentTimeIndicator(double leftMargin, double plotWidth, double topMargin,
            double bottomY, DateTime currentTime, DateTime minTime, TimeSpan timeSpan)
        {
            double nowX = leftMargin + ((currentTime - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;

            // Current time line
            var currentTimeLine = new Line
            {
                X1 = nowX,
                Y1 = topMargin,
                X2 = nowX,
                Y2 = bottomY,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            TimelineCanvas.Children.Add(currentTimeLine);

            // Current time label
            var currentTimeLabel = new TextBlock
            {
                Text = $"NOW",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(currentTimeLabel, nowX - 15);
            Canvas.SetTop(currentTimeLabel, topMargin - 15);
            TimelineCanvas.Children.Add(currentTimeLabel);
        }

        private void TrimOldData(string machineName)
        {
            if (!_machineTransitions.TryGetValue(machineName, out var transitions))
                return;

            var lockObj = _transitionLocks.GetOrAdd(machineName, _ => new object());
            lock (lockObj)
            {
                if (transitions.Count > MAX_TRANSITION_HISTORY)
                {
                    var toRemove = transitions.Count - MAX_TRANSITION_HISTORY;
                    transitions.RemoveRange(0, toRemove);
                }
            }
        }

        private Color GetStateColor(string stateName)
        {
            if (_stateColorCache.ContainsKey(stateName))
                return _stateColorCache[stateName];

            var color = stateName switch
            {
                "OFFLINE" => Color.FromRgb(180, 50, 50),
                "ONLINE" => Color.FromRgb(0, 255, 0),
                "INITIALIZING" => Color.FromRgb(255, 255, 0),
                "PROCESSING" => Color.FromRgb(0, 150, 255),
                "ERROR" => Color.FromRgb(255, 0, 0),
                "IDLE" => Color.FromRgb(100, 200, 100),
                "READY" => Color.FromRgb(0, 200, 150),
                "SETUP" => Color.FromRgb(255, 165, 0),
                "EXECUTING" => Color.FromRgb(147, 112, 219),
                "CLEANUP" => Color.FromRgb(255, 192, 203),
                "MOVING" => Color.FromRgb(64, 224, 208),
                "LOADING" => Color.FromRgb(255, 215, 0),
                "WAITING" => Color.FromRgb(255, 140, 0),
                "VERIFYING" => Color.FromRgb(135, 206, 235),
                "COMPLETE" => Color.FromRgb(50, 205, 50),
                _ => Color.FromRgb(138, 43, 226)
            };

            _stateColorCache[stateName] = color;
            return color;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateTimeline();
            StatusText.Text = "Refreshed";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _continuousUpdateTimer?.Stop();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private class StateTransition
        {
            public string MachineName { get; set; } = "";
            public string FromState { get; set; } = "";
            public string ToState { get; set; } = "";
            public DateTime Timestamp { get; set; }
        }
    }
}
