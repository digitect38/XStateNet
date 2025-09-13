using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SemiStandard.Simulator.Wpf
{
    public partial class StateTimeChartWindow : Window
    {
        private readonly string _machineName;
        private readonly Dictionary<string, StateTimeData> _stateTimeData = new();
        private readonly ObservableCollection<StateBreakdownItem> _stateBreakdownItems = new();
        private readonly List<StateTransitionEvent> _stateTransitions = new();
        
        
        private DateTime? _lastStateChangeTime = null;
        private string _currentState = "";
        private DateTime _sessionStartTime = DateTime.Now;
        
        // Performance optimization fields
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private const double UPDATE_THROTTLE_SECONDS = 0.5; // Update at most every 500ms
        private const int MAX_TRANSITION_HISTORY = 1000; // Keep only last 1000 transitions
        private const double MAX_TIME_WINDOW_HOURS = 24; // Show only last 24 hours
        
        // Continuous timeline update timer
        private DispatcherTimer _continuousUpdateTimer;
        
        public StateTimeChartWindow(string machineName)
        {
            InitializeComponent();
            _machineName = machineName;
            MachineNameText.Text = machineName;
            
            StateBreakdownList.ItemsSource = _stateBreakdownItems;
            DataContext = this;
            
            // Setup continuous update timer for timeline (before initializing view)
            _continuousUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250) // Update every 250ms to reduce jitter
            };
            _continuousUpdateTimer.Tick += ContinuousUpdateTimer_Tick;
            
            // Initialize timeline view by default
            InitializeTimelineView();
            
            // Subscribe to combo box changes
            ChartTypeCombo.SelectionChanged += ChartTypeCombo_SelectionChanged;
            
            // Add sample data for additional state machines
            GenerateSampleStateTransitions();
        }
        
        private void GenerateSampleStateTransitions()
        {
            // Add some sample transitions to demonstrate multiple state tracking
            var now = DateTime.Now;
            
            // Process Control State Machine
            AddStateTransition("IDLE", "STARTING", now.AddSeconds(-30));
            AddStateTransition("STARTING", "PROCESSING", now.AddSeconds(-25));
            AddStateTransition("PROCESSING", "PAUSED", now.AddSeconds(-15));
            AddStateTransition("PAUSED", "PROCESSING", now.AddSeconds(-10));
            AddStateTransition("PROCESSING", "COMPLETING", now.AddSeconds(-5));
            AddStateTransition("COMPLETING", "IDLE", now.AddSeconds(-2));
            
            // Material Handling State Machine
            AddStateTransition("NO_MATERIAL", "LOADING", now.AddSeconds(-28));
            AddStateTransition("LOADING", "MATERIAL_READY", now.AddSeconds(-24));
            AddStateTransition("MATERIAL_READY", "MATERIAL_IN_PROCESS", now.AddSeconds(-20));
            AddStateTransition("MATERIAL_IN_PROCESS", "UNLOADING", now.AddSeconds(-8));
            AddStateTransition("UNLOADING", "NO_MATERIAL", now.AddSeconds(-3));
            
            // Alarm State Machine
            AddStateTransition("NO_ALARM", "WARNING", now.AddSeconds(-22));
            AddStateTransition("WARNING", "ALARM_ACTIVE", now.AddSeconds(-18));
            AddStateTransition("ALARM_ACTIVE", "ALARM_CLEARING", now.AddSeconds(-12));
            AddStateTransition("ALARM_CLEARING", "NO_ALARM", now.AddSeconds(-7));
        }
        
        private void InitializeTimelineView()
        {
            // Show timeline view by default
            SimpleChartViewer.Visibility = Visibility.Collapsed;
            TimelineContainer.Visibility = Visibility.Visible;
            UpdateTimeline();
            _continuousUpdateTimer?.Start(); // Start continuous updates if timer is initialized
        }
        
        private void ContinuousUpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Update timeline continuously for smooth movement
            if (TimelineContainer?.Visibility == Visibility.Visible)
            {
                UpdateTimelineContinuous();
            }
        }
        
        private void InitializeSimpleCharts()
        {
            // Initialize simple chart view
            UpdateSimpleCharts();
        }
        
        public void AddStateTransition(string fromState, string toState, DateTime timestamp)
        {
            // Track the transition event for timeline
            _stateTransitions.Add(new StateTransitionEvent
            {
                FromState = fromState,
                ToState = toState,
                Timestamp = timestamp
            });
            
            // Trim old data for performance
            TrimOldData();
            
            // Update time spent in the previous state
            if (_lastStateChangeTime.HasValue && !string.IsNullOrEmpty(_currentState))
            {
                var timeSpent = (timestamp - _lastStateChangeTime.Value).TotalSeconds;
                
                if (!_stateTimeData.ContainsKey(_currentState))
                {
                    _stateTimeData[_currentState] = new StateTimeData
                    {
                        StateName = _currentState,
                        TotalSeconds = 0,
                        TransitionCount = 0,
                        Color = GetStateColor(_currentState)
                    };
                }
                
                _stateTimeData[_currentState].TotalSeconds += timeSpent;
                _stateTimeData[_currentState].TransitionCount++;
            }
            
            // Update current state
            _currentState = toState;
            _lastStateChangeTime = timestamp;
            CurrentStateText.Text = toState;
            
            // Update charts and statistics
            UpdateSimpleCharts();
            UpdateStatistics();
        }
        
        private void UpdateSimpleCharts()
        {
            try
            {
                if (SimpleChartPanel == null) return;
                
                // Clear existing chart elements (keep title)
                var elementsToRemove = SimpleChartPanel.Children.OfType<FrameworkElement>()
                    .Where(e => e.Tag?.ToString() == "chart_element").ToList();
                
                foreach (var element in elementsToRemove)
                {
                    SimpleChartPanel.Children.Remove(element);
                }
                
                if (_stateTimeData.Count == 0) return;
                
                double totalTime = _stateTimeData.Values.Sum(s => s.TotalSeconds);
                if (totalTime <= 0) return;
                
                // Create simple bar visualization
                foreach (var stateData in _stateTimeData.Values.OrderByDescending(s => s.TotalSeconds))
                {
                    if (stateData.TotalSeconds <= 0) continue;
                    
                    var percentage = (stateData.TotalSeconds / totalTime) * 100;
                    
                    // State info container
                    var stateContainer = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                        Margin = new Thickness(0, 5, 0, 5),
                        Padding = new Thickness(10),
                        CornerRadius = new CornerRadius(5),
                        Tag = "chart_element"
                    };
                    
                    var statePanel = new StackPanel();
                    
                    // State name and time
                    var headerPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal
                    };
                    
                    var stateNameText = new TextBlock
                    {
                        Text = stateData.StateName,
                        FontWeight = FontWeights.Bold,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(stateData.Color),
                        Margin = new Thickness(0, 0, 20, 0)
                    };
                    
                    var timeText = new TextBlock
                    {
                        Text = $"{stateData.TotalSeconds:F1}s ({percentage:F1}%)",
                        Foreground = Brushes.White,
                        FontSize = 12
                    };
                    
                    headerPanel.Children.Add(stateNameText);
                    headerPanel.Children.Add(timeText);
                    
                    // Progress bar
                    var progressBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                        Height = 8,
                        Margin = new Thickness(0, 5, 0, 0),
                        CornerRadius = new CornerRadius(4)
                    };
                    
                    var progressBar = new Border
                    {
                        Background = new SolidColorBrush(stateData.Color),
                        Height = 8,
                        Width = (percentage / 100) * 300, // Max width of 300px
                        HorizontalAlignment = HorizontalAlignment.Left,
                        CornerRadius = new CornerRadius(4)
                    };
                    
                    var progressGrid = new Grid();
                    progressGrid.Children.Add(progressBorder);
                    progressGrid.Children.Add(progressBar);
                    
                    statePanel.Children.Add(headerPanel);
                    statePanel.Children.Add(progressGrid);
                    stateContainer.Child = statePanel;
                    
                    SimpleChartPanel.Children.Add(stateContainer);
                }
                
                // Update Timeline (if visible)
                if (TimelineContainer?.Visibility == Visibility.Visible)
                {
                    UpdateTimeline();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating simple charts: {ex.Message}");
            }
        }
        
        private void UpdateTimelineContinuous()
        {
            // Lightweight update for continuous movement - no throttling
            UpdateTimelineCore(false);
        }
        
        private void UpdateTimeline()
        {
            // Full update with throttling for data changes
            UpdateTimelineCore(true);
        }
        
        private void UpdateTimelineCore(bool throttle)
        {
            // Performance throttling - only for non-continuous updates
            if (throttle)
            {
                var now = DateTime.Now;
                if ((now - _lastUpdateTime).TotalSeconds < UPDATE_THROTTLE_SECONDS)
                {
                    return; // Skip update to maintain performance
                }
                _lastUpdateTime = now;
            }
            
            TimelineCanvas.Children.Clear();
            if (StateHeaderCanvas != null)
                StateHeaderCanvas.Children.Clear();
            
            if (_stateTransitions.Count == 0) return;
            
            // Get all unique states
            var allStates = _stateTransitions.SelectMany(t => new[] { t.FromState, t.ToState })
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();
            
            if (allStates.Count == 0) return;
            
            // Calculate time range - use current time as max for continuous movement
            var currentTime = DateTime.Now;
            var minTime = _stateTransitions.Min(t => t.Timestamp);
            var maxTime = currentTime; // Always use current time for continuous movement
            var timeSpan = maxTime - minTime;
            
            if (timeSpan.TotalSeconds < 1) timeSpan = TimeSpan.FromSeconds(10); // Minimum span
            
            // Canvas dimensions - make width dynamic based on time span
            double leftMargin = 180;  // Even more space for state names at left
            double rightMargin = 50;
            double topMargin = 30;  // Less top margin
            double bottomMargin = 10;  // Minimal bottom margin since no x-axis
            double pixelsPerSecond = 200; // 200 pixels per second for higher horizontal resolution
            double canvasWidth = Math.Max(4000, leftMargin + rightMargin + (timeSpan.TotalSeconds * pixelsPerSecond));
            double canvasHeight = Math.Max(600, allStates.Count * 50 + 80);
            
            TimelineCanvas.Width = canvasWidth;
            TimelineCanvas.Height = canvasHeight;
            
            // Set the same height for header canvas
            if (StateHeaderCanvas != null)
            {
                StateHeaderCanvas.Height = canvasHeight;
            }
            
            // Enable anti-aliasing and edge smoothing for less jitter
            RenderOptions.SetEdgeMode(TimelineCanvas, EdgeMode.Unspecified);
            RenderOptions.SetBitmapScalingMode(TimelineCanvas, BitmapScalingMode.HighQuality);
            
            double plotWidth = canvasWidth - leftMargin - rightMargin;
            double plotHeight = canvasHeight - topMargin - bottomMargin;
            double stateSpacing = plotHeight / Math.Max(1, allStates.Count - 1);
            
            // Draw vertical start line at the beginning of timeline canvas
            var startLine = new Line
            {
                X1 = 0, Y1 = topMargin,  // Start from left edge of timeline
                X2 = 0, Y2 = canvasHeight - bottomMargin,
                Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                StrokeThickness = 1
            };
            TimelineCanvas.Children.Add(startLine);
            
            // Draw state labels in the separate header canvas
            for (int i = 0; i < allStates.Count; i++)
            {
                double y = topMargin + (i * stateSpacing);
                
                // Draw state labels in header canvas if it exists
                if (StateHeaderCanvas != null)
                {
                    // Truncate state name if too long to prevent overflow
                    string displayName = allStates[i].Length > 20 ? allStates[i].Substring(0, 17) + "..." : allStates[i];
                    
                    var stateLabel = new TextBlock
                    {
                        Text = displayName,
                        Foreground = new SolidColorBrush(GetStateColor(allStates[i])),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        TextAlignment = TextAlignment.Right,
                        ClipToBounds = true,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Canvas.SetLeft(stateLabel, 10);
                    Canvas.SetTop(stateLabel, y - 8);
                    stateLabel.Width = 180;  // Fixed width within header
                    stateLabel.MaxWidth = 180;
                    StateHeaderCanvas.Children.Add(stateLabel);
                }
                
                // Subtle horizontal guide line in main canvas (starting from 0, not leftMargin)
                var guideLine = new Line
                {
                    X1 = 0, Y1 = y,  // Start from left edge of timeline canvas
                    X2 = canvasWidth - rightMargin, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    StrokeDashArray = new DoubleCollection { 3, 5 },
                    StrokeThickness = 0.3
                };
                TimelineCanvas.Children.Add(guideLine);
            }
            
            // Show current time indicator at the right edge
            if (_stateTransitions.Count > 0)
            {
                // Current time vertical line
                var currentTimeLine = new Line
                {
                    X1 = canvasWidth - rightMargin,
                    Y1 = topMargin,
                    X2 = canvasWidth - rightMargin,
                    Y2 = canvasHeight - bottomMargin,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                TimelineCanvas.Children.Add(currentTimeLine);
                
                // Current time label
                var currentTimeLabel = new TextBlock
                {
                    Text = $"NOW {currentTime:HH:mm:ss}",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(currentTimeLabel, canvasWidth - rightMargin - 35);
                Canvas.SetTop(currentTimeLabel, 5);
                TimelineCanvas.Children.Add(currentTimeLabel);
            }
            
            // Draw state transitions as dots and lines
            string currentState = "";
            DateTime lastTime = minTime;
            
            foreach (var transition in _stateTransitions.OrderBy(t => t.Timestamp))
            {
                // Calculate position (no left margin in timeline canvas)
                double x = ((transition.Timestamp - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;
                
                if (!string.IsNullOrEmpty(currentState))
                {
                    // Draw line showing state duration
                    int currentStateIndex = allStates.IndexOf(currentState);
                    if (currentStateIndex >= 0)
                    {
                        double stateY = topMargin + (currentStateIndex * stateSpacing);
                        
                        // Horizontal line showing time in state (adjusted for no left margin in timeline)
                        double lastX = ((lastTime - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;
                        var stateLine = new Line
                        {
                            X1 = lastX, Y1 = stateY,
                            X2 = x - leftMargin, Y2 = stateY,  // Adjust X position
                            Stroke = new SolidColorBrush(GetStateColor(currentState)),
                            StrokeThickness = 4
                        };
                        TimelineCanvas.Children.Add(stateLine);
                        
                        // Add white transition line
                        int newStateIndex = allStates.IndexOf(transition.ToState);
                        if (newStateIndex >= 0 && newStateIndex != currentStateIndex)
                        {
                            double newStateY = topMargin + (newStateIndex * stateSpacing);
                            var transitionLine = new Line
                            {
                                X1 = x, Y1 = stateY,
                                X2 = x, Y2 = newStateY,
                                Stroke = Brushes.White,
                                StrokeThickness = 1
                            };
                            TimelineCanvas.Children.Add(transitionLine);
                        }
                    }
                }
                
                currentState = transition.ToState;
                lastTime = transition.Timestamp;
            }
            
            // Extend current state to current time for continuous movement
            if (!string.IsNullOrEmpty(currentState) && _lastStateChangeTime.HasValue)
            {
                int currentStateIndex = allStates.IndexOf(currentState);
                if (currentStateIndex >= 0)
                {
                    double stateY = topMargin + (currentStateIndex * stateSpacing);
                    double lastX = leftMargin + ((lastTime - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;
                    double nowX = leftMargin + ((currentTime - minTime).TotalSeconds / timeSpan.TotalSeconds) * plotWidth;
                    
                    // Draw current state line with slight animation effect
                    var currentStateLine = new Line
                    {
                        X1 = lastX, Y1 = stateY,
                        X2 = nowX, Y2 = stateY,
                        Stroke = new SolidColorBrush(GetStateColor(currentState)),
                        StrokeThickness = 4,
                        Opacity = 0.95  // Slight transparency for active state
                    };
                    TimelineCanvas.Children.Add(currentStateLine);
                    
                    // Add pulsing dot at the end of current state
                    var currentDot = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = new SolidColorBrush(GetStateColor(currentState))
                    };
                    Canvas.SetLeft(currentDot, nowX - 4);
                    Canvas.SetTop(currentDot, stateY - 4);
                    TimelineCanvas.Children.Add(currentDot);
                }
            }
            
            // Remove x-axis label completely to avoid number chart appearance
            // Keep only minimal labeling
            
            // Auto-scroll to show current time (rightmost part of timeline)
            if (TimelineScroller != null)
            {
                TimelineScroller.ScrollToRightEnd();
            }
        }
        
        private void UpdateStatistics()
        {
            // Update total time
            double totalTime = _stateTimeData.Values.Sum(s => s.TotalSeconds);
            
            // Format time display
            string timeDisplay;
            if (totalTime < 60)
            {
                timeDisplay = $"{totalTime:F1} seconds";
            }
            else if (totalTime < 3600)
            {
                var minutes = (int)(totalTime / 60);
                var seconds = (int)(totalTime % 60);
                timeDisplay = $"{minutes}m {seconds}s";
            }
            else
            {
                var hours = (int)(totalTime / 3600);
                var minutes = (int)((totalTime % 3600) / 60);
                timeDisplay = $"{hours}h {minutes}m";
            }
            
            // Only update if changed
            if (TotalTimeText.Text != timeDisplay)
                TotalTimeText.Text = timeDisplay;
            
            // Update transition count
            int totalTransitions = _stateTimeData.Values.Sum(s => s.TransitionCount);
            var transitionText = totalTransitions.ToString();
            if (TransitionCountText.Text != transitionText)
                TransitionCountText.Text = transitionText;
            
            // Update existing breakdown items in-place instead of recreating
            if (totalTime > 0)
            {
                var orderedStates = _stateTimeData.Values.OrderByDescending(s => s.TotalSeconds).ToList();
                
                // Update existing items or add new ones
                for (int i = 0; i < orderedStates.Count; i++)
                {
                    var stateData = orderedStates[i];
                    double percentage = (stateData.TotalSeconds / totalTime) * 100;
                    
                    // Format time spent in state
                    string timeInState;
                    if (stateData.TotalSeconds < 60)
                    {
                        timeInState = $"{stateData.TotalSeconds:F1}s";
                    }
                    else if (stateData.TotalSeconds < 3600)
                    {
                        var minutes = (int)(stateData.TotalSeconds / 60);
                        var seconds = (int)(stateData.TotalSeconds % 60);
                        timeInState = $"{minutes}m {seconds}s";
                    }
                    else
                    {
                        var hours = (int)(stateData.TotalSeconds / 3600);
                        var minutes = (int)((stateData.TotalSeconds % 3600) / 60);
                        timeInState = $"{hours}h {minutes}m";
                    }
                    
                    // Find existing item or create new
                    var existingItem = _stateBreakdownItems.FirstOrDefault(x => x.StateName == stateData.StateName);
                    if (existingItem != null)
                    {
                        // Update existing item properties
                        existingItem.Percentage = $"{percentage:F1}%";
                        existingItem.TimeSpent = timeInState;
                        existingItem.TransitionCount = stateData.TransitionCount;
                        existingItem.PercentageValue = percentage;
                    }
                    else
                    {
                        // Add new item only if it doesn't exist
                        _stateBreakdownItems.Add(new StateBreakdownItem
                        {
                            StateName = stateData.StateName,
                            Percentage = $"{percentage:F1}%",
                            TimeSpent = timeInState,
                            TransitionCount = stateData.TransitionCount,
                            PercentageValue = percentage,
                            StateColor = new SolidColorBrush(stateData.Color)
                        });
                    }
                }
                
                // Remove items that no longer exist
                var statesToRemove = _stateBreakdownItems
                    .Where(item => !_stateTimeData.ContainsKey(item.StateName))
                    .ToList();
                
                foreach (var item in statesToRemove)
                {
                    _stateBreakdownItems.Remove(item);
                }
            }
            
            // Update last update time
            LastUpdateText.Text = $"Last Update: {DateTime.Now:HH:mm:ss}";
        }
        
        private void TrimOldData()
        {
            // Primary policy: Keep only the most recent MAX_TRANSITION_HISTORY items
            if (_stateTransitions.Count > MAX_TRANSITION_HISTORY)
            {
                // Sort by timestamp and keep only the newest items
                var sortedTransitions = _stateTransitions.OrderBy(t => t.Timestamp).ToList();
                var itemsToRemove = sortedTransitions.Take(_stateTransitions.Count - MAX_TRANSITION_HISTORY).ToList();
                
                foreach (var transition in itemsToRemove)
                {
                    _stateTransitions.Remove(transition);
                }
            }
        }
        
        private Color GetStateColor(string stateName)
        {
            // Use vibrant colors, avoid gray/black/white
            return stateName switch
            {
                "OFFLINE" => Color.FromRgb(180, 50, 50),        // Dark red
                "ONLINE" => Color.FromRgb(0, 255, 0),           // Bright green
                "INITIALIZING" => Color.FromRgb(255, 255, 0),   // Yellow
                "PROCESSING" => Color.FromRgb(0, 150, 255),     // Blue
                "ERROR" => Color.FromRgb(255, 0, 0),            // Red
                "IDLE" => Color.FromRgb(100, 200, 100),         // Light green
                "READY" => Color.FromRgb(0, 200, 150),          // Teal
                "SETUP" => Color.FromRgb(255, 165, 0),          // Orange
                "EXECUTING" => Color.FromRgb(147, 112, 219),    // Purple
                "CLEANUP" => Color.FromRgb(255, 192, 203),      // Pink
                "MOVING" => Color.FromRgb(64, 224, 208),        // Turquoise
                "LOADING" => Color.FromRgb(255, 215, 0),        // Gold
                "WAITING" => Color.FromRgb(255, 140, 0),        // Dark orange
                "VERIFYING" => Color.FromRgb(135, 206, 235),    // Sky blue
                "COMPLETE" => Color.FromRgb(50, 205, 50),       // Lime green
                _ => Color.FromRgb(138, 43, 226)                // Blue violet
            };
        }
        
        private void ChartTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SimpleChartViewer == null || TimelineContainer == null || ChartTypeCombo == null)
                return;
                
            try
            {
                switch (ChartTypeCombo.SelectedIndex)
                {
                    case 0: // Bar View
                        SimpleChartViewer.Visibility = Visibility.Visible;
                        TimelineContainer.Visibility = Visibility.Collapsed;
                        _continuousUpdateTimer.Stop(); // Stop continuous updates
                        UpdateSimpleCharts();
                        break;
                        
                    case 1: // Timeline
                        SimpleChartViewer.Visibility = Visibility.Collapsed;
                        TimelineContainer.Visibility = Visibility.Visible;
                        UpdateTimeline();
                        _continuousUpdateTimer.Start(); // Start continuous updates
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error changing chart type: {ex.Message}");
            }
        }
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSimpleCharts();
            UpdateStatistics();
            StatusText.Text = "Refreshed";
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void TimelineScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Sync the header scroll position with the timeline vertical scroll
            if (HeaderScroller != null && e.VerticalChange != 0)
            {
                HeaderScroller.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }
        
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _continuousUpdateTimer?.Stop(); // Clean up timer
        }
    }
    
    public class StateTimeData
    {
        public string StateName { get; set; } = "";
        public double TotalSeconds { get; set; }
        public int TransitionCount { get; set; }
        public Color Color { get; set; }
    }
    
    public class StateBreakdownItem : INotifyPropertyChanged
    {
        private string _stateName = "";
        private string _percentage = "";
        private string _timeSpent = "";
        private int _transitionCount;
        private double _percentageValue;
        private Brush _stateColor = Brushes.White;
        
        public string StateName
        {
            get => _stateName;
            set { _stateName = value; OnPropertyChanged(); }
        }
        
        public string Percentage
        {
            get => _percentage;
            set { _percentage = value; OnPropertyChanged(); }
        }
        
        public string TimeSpent
        {
            get => _timeSpent;
            set { _timeSpent = value; OnPropertyChanged(); }
        }
        
        public int TransitionCount
        {
            get => _transitionCount;
            set { _transitionCount = value; OnPropertyChanged(); }
        }
        
        public double PercentageValue
        {
            get => _percentageValue;
            set { _percentageValue = value; OnPropertyChanged(); }
        }
        
        public Brush StateColor
        {
            get => _stateColor;
            set { _stateColor = value; OnPropertyChanged(); }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class StateTransitionEvent
    {
        public string FromState { get; set; } = "";
        public string ToState { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}