using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using XStateNet;

namespace SemiStandard.Simulator.Wpf;

public partial class UmlTimingDiagramWindow : Window
{
    private readonly DispatcherTimer _updateTimer;
    private readonly Dictionary<string, SwimmingLane> _lanes = new();
    private readonly List<StateTransition> _transitions = new();
    private double _pixelsPerSecond = 20;
    private double _timeWindowSeconds = 120; // Increased for better visibility
    private DateTime _startTime;
    private double _laneHeight = 80;
    private double _currentTimePosition = 0;
    private double _elapsedSeconds = 0;
    
    private class SwimmingLane
    {
        public string MachineName { get; set; } = "";
        public string CurrentState { get; set; } = "";
        public double YPosition { get; set; }
        public List<StateSegment> Segments { get; set; } = new();
        public Border HeaderControl { get; set; } = null!;
        public Canvas LaneCanvas { get; set; } = null!;
        public Dictionary<string, double> StateYLevels { get; set; } = new();
        public string[] PossibleStates { get; set; } = Array.Empty<string>();
    }
    
    private class StateSegment
    {
        public string State { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double StartX { get; set; }
        public double EndX { get; set; }
        public double YLevel { get; set; }
    }
    
    private class StateTransition
    {
        public string MachineName { get; set; } = "";
        public string FromState { get; set; } = "";
        public string ToState { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
    
    public UmlTimingDiagramWindow(Dictionary<string, StateMachine>? stateMachines = null)
    {
        InitializeComponent();
        
        System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Constructor called with stateMachines: {stateMachines?.Count ?? 0} machines");
        
        _startTime = DateTime.Now;
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
        
        InitializeDiagram();
        SetupEventHandlers();
        
        // Connect to real state machines if provided, otherwise generate sample data
        if (stateMachines != null && stateMachines.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Connecting to {stateMachines.Count} real state machines");
            ConnectToStateMachines(stateMachines);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[UML_TIMING] No state machines provided - using sample data");
            GenerateSampleData();
        }
        
        DrawAllWaveforms();
        
        _updateTimer.Start();
    }
    
    private Dictionary<string, string[]> GetStateMachineStates()
    {
        var stateMachineDefinitions = new Dictionary<string, string[]>();
        var scripts = StateMachineDefinitions.GetStateMachineScripts();
        
        foreach (var kvp in scripts)
        {
            var machineName = kvp.Key;
            var scriptJson = kvp.Value;
            
            try
            {
                // Parse the XState script to extract state names
                var states = ExtractStatesFromScript(scriptJson);
                stateMachineDefinitions[machineName] = states;
            }
            catch
            {
                // Fallback to some basic states if parsing fails
                stateMachineDefinitions[machineName] = new[] { "Unknown" };
            }
        }
        
        return stateMachineDefinitions;
    }
    
    private string[] ExtractStatesFromScript(string scriptJson)
    {
        try
        {
            // Convert single quotes to double quotes for valid JSON
            var validJson = scriptJson.Replace('\'', '"');
            
            using var doc = JsonDocument.Parse(validJson);
            var root = doc.RootElement;
            
            var states = new List<string>();
            
            if (root.TryGetProperty("states", out var statesElement))
            {
                foreach (var state in statesElement.EnumerateObject())
                {
                    states.Add(state.Name);
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Extracted {states.Count} states: {string.Join(", ", states)}");
            return states.ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Failed to extract states: {ex.Message}");
            return new[] { "Unknown" };
        }
    }
    
    private void InitializeDiagram()
    {
        // Get state machine definitions from StateMachineDefinitions class
        var stateMachineDefinitions = GetStateMachineStates();
        
        double yPos = 10;
        foreach (var kvp in stateMachineDefinitions)
        {
            var machineName = kvp.Key;
            var states = kvp.Value;
            
            var lane = new SwimmingLane
            {
                MachineName = machineName,
                CurrentState = states[0],
                YPosition = yPos,
                PossibleStates = states
            };
            
            // Calculate Y levels for each state (evenly distributed in lane height)
            double stateSpacing = (_laneHeight - 20) / (states.Length + 1);
            for (int i = 0; i < states.Length; i++)
            {
                lane.StateYLevels[states[i]] = yPos + 15 + (i * stateSpacing);
            }
            
            // Create header in fixed lane area
            var header = CreateLaneHeader(machineName, yPos, states);
            LaneHeaderCanvas.Children.Add(header);
            lane.HeaderControl = header;
            
            // Create a dedicated canvas for this lane's waveform
            var laneCanvas = new Canvas
            {
                Height = _laneHeight,
                Width = _timeWindowSeconds * _pixelsPerSecond,
                ClipToBounds = true
            };
            Canvas.SetTop(laneCanvas, yPos);
            DiagramCanvas.Children.Add(laneCanvas);
            lane.LaneCanvas = laneCanvas;
            
            _lanes[machineName] = lane;
            
            // Create initial segment for each lane
            var initialSegment = new StateSegment
            {
                State = states[0],
                StartTime = DateTime.Now,
                StartX = 0,
                YLevel = lane.StateYLevels[states[0]],
                EndTime = null
            };
            lane.Segments.Add(initialSegment);
            
            yPos += _laneHeight;
        }
        
        // Set canvas sizes (11 machines * 80px = 880px + margins)
        DiagramCanvas.Height = yPos + 20;
        LaneHeaderCanvas.Height = yPos + 20;
        
        // Log the total height for debugging
        System.Diagnostics.Debug.WriteLine($"Total canvas height: {DiagramCanvas.Height}px for {_lanes.Count} state machines");
        DiagramCanvas.Width = _timeWindowSeconds * _pixelsPerSecond;
        TimeAxisCanvas.Width = DiagramCanvas.Width;
        
        // Draw initial grid and axis
        DrawGrid();
        DrawTimeAxis();
        DrawAllWaveforms();
        
        UpdateStatusBar();
    }
    
    private Border CreateLaneHeader(string machineName, double yPosition, string[] states)
    {
        var border = new Border
        {
            Width = 240,
            Height = _laneHeight - 2,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(50, 50, 50), 0),
                    new GradientStop(Color.FromRgb(40, 40, 40), 1)
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(0, 0, 2, 1),
            Margin = new Thickness(5, yPosition, 0, 0)
        };
        
        var grid = new Grid();
        
        // Machine name at top
        var nameText = new TextBlock
        {
            Text = machineName,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = GetMachineBrush(machineName),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 5, 0, 0)
        };
        grid.Children.Add(nameText);
        
        // State levels on the right side
        for (int i = 0; i < states.Length; i++)
        {
            double yLevel = 15 + (i * ((_laneHeight - 20) / (states.Length + 1)));
            
            var stateLabel = new TextBlock
            {
                Text = states[i],
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, yLevel - 5, 10, 0)
            };
            grid.Children.Add(stateLabel);
            
            // Draw a small line indicator
            var line = new Line
            {
                X1 = 200,
                Y1 = yLevel,
                X2 = 230,
                Y2 = yLevel,
                Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            grid.Children.Add(line);
        }
        
        // Current state indicator at bottom
        var currentStateText = new TextBlock
        {
            Text = "► " + states[0],
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 100)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 5),
            Tag = $"{machineName}_CurrentState"
        };
        grid.Children.Add(currentStateText);
        
        border.Child = grid;
        return border;
    }
    
    private void DrawGrid()
    {
        if (!ShowGridCheck.IsChecked == true)
        {
            // Remove existing grid lines
            var gridLines = DiagramCanvas.Children.OfType<Line>()
                .Where(l => l.Tag?.ToString() == "GridLine").ToList();
            foreach (var line in gridLines)
            {
                DiagramCanvas.Children.Remove(line);
            }
            return;
        }
        
        // Clear existing grid lines first
        var existingGridLines = DiagramCanvas.Children.OfType<Line>()
            .Where(l => l.Tag?.ToString() == "GridLine").ToList();
        foreach (var line in existingGridLines)
        {
            DiagramCanvas.Children.Remove(line);
        }
        
        // Vertical grid lines (time markers) - every 5 seconds
        for (double x = 0; x <= DiagramCanvas.Width; x += _pixelsPerSecond * 5)
        {
            var line = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = DiagramCanvas.Height,
                Stroke = new SolidColorBrush(Color.FromArgb(20, 100, 100, 100)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 4 },
                Tag = "GridLine"
            };
            DiagramCanvas.Children.Add(line);
        }
        
        // Horizontal grid lines (lane separators)
        foreach (var lane in _lanes.Values)
        {
            var line = new Line
            {
                X1 = 0,
                Y1 = lane.YPosition + _laneHeight,
                X2 = DiagramCanvas.Width,
                Y2 = lane.YPosition + _laneHeight,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 100, 100, 100)),
                StrokeThickness = 1,
                Tag = "GridLine"
            };
            DiagramCanvas.Children.Add(line);
        }
    }
    
    private void DrawTimeAxis()
    {
        TimeAxisCanvas.Children.Clear();
        TimeAxisCanvas.Width = DiagramCanvas.Width;
        
        // Draw time markers
        for (double x = 0; x <= DiagramCanvas.Width; x += _pixelsPerSecond * 5) // Every 5 seconds
        {
            var time = x / _pixelsPerSecond;
            
            // Major tick
            var tick = new Line
            {
                X1 = x,
                Y1 = 35,
                X2 = x,
                Y2 = 50,
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                StrokeThickness = 1
            };
            TimeAxisCanvas.Children.Add(tick);
            
            // Time label
            var label = new TextBlock
            {
                Text = $"{time:0}s",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                Margin = new Thickness(x - 10, 20, 0, 0)
            };
            TimeAxisCanvas.Children.Add(label);
        }
        
        // Draw minor ticks (every second)
        for (double x = 0; x <= DiagramCanvas.Width; x += _pixelsPerSecond)
        {
            if (x % (_pixelsPerSecond * 5) != 0) // Skip major ticks
            {
                var minorTick = new Line
                {
                    X1 = x,
                    Y1 = 42,
                    Y2 = 50,
                    X2 = x,
                    Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    StrokeThickness = 0.5
                };
                TimeAxisCanvas.Children.Add(minorTick);
            }
        }
        
        // Current time indicator (red line)
        var currentTimeLine = new Line
        {
            X1 = _currentTimePosition,
            Y1 = 0,
            Y2 = 50,
            X2 = _currentTimePosition,
            Stroke = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
            StrokeThickness = 2,
            Tag = "CurrentTime"
        };
        TimeAxisCanvas.Children.Add(currentTimeLine);
        
        // Add current time label
        var currentTimeLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(_currentTimePosition - 20, 2, 0, 0),
            Child = new TextBlock
            {
                Text = $"{_elapsedSeconds:0.0}s",
                FontSize = 10,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            }
        };
        TimeAxisCanvas.Children.Add(currentTimeLabel);
    }
    
    private void DrawAllWaveforms()
    {
        foreach (var lane in _lanes.Values)
        {
            DrawLaneWaveform(lane);
        }
        
        // Draw current time line across all lanes
        DrawCurrentTimeLine();
    }
    
    private void DrawLaneWaveform(SwimmingLane lane)
    {
        lane.LaneCanvas.Children.Clear();
        
        if (lane.Segments.Count == 0) return;
        
        // Draw the waveform using lines
        for (int i = 0; i < lane.Segments.Count; i++)
        {
            var segment = lane.Segments[i];
            double startX = segment.StartX;
            double endX = segment.EndTime.HasValue 
                ? segment.EndX 
                : _currentTimePosition;
            
            double y = segment.YLevel;
            
            // Horizontal line for state duration
            var horizontalLine = new Line
            {
                X1 = startX,
                Y1 = y,
                X2 = endX,
                Y2 = y,
                Stroke = GetMachineBrush(lane.MachineName),
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            lane.LaneCanvas.Children.Add(horizontalLine);
            
            // Add state label if there's enough space
            if (endX - startX > 30)
            {
                var stateLabel = new TextBlock
                {
                    Text = segment.State,
                    FontSize = 9,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(startX + 3, y - 12, 0, 0)
                };
                lane.LaneCanvas.Children.Add(stateLabel);
            }
            
            // Draw vertical transition line to next state
            if (i < lane.Segments.Count - 1)
            {
                var nextSegment = lane.Segments[i + 1];
                var transitionLine = new Line
                {
                    X1 = endX,
                    Y1 = y,
                    X2 = endX,
                    Y2 = nextSegment.YLevel,
                    Stroke = GetMachineBrush(lane.MachineName),
                    StrokeThickness = 3,
                    StrokeDashArray = new DoubleCollection { 2, 1 }
                };
                lane.LaneCanvas.Children.Add(transitionLine);
                
                // Add transition marker (small circle)
                var marker = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = GetMachineBrush(lane.MachineName),
                    Margin = new Thickness(endX - 3, y - 3, 0, 0)
                };
                lane.LaneCanvas.Children.Add(marker);
            }
        }
        
        // Highlight current state
        if (lane.Segments.Count > 0)
        {
            var currentSegment = lane.Segments.Last();
            if (!currentSegment.EndTime.HasValue)
            {
                var currentMarker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromRgb(0, 255, 100)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Margin = new Thickness(_currentTimePosition - 4, currentSegment.YLevel - 4, 0, 0)
                };
                lane.LaneCanvas.Children.Add(currentMarker);
            }
        }
    }
    
    private void DrawCurrentTimeLine()
    {
        // Remove old current time line
        var oldLines = DiagramCanvas.Children.OfType<Line>()
            .Where(l => l.Tag?.ToString() == "CurrentTimeLine").ToList();
        foreach (var line in oldLines)
        {
            DiagramCanvas.Children.Remove(line);
        }
        
        // Draw new current time line
        var currentLine = new Line
        {
            X1 = _currentTimePosition,
            Y1 = 0,
            X2 = _currentTimePosition,
            Y2 = DiagramCanvas.Height,
            Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 80, 80)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Tag = "CurrentTimeLine"
        };
        DiagramCanvas.Children.Add(currentLine);
    }
    
    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        _elapsedSeconds += 0.1;
        _currentTimePosition = _elapsedSeconds * _pixelsPerSecond;
        
        // Auto-scroll if enabled
        if (AutoScrollCheck.IsChecked == true && 
            _currentTimePosition > DiagramScroller.HorizontalOffset + DiagramScroller.ViewportWidth - 200)
        {
            DiagramScroller.ScrollToHorizontalOffset(_currentTimePosition - DiagramScroller.ViewportWidth + 200);
        }
        
        // Extend canvas if needed
        if (_currentTimePosition > DiagramCanvas.Width - 100)
        {
            DiagramCanvas.Width += 500;
            TimeAxisCanvas.Width = DiagramCanvas.Width;
            foreach (var lane in _lanes.Values)
            {
                lane.LaneCanvas.Width = DiagramCanvas.Width;
            }
            DrawGrid();
        }
        
        // Disabled - we get real state changes from the simulator
        // if (_elapsedSeconds > 2 && Random.Shared.Next(100) < 5) // 5% chance per tick
        // {
        //     GenerateRandomStateChange();
        // }
        
        DrawTimeAxis();
        DrawAllWaveforms();
        DrawCurrentTimeLine();
        UpdateStatusBar();
    }
    
    private void GenerateRandomStateChange()
    {
        var lanesList = _lanes.Values.ToList();
        var lane = lanesList[Random.Shared.Next(lanesList.Count)];
        
        // Get current state
        var currentSegment = lane.Segments.LastOrDefault();
        if (currentSegment != null && !currentSegment.EndTime.HasValue)
        {
            var currentStateIndex = Array.IndexOf(lane.PossibleStates, currentSegment.State);
            var newStateIndex = (currentStateIndex + 1 + Random.Shared.Next(lane.PossibleStates.Length - 1)) 
                                % lane.PossibleStates.Length;
            var newState = lane.PossibleStates[newStateIndex];
            
            // End current segment
            currentSegment.EndTime = DateTime.Now;
            currentSegment.EndX = _currentTimePosition;
            
            // Start new segment
            var newSegment = new StateSegment
            {
                State = newState,
                StartTime = DateTime.Now,
                StartX = _currentTimePosition,
                YLevel = lane.StateYLevels[newState],
                EndTime = null
            };
            lane.Segments.Add(newSegment);
            lane.CurrentState = newState;
            
            // Update header
            UpdateLaneHeader(lane);
            
            // Record transition
            _transitions.Add(new StateTransition
            {
                MachineName = lane.MachineName,
                FromState = currentSegment.State,
                ToState = newState,
                Timestamp = DateTime.Now
            });
        }
    }
    
    private void ConnectToStateMachines(Dictionary<string, StateMachine> stateMachines)
    {
        System.Diagnostics.Debug.WriteLine($"[UML_TIMING] ConnectToStateMachines called with {stateMachines.Count} machines");
        System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Available lanes: {string.Join(", ", _lanes.Keys)}");
        
        foreach (var kvp in stateMachines)
        {
            var machineName = kvp.Key;
            var machine = kvp.Value;
            
            System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Processing machine: {machineName}");
            
            if (_lanes.TryGetValue(machineName, out var lane))
            {
                System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Found lane for {machineName}");
                // Set initial current state from the machine
                var currentStateName = machine.GetActiveStateString();
                lane.CurrentState = currentStateName;
                UpdateLaneHeader(lane);
                
                // Create initial segment for current state
                if (!lane.StateYLevels.ContainsKey(currentStateName))
                {
                    // Add unknown state to the lane if not exists
                    var states = lane.PossibleStates.ToList();
                    states.Add(currentStateName);
                    lane.PossibleStates = states.ToArray();
                    
                    // Recalculate Y levels
                    double stateSpacing = (_laneHeight - 20) / (states.Count + 1);
                    for (int i = 0; i < states.Count; i++)
                    {
                        lane.StateYLevels[states[i]] = lane.YPosition + 15 + (i * stateSpacing);
                    }
                }
                
                var initialSegment = new StateSegment
                {
                    State = currentStateName,
                    StartTime = _startTime,
                    StartX = 0,
                    YLevel = lane.StateYLevels[currentStateName],
                    EndTime = null
                };
                lane.Segments.Add(initialSegment);
                
                // Subscribe to state machine transitions
                machine.OnTransition += (fromState, toState, eventName) =>
                {
                    
                    // Add transition to timing diagram
                    var currentStateName = machine.GetActiveStateString();
                    
                    // Use Dispatcher.Invoke to ensure UI updates happen on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"[UML_TIMING] Transition: {machineName} from {fromState?.Name ?? "unknown"} to {currentStateName} via {eventName}");
                        AddStateTransition(machineName, fromState?.Name ?? "unknown", currentStateName, DateTime.Now);
                    });
                };
            }
        }
    }
    
    private void GenerateSampleData()
    {
        var now = DateTime.Now;
        
        // Generate initial state history for each lane
        foreach (var lane in _lanes.Values)
        {
            var states = lane.PossibleStates;
            var currentState = states[0];
            var time = now.AddSeconds(-50);
            
            // Create initial segment
            var segment = new StateSegment
            {
                State = currentState,
                StartTime = time,
                StartX = 0,
                YLevel = lane.StateYLevels[currentState]
            };
            lane.Segments.Add(segment);
            
            // Generate some historical transitions
            for (int i = 0; i < 5; i++)
            {
                time = time.AddSeconds(Random.Shared.Next(5, 15));
                if (time >= now) break;
                
                var newStateIndex = Random.Shared.Next(states.Length);
                var newState = states[newStateIndex];
                
                // End current segment
                segment.EndTime = time;
                segment.EndX = (time - _startTime).TotalSeconds * _pixelsPerSecond;
                
                // Create new segment
                segment = new StateSegment
                {
                    State = newState,
                    StartTime = time,
                    StartX = segment.EndX,
                    YLevel = lane.StateYLevels[newState]
                };
                lane.Segments.Add(segment);
                
                // Record transition
                _transitions.Add(new StateTransition
                {
                    MachineName = lane.MachineName,
                    FromState = currentState,
                    ToState = newState,
                    Timestamp = time
                });
                
                currentState = newState;
            }
            
            lane.CurrentState = currentState;
            UpdateLaneHeader(lane);
        }
    }
    
    private void UpdateLaneHeader(SwimmingLane lane)
    {
        if (lane.HeaderControl?.Child is Grid grid)
        {
            var currentStateLabel = grid.Children.OfType<TextBlock>()
                .FirstOrDefault(t => t.Tag?.ToString() == $"{lane.MachineName}_CurrentState");
            if (currentStateLabel != null)
            {
                currentStateLabel.Text = "► " + lane.CurrentState;
            }
        }
    }
    
    private Brush GetMachineBrush(string machineName)
    {
        return machineName switch
        {
            "EquipmentController" => new SolidColorBrush(Color.FromRgb(135, 206, 235)), // Sky blue
            "TransportHandler" => new SolidColorBrush(Color.FromRgb(255, 165, 0)),     // Orange
            "ProcessManager" => new SolidColorBrush(Color.FromRgb(50, 205, 50)),       // Lime green
            "RecipeExecutor" => new SolidColorBrush(Color.FromRgb(147, 112, 219)),     // Medium purple
            "E30GEM" => new SolidColorBrush(Color.FromRgb(100, 200, 100)),
            "E87Carrier" => new SolidColorBrush(Color.FromRgb(100, 150, 250)),
            "E94ControlJob" => new SolidColorBrush(Color.FromRgb(250, 150, 100)),
            "E37HSMSSession" => new SolidColorBrush(Color.FromRgb(250, 100, 200)),
            "ProcessControl" => new SolidColorBrush(Color.FromRgb(150, 250, 150)),
            "MaterialHandling" => new SolidColorBrush(Color.FromRgb(250, 250, 100)),
            "AlarmManager" => new SolidColorBrush(Color.FromRgb(250, 100, 100)),
            _ => new SolidColorBrush(Color.FromRgb(180, 180, 180))
        };
    }
    
    private void SetupEventHandlers()
    {
        ZoomSlider.ValueChanged += (s, e) =>
        {
            var scale = ZoomSlider.Value;
            DiagramScale.ScaleX = scale;
            DiagramScale.ScaleY = scale;
            ZoomText.Text = $"{scale * 100:0}%";
        };
        
        ShowGridCheck.Checked += (s, e) => DrawGrid();
        ShowGridCheck.Unchecked += (s, e) => DrawGrid();
    }
    
    private void DiagramScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync lane headers with vertical scroll
        LaneHeaderScroller.ScrollToVerticalOffset(e.VerticalOffset);
        
        // Sync time axis with horizontal scroll  
        TimeAxisScroller.ScrollToHorizontalOffset(e.HorizontalOffset);
    }
    
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // Clear all data
        foreach (var lane in _lanes.Values)
        {
            lane.Segments.Clear();
            lane.LaneCanvas.Children.Clear();
            
            // Re-initialize with initial segment at current state
            var initialSegment = new StateSegment
            {
                State = lane.CurrentState,
                StartTime = DateTime.Now,
                StartX = 0,
                YLevel = lane.StateYLevels.ContainsKey(lane.CurrentState) 
                    ? lane.StateYLevels[lane.CurrentState] 
                    : lane.StateYLevels.Values.First(),
                EndTime = null
            };
            lane.Segments.Add(initialSegment);
        }
        _transitions.Clear();
        _elapsedSeconds = 0;
        _currentTimePosition = 0;
        
        // Clear and redraw grid, axis and waveforms
        DrawGrid();
        DrawTimeAxis();
        DrawAllWaveforms();
        UpdateStatusBar();
        
        StatusText.Text = "Diagram refreshed";
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _updateTimer.Stop();
        Close();
    }
    
    private void UpdateStatusBar()
    {
        TimeRangeText.Text = $"Time: 0s - {_elapsedSeconds:0.0}s";
        MachineCountText.Text = $"{_lanes.Count} State Machines";
        LastUpdateText.Text = $"Last Update: {DateTime.Now:HH:mm:ss.f}";
        
        // Show total transitions
        StatusText.Text = $"Total Transitions: {_transitions.Count}";
    }
    
    // Public method to add state transitions from external sources
    public void AddStateTransition(string machineName, string fromState, string toState, DateTime timestamp)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_lanes.TryGetValue(machineName, out var lane)) return;
            
            // Find and end the current segment
            var currentSegment = lane.Segments.LastOrDefault(s => !s.EndTime.HasValue);
            if (currentSegment != null)
            {
                currentSegment.EndTime = timestamp;
                currentSegment.EndX = (timestamp - _startTime).TotalSeconds * _pixelsPerSecond;
            }
            
            // Create new segment
            if (!lane.StateYLevels.ContainsKey(toState))
            {
                // Add unknown state to the lane if not exists
                var states = lane.PossibleStates.ToList();
                states.Add(toState);
                lane.PossibleStates = states.ToArray();
                
                // Recalculate Y levels
                double stateSpacing = (_laneHeight - 20) / (states.Count + 1);
                for (int i = 0; i < states.Count; i++)
                {
                    lane.StateYLevels[states[i]] = lane.YPosition + 15 + (i * stateSpacing);
                }
            }
            
            var newSegment = new StateSegment
            {
                State = toState,
                StartTime = timestamp,
                StartX = (timestamp - _startTime).TotalSeconds * _pixelsPerSecond,
                YLevel = lane.StateYLevels[toState],
                EndTime = null
            };
            lane.Segments.Add(newSegment);
            lane.CurrentState = toState;
            UpdateLaneHeader(lane);
            
            // Record transition
            _transitions.Add(new StateTransition
            {
                MachineName = machineName,
                FromState = fromState,
                ToState = toState,
                Timestamp = timestamp
            });
            
            DrawLaneWaveform(lane);
        });
    }
    
    // Public method to set the current state without transition
    public void SetCurrentState(string machineName, string state)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_lanes.TryGetValue(machineName, out var lane)) return;
            
            lane.CurrentState = state;
            UpdateLaneHeader(lane);
            
            // If no segments exist, create initial segment
            if (lane.Segments.Count == 0)
            {
                if (!lane.StateYLevels.ContainsKey(state))
                {
                    // Add unknown state to the lane if not exists
                    var states = lane.PossibleStates.ToList();
                    states.Add(state);
                    lane.PossibleStates = states.ToArray();
                    
                    // Recalculate Y levels
                    double stateSpacing = (_laneHeight - 20) / (states.Count + 1);
                    for (int i = 0; i < states.Count; i++)
                    {
                        lane.StateYLevels[states[i]] = lane.YPosition + 15 + (i * stateSpacing);
                    }
                }
                
                var segment = new StateSegment
                {
                    State = state,
                    StartTime = DateTime.Now,
                    StartX = 0,
                    YLevel = lane.StateYLevels[state],
                    EndTime = null
                };
                lane.Segments.Add(segment);
                DrawLaneWaveform(lane);
            }
        });
    }
}