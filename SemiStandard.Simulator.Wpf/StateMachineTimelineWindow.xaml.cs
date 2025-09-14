using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SemiStandard.Simulator.Wpf
{
    public partial class StateMachineTimelineWindow : Window
    {
        // Constants
        private const double LANE_HEIGHT = 50;
        private const double TITLE_WIDTH = 100;
        private const double GRID_HEIGHT = 40;
        
        // State colors
        private readonly Color[] STATE_COLORS = new[]
        {
            Color.FromRgb(52, 211, 153),  // Green
            Color.FromRgb(251, 191, 36),   // Yellow
            Color.FromRgb(96, 165, 250),   // Blue
            Color.FromRgb(248, 113, 113),  // Red
            Color.FromRgb(167, 139, 250),  // Purple
            Color.FromRgb(244, 114, 182)   // Pink
        };

        // Simulation state
        private enum SimulationState { Stopped, Running, Paused }
        private SimulationState simulationState = SimulationState.Stopped;
        
        // Timing
        private DispatcherTimer simulationTimer;
        private DateTime lastTimestamp;
        private double simulationTime = 0; // microseconds
        private double playbackSpeed = 1.0;
        private bool isRealtimeMode = true;
        private bool isStepDisplayMode = false;
        
        // View control
        private double zoomFactor = 0.01;
        private double playbackZoomFactor = 0.01;
        private double viewOffset = 0; // microseconds
        private bool isPanning = false;
        private Point lastPanPoint;
        
        // State machine data
        private List<TimelineItem> sm1Data = new List<TimelineItem>();
        private List<TimelineItem> sm2Data = new List<TimelineItem>();
        private List<TimelineItem> sm3Data = new List<TimelineItem>();
        
        // State machine definitions
        private readonly StateMachineDefinition sm1Def = new StateMachineDefinition
        {
            Name = "SM1 (Ping)",
            States = new[] { "waiting", "pinging" },
            InitialState = "waiting"
        };
        
        private readonly StateMachineDefinition sm2Def = new StateMachineDefinition
        {
            Name = "SM2 (Pong)",
            States = new[] { "waiting", "ponging" },
            InitialState = "waiting"
        };
        
        private readonly StateMachineDefinition sm3Def = new StateMachineDefinition
        {
            Name = "SM3 (Pang)",
            States = new[] { "waiting", "panging" },
            InitialState = "waiting"
        };

        public StateMachineTimelineWindow()
        {
            Debug.WriteLine("[DEBUG] StateMachineTimelineWindow constructor called");
            InitializeComponent();
            InitializeSimulation();
            Debug.WriteLine("[DEBUG] StateMachineTimelineWindow initialization complete");
        }

        private void InitializeSimulation()
        {
            Debug.WriteLine("[DEBUG] InitializeSimulation called");

            simulationTimer = new DispatcherTimer();
            simulationTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            simulationTimer.Tick += SimulationTimer_Tick;

            // Set initial mode
            isRealtimeMode = true;
            UpdateSpeedControlVisibility();

            // Calculate initial zoom factor
            UpdateZoomFactorForRealtimeMode();
            playbackZoomFactor = zoomFactor;

            // Generate initial data
            GenerateSimulationData();
            Debug.WriteLine($"[DEBUG] After GenerateSimulationData - sm1Data.Count={sm1Data.Count}");

            // Initial draw
            DrawAllCharts();
            Debug.WriteLine("[DEBUG] InitializeSimulation complete");
        }

        private void GenerateSimulationData()
        {
            sm1Data.Clear();
            sm2Data.Clear();
            sm3Data.Clear();
            
            double currentTime = 0;
            Random random = new Random();
            
            // Add initial waiting states
            var lastState1 = new TimelineItem { Time = 0, Type = ItemType.State, Name = "waiting", Duration = 0 };
            var lastState2 = new TimelineItem { Time = 0, Type = ItemType.State, Name = "waiting", Duration = 0 };
            var lastState3 = new TimelineItem { Time = 0, Type = ItemType.State, Name = "waiting", Duration = 0 };
            
            sm1Data.Add(lastState1);
            sm2Data.Add(lastState2);
            sm3Data.Add(lastState3);
            
            // Initial wait with jitter
            currentTime += GetJitteryDuration(1000000, 0.4, random);
            
            // Generate 500 cycles
            for (int i = 0; i < 500; i++)
            {
                double pingDuration = GetJitteryDuration(1000000, 0.4, random);
                double pongDuration = GetJitteryDuration(1000000, 0.4, random);
                double pangDuration = GetJitteryDuration(1000000, 0.4, random);
                
                // PING starts
                lastState1.Duration = currentTime - lastState1.Time;
                sm1Data.Add(new TimelineItem { Time = currentTime, Type = ItemType.Event, Name = "PING" });
                lastState1 = new TimelineItem { Time = currentTime, Type = ItemType.State, Name = "pinging", Duration = pingDuration };
                sm1Data.Add(lastState1);
                
                currentTime += pingDuration;
                sm1Data.Add(new TimelineItem { Time = currentTime, Type = ItemType.Action, Name = "sendPong" });
                
                // PONG starts
                lastState2.Duration = currentTime - lastState2.Time;
                sm2Data.Add(new TimelineItem { Time = currentTime, Type = ItemType.Event, Name = "PONG" });
                lastState2 = new TimelineItem { Time = currentTime, Type = ItemType.State, Name = "ponging", Duration = pongDuration };
                sm2Data.Add(lastState2);
                lastState1 = new TimelineItem { Time = currentTime, Type = ItemType.State, Name = "waiting", Duration = 0 };
                sm1Data.Add(lastState1);
                
                currentTime += pongDuration;
                sm2Data.Add(new TimelineItem { Time = currentTime, Type = ItemType.Action, Name = "sendPang" });
                
                // PANG starts
                lastState3.Duration = currentTime - lastState3.Time;
                sm3Data.Add(new TimelineItem { Time = currentTime, Type = ItemType.Event, Name = "PANG" });
                lastState3 = new TimelineItem { Time = currentTime, Type = ItemType.State, Name = "panging", Duration = pangDuration };
                sm3Data.Add(lastState3);
                lastState2 = new TimelineItem { Time = currentTime, Type = ItemType.State, Name = "waiting", Duration = 0 };
                sm2Data.Add(lastState2);
                
                currentTime += pangDuration;
                sm3Data.Add(new TimelineItem { Time = currentTime, Type = ItemType.Action, Name = "sendPing" });
                lastState3 = new TimelineItem { Time = currentTime, Type = ItemType.State, Name = "waiting", Duration = 0 };
                sm3Data.Add(lastState3);
            }
            
            // Set final durations
            lastState1.Duration = currentTime - lastState1.Time;
            lastState2.Duration = currentTime - lastState2.Time;
            lastState3.Duration = currentTime - lastState3.Time;
        }

        private double GetJitteryDuration(double baseTime, double jitter, Random random)
        {
            return baseTime * (1 - jitter) + random.NextDouble() * (baseTime * jitter * 2);
        }

        private void SimulationTimer_Tick(object sender, EventArgs e)
        {
            if (simulationState != SimulationState.Running) return;

            var now = DateTime.Now;
            if (lastTimestamp == default(DateTime))
            {
                lastTimestamp = now;
            }

            var deltaTime = (now - lastTimestamp).TotalMilliseconds;
            lastTimestamp = now;

            // Update simulation time
            simulationTime += deltaTime * 1000 * playbackSpeed; // Convert to microseconds

            Debug.WriteLine($"[DEBUG] Timer Tick - simulationTime={simulationTime:F0}μs, deltaTime={deltaTime:F1}ms");

            // In playback mode, update view offset to follow simulation time
            if (!isRealtimeMode)
            {
                viewOffset = simulationTime;
            }

            // Update time display
            TimeDisplay.Text = $"Time: {(simulationTime / 1000000):F3}s";

            // Redraw all charts
            DrawAllCharts();
        }

        private void DrawAllCharts()
        {
            Debug.WriteLine($"[DEBUG] DrawAllCharts called - sm1Data.Count={sm1Data.Count}, sm2Data.Count={sm2Data.Count}, sm3Data.Count={sm3Data.Count}");
            DrawChart(SM1Canvas, sm1Data, sm1Def);
            DrawChart(SM2Canvas, sm2Data, sm2Def);
            DrawChart(SM3Canvas, sm3Data, sm3Def);
        }

        private void DrawChart(Canvas canvas, List<TimelineItem> data, StateMachineDefinition definition)
        {
            Debug.WriteLine($"[DEBUG] DrawChart called for '{definition.Name}' - data.Count={data.Count}, canvas.ActualWidth={canvas.ActualWidth}");

            canvas.Children.Clear();

            double width = canvas.ActualWidth;
            if (width <= 0) width = 1200; // Default width

            double height = LANE_HEIGHT * 3 + GRID_HEIGHT;
            canvas.Height = height;
            
            // Draw lanes
            DrawLanes(canvas);
            
            // Draw grid
            if (isRealtimeMode)
            {
                DrawGridRealtime(canvas);
            }
            else
            {
                DrawGrid(canvas);
            }
            
            // Draw objects
            DrawObjects(canvas, data, definition);
            
            // Draw live line
            DrawLiveLine(canvas);
        }

        private void DrawLanes(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            
            for (int i = 1; i < 3; i++)
            {
                var line = new Line
                {
                    X1 = 0,
                    Y1 = i * LANE_HEIGHT,
                    X2 = width,
                    Y2 = i * LANE_HEIGHT,
                    Stroke = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(line);
            }
        }

        private void DrawLiveLine(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double liveLineX = width / 2;
            
            // Draw red dashed line
            var liveLine = new Line
            {
                X1 = liveLineX,
                Y1 = 0,
                X2 = liveLineX,
                Y2 = LANE_HEIGHT * 3,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 5 }
            };
            canvas.Children.Add(liveLine);
            
            // Draw time label
            double currentTime = isRealtimeMode ? simulationTime : viewOffset;
            string timeLabel = $"{(currentTime / 1000000):F3}s";
            
            var textBlock = new TextBlock
            {
                Text = timeLabel,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, 239, 68, 68)),
                Padding = new Thickness(5, 2, 5, 2),
                FontSize = 12
            };
            
            Canvas.SetLeft(textBlock, liveLineX + 5);
            Canvas.SetTop(textBlock, 5);
            canvas.Children.Add(textBlock);
        }

        private double TimeToPx(double time, Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double liveLineX = width / 2;
            double currentTime = isRealtimeMode ? simulationTime : viewOffset;
            return (time - currentTime) * zoomFactor + liveLineX;
        }

        private void DrawObjects(Canvas canvas, List<TimelineItem> data, StateMachineDefinition definition)
        {
            double currentTime = isRealtimeMode ? simulationTime : viewOffset;
            double width = canvas.ActualWidth;
            
            foreach (var item in data)
            {
                // In real-time mode, don't render future objects
                if (isRealtimeMode && item.Time > currentTime) continue;
                
                double x = TimeToPx(item.Time, canvas);
                int laneIndex = GetLaneIndex(item.Type);
                if (laneIndex == -1) continue;
                
                double y = laneIndex * LANE_HEIGHT;
                
                if (item.Type == ItemType.State)
                {
                    Debug.WriteLine($"[DEBUG] Processing State item: '{item.Name}' at time {item.Time}, duration {item.Duration}");

                    double durationToDraw = item.Duration;
                    if (isRealtimeMode && item.Time + item.Duration > currentTime)
                    {
                        durationToDraw = currentTime - item.Time;
                    }

                    double rectWidth = durationToDraw * zoomFactor;

                    Debug.WriteLine($"[DEBUG] State item width: {rectWidth}, x: {x}, y: {y}");

                    // Culling
                    if (x + rectWidth < 0 || x > width)
                    {
                        Debug.WriteLine($"[DEBUG] State item culled: x+width={x + rectWidth}, canvas width={width}");
                        continue;
                    }

                    DrawState(canvas, item, x, y, rectWidth, definition);
                }
                else
                {
                    // Culling for icons
                    if (x < -20 || x > width + 20) continue;
                    
                    double yPos = y + LANE_HEIGHT / 2;
                    
                    if (item.Type == ItemType.Event)
                    {
                        DrawEventIcon(canvas, item, x, yPos);
                    }
                    else if (item.Type == ItemType.Action)
                    {
                        DrawActionIcon(canvas, item, x, yPos);
                    }
                }
            }
        }

        private void DrawState(Canvas canvas, TimelineItem item, double x, double y, double width, StateMachineDefinition definition)
        {
            int stateIndex = Array.IndexOf(definition.States, item.Name);

            // Debug output to trace state name matching
            Debug.WriteLine($"[DEBUG] DrawState: item.Name='{item.Name}', definition.States=[{string.Join(", ", definition.States)}], stateIndex={stateIndex}");

            // Ensure we have a valid state index, fallback to 0 if not found
            if (stateIndex == -1)
            {
                Debug.WriteLine($"[DEBUG] State '{item.Name}' not found in definition, using index 0");
                stateIndex = 0; // Default to first color if state not found in definition
            }

            // Force very obvious colors for testing
            Color color;
            if (stateIndex == 0)
            {
                color = Colors.Red;  // Very obvious red
            }
            else if (stateIndex == 1)
            {
                color = Colors.Lime; // Very obvious green
            }
            else
            {
                color = Colors.Blue; // Very obvious blue
            }

            Debug.WriteLine($"[DEBUG] Using color: R={color.R}, G={color.G}, B={color.B} for state '{item.Name}' (stateIndex={stateIndex})");
            
            if (isStepDisplayMode)
            {
                // Step mode - draw as horizontal lines with improved visibility
                double stepHeight = LANE_HEIGHT / definition.States.Length;
                double stepY = y + stateIndex * stepHeight;
                double lineY = stepY + stepHeight / 2;

                // Draw main horizontal step line with increased thickness
                var line = new Line
                {
                    X1 = x,
                    Y1 = lineY,
                    X2 = x + width,
                    Y2 = lineY,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 5  // Increased from 3 to 5 for better visibility
                };
                canvas.Children.Add(line);

                // Draw vertical connectors at start and end for continuity (if width > 10)
                if (width > 10)
                {
                    // Start connector
                    var startConnector = new Line
                    {
                        X1 = x,
                        Y1 = lineY - 3,
                        X2 = x,
                        Y2 = lineY + 3,
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = 3
                    };
                    canvas.Children.Add(startConnector);

                    // End connector
                    var endConnector = new Line
                    {
                        X1 = x + width,
                        Y1 = lineY - 3,
                        X2 = x + width,
                        Y2 = lineY + 3,
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = 3
                    };
                    canvas.Children.Add(endConnector);
                }

                // Draw state name with better positioning
                if (width > 40)
                {
                    var text = new TextBlock
                    {
                        Text = item.Name,
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    };

                    // Position text above the line, but check if it fits
                    double textY = lineY - 18;
                    if (textY < y + 2) // If text would go above the lane
                    {
                        textY = lineY + 4; // Place below the line instead
                    }

                    Canvas.SetLeft(text, x + Math.Max(5, width / 2 - 25));
                    Canvas.SetTop(text, textY);
                    canvas.Children.Add(text);
                }
            }
            else
            {
                // Block mode - draw as rectangles
                var rect = new Rectangle
                {
                    Width = width,
                    Height = LANE_HEIGHT - 10,
                    Fill = new SolidColorBrush(color)
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y + 5);
                canvas.Children.Add(rect);
                
                // Draw text centered in block
                if (width > 30)
                {
                    var text = new TextBlock
                    {
                        Text = item.Name,
                        Foreground = Brushes.White,
                        FontSize = 12,
                        TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(text, x + width / 2 - 25);
                    Canvas.SetTop(text, y + LANE_HEIGHT / 2 - 8);
                    canvas.Children.Add(text);
                }
            }
        }

        private void DrawEventIcon(Canvas canvas, TimelineItem item, double x, double y)
        {
            // Lightning bolt icon
            var polygon = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(x + 2, y - 10),
                    new Point(x - 5, y + 1),
                    new Point(x, y + 1),
                    new Point(x - 2, y + 10),
                    new Point(x + 5, y - 1),
                    new Point(x, y - 1)
                },
                Fill = new SolidColorBrush(Color.FromRgb(251, 191, 36)) // Yellow
            };
            canvas.Children.Add(polygon);
            
            DrawIconLabel(canvas, item.Name, x, y);
        }

        private void DrawActionIcon(Canvas canvas, TimelineItem item, double x, double y)
        {
            // Gear icon
            var gear = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(Color.FromRgb(96, 165, 250)) // Blue
            };
            Canvas.SetLeft(gear, x - 8);
            Canvas.SetTop(gear, y - 8);
            canvas.Children.Add(gear);
            
            // Center hole
            var hole = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromRgb(31, 41, 55))
            };
            Canvas.SetLeft(hole, x - 3);
            Canvas.SetTop(hole, y - 3);
            canvas.Children.Add(hole);
            
            DrawIconLabel(canvas, item.Name, x, y);
        }

        private void DrawIconLabel(Canvas canvas, string name, double x, double y)
        {
            var text = new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 10
            };
            Canvas.SetLeft(text, x + 15);
            Canvas.SetTop(text, y - 6);
            canvas.Children.Add(text);
        }

        private void DrawGridRealtime(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double gridY = LANE_HEIGHT * 3;
            
            // Draw horizontal ruler line
            var rulerLine = new Line
            {
                X1 = 0,
                Y1 = gridY,
                X2 = width,
                Y2 = gridY,
                Stroke = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                StrokeThickness = 1
            };
            canvas.Children.Add(rulerLine);
            
            // Draw time markers every 1 second
            double centerTime = simulationTime;
            double gridTimeInterval = 1000000; // 1s in microseconds
            
            double leftEdgeTime = centerTime - (width / 2) / zoomFactor;
            double startGridTime = Math.Floor(leftEdgeTime / gridTimeInterval) * gridTimeInterval;
            double endGridTime = centerTime + (width / 2) / zoomFactor;
            
            for (double t = startGridTime; t < endGridTime; t += gridTimeInterval)
            {
                double x = TimeToPx(t, canvas);
                if (x < 0 || x > width) continue;
                
                var tick = new Line
                {
                    X1 = x,
                    Y1 = gridY,
                    X2 = x,
                    Y2 = gridY + 10,
                    Stroke = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(tick);
                
                var label = new TextBlock
                {
                    Text = $"{Math.Round(t / 1000000)}s",
                    Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                    FontSize = 10
                };
                Canvas.SetLeft(label, x - 10);
                Canvas.SetTop(label, gridY + 15);
                canvas.Children.Add(label);
            }
        }

        private void DrawGrid(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double gridY = LANE_HEIGHT * 3;
            
            // Draw horizontal ruler line
            var rulerLine = new Line
            {
                X1 = 0,
                Y1 = gridY,
                X2 = width,
                Y2 = gridY,
                Stroke = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                StrokeThickness = 1
            };
            canvas.Children.Add(rulerLine);
            
            double timePerPixel = 1 / zoomFactor;
            double centerTime = viewOffset;
            
            double minGridSpacingPx = 100;
            double minTimeSpacing = minGridSpacingPx * timePerPixel;
            
            double[] timeIntervals = new[]
            {
                1.0, 2, 5, 10, 20, 50, 100, 200, 500,
                1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000,
                1000000, 2000000, 5000000, 10000000
            };
            
            double gridTimeInterval = timeIntervals.FirstOrDefault(i => i > minTimeSpacing);
            if (gridTimeInterval == 0) gridTimeInterval = timeIntervals.Last();
            
            int subdivisions = 10;
            if (gridTimeInterval.ToString().StartsWith("5")) subdivisions = 5;
            else if (gridTimeInterval.ToString().StartsWith("2")) subdivisions = 2;
            
            double subGridTimeInterval = gridTimeInterval / subdivisions;
            
            double visibleTimeSpan = width * timePerPixel;
            int startIndex = (int)Math.Floor((centerTime - visibleTimeSpan / 2) / subGridTimeInterval);
            int endIndex = (int)Math.Ceiling((centerTime + visibleTimeSpan / 2) / subGridTimeInterval);
            
            for (int i = startIndex; i <= endIndex; i++)
            {
                double t = i * subGridTimeInterval;
                double x = TimeToPx(t, canvas);
                if (x < 0 || x > width) continue;
                
                bool isMainTick = Math.Abs(i % subdivisions) < 0.001;
                
                var tick = new Line
                {
                    X1 = x,
                    Y1 = gridY,
                    X2 = x,
                    Y2 = gridY + (isMainTick ? 10 : 5),
                    Stroke = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(tick);
                
                if (isMainTick)
                {
                    string timeLabel;
                    if (gridTimeInterval >= 1000000)
                    {
                        double seconds = t / 1000000;
                        timeLabel = $"{seconds:F3}s";
                    }
                    else if (gridTimeInterval >= 1000)
                    {
                        timeLabel = $"{Math.Round(t / 1000)}ms";
                    }
                    else
                    {
                        timeLabel = $"{Math.Round(t)}µs";
                    }
                    
                    var label = new TextBlock
                    {
                        Text = timeLabel,
                        Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
                        FontSize = 10
                    };
                    Canvas.SetLeft(label, x - 15);
                    Canvas.SetTop(label, gridY + 15);
                    canvas.Children.Add(label);
                }
            }
        }

        private int GetLaneIndex(ItemType type)
        {
            switch (type)
            {
                case ItemType.State: return 0;
                case ItemType.Event: return 1;
                case ItemType.Action: return 2;
                default: return -1;
            }
        }

        private void UpdateZoomFactorForRealtimeMode()
        {
            double width = SM1Canvas.ActualWidth;
            if (width <= 0) width = 1200;
            // 10 seconds (10,000,000 microseconds) should fit in the width
            zoomFactor = width / 10000000;
        }

        private void UpdateSpeedControlVisibility()
        {
            // Speed control is hidden in real-time mode
            SpeedSlider.IsEnabled = !isRealtimeMode;
            SpeedValueText.Opacity = isRealtimeMode ? 0.5 : 1.0;
        }

        private void UpdateButtonStates()
        {
            StartButton.IsEnabled = simulationState != SimulationState.Running;
            PauseButton.IsEnabled = simulationState == SimulationState.Running;
            ResetButton.IsEnabled = simulationState != SimulationState.Stopped;
            
            StatusText.Text = simulationState.ToString();
        }

        // Event handlers
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            simulationState = SimulationState.Running;
            lastTimestamp = default(DateTime);
            simulationTimer.Start();
            UpdateButtonStates();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            simulationState = SimulationState.Paused;
            simulationTimer.Stop();
            UpdateButtonStates();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            simulationState = SimulationState.Stopped;
            simulationTimer.Stop();
            simulationTime = 0;
            viewOffset = 0;
            GenerateSimulationData();
            DrawAllCharts();
            UpdateButtonStates();
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            playbackSpeed = SpeedSlider.Value;
            SpeedValueText.Text = $"x{playbackSpeed:F1}";
        }

        private void ModeSwitch_Checked(object sender, RoutedEventArgs e)
        {
            // Switched to playback mode
            isRealtimeMode = false;
            viewOffset = simulationTime;
            zoomFactor = playbackZoomFactor;
            UpdateSpeedControlVisibility();
            DrawAllCharts();
        }

        private void ModeSwitch_Unchecked(object sender, RoutedEventArgs e)
        {
            // Switched to real-time mode
            isRealtimeMode = true;
            UpdateZoomFactorForRealtimeMode();
            UpdateSpeedControlVisibility();
            DrawAllCharts();
            if (simulationState != SimulationState.Running)
            {
                StartButton_Click(null, null);
            }
        }

        private void DisplayModeSwitch_Checked(object sender, RoutedEventArgs e)
        {
            isStepDisplayMode = true;
            DrawAllCharts();
        }

        private void DisplayModeSwitch_Unchecked(object sender, RoutedEventArgs e)
        {
            isStepDisplayMode = false;
            DrawAllCharts();
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (isRealtimeMode) return;
            
            var canvas = sender as Canvas;
            Point mousePos = e.GetPosition(canvas);
            
            double timeAtMouse = viewOffset + (mousePos.X - canvas.ActualWidth / 2) / zoomFactor;
            
            double zoomIntensity = 0.1;
            double delta = e.Delta > 0 ? (1 + zoomIntensity) : (1 - zoomIntensity);
            
            double newZoomFactor = zoomFactor * delta;
            newZoomFactor = Math.Max(0.0001, newZoomFactor);
            newZoomFactor = Math.Min(1, newZoomFactor);
            
            zoomFactor = newZoomFactor;
            playbackZoomFactor = newZoomFactor;
            
            viewOffset = timeAtMouse - (mousePos.X - canvas.ActualWidth / 2) / zoomFactor;
            
            DrawAllCharts();
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isRealtimeMode) return;
            
            if (simulationState == SimulationState.Running)
            {
                PauseButton_Click(null, null);
            }
            
            isPanning = true;
            lastPanPoint = e.GetPosition(sender as Canvas);
            (sender as Canvas).CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isRealtimeMode || !isPanning) return;
            
            var canvas = sender as Canvas;
            Point currentPoint = e.GetPosition(canvas);
            double dx = currentPoint.X - lastPanPoint.X;
            lastPanPoint = currentPoint;
            
            viewOffset -= dx / zoomFactor;
            simulationTime = viewOffset;
            DrawAllCharts();
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isPanning)
            {
                isPanning = false;
                (sender as Canvas).ReleaseMouseCapture();
            }
        }

        private class TimelineItem
        {
            public double Time { get; set; }
            public ItemType Type { get; set; }
            public string Name { get; set; }
            public double Duration { get; set; }
        }

        private enum ItemType
        {
            State,
            Event,
            Action
        }

        private class StateMachineDefinition
        {
            public string Name { get; set; }
            public string[] States { get; set; }
            public string InitialState { get; set; }
        }
    }
}