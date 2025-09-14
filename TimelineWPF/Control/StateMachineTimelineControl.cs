using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using TimelineWPF.Models;

namespace TimelineWPF.Control
{
    public class StateMachineTimelineControl : FrameworkElement
    {
        private readonly Typeface _typeface = new Typeface(new FontFamily("Inter"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private readonly Dictionary<string, Brush> _stateBrushes = new Dictionary<string, Brush>();
        private double _offsetFromTimelineBorderLeft; // Field to store the calculated offset

        // Brushes and Pens (cache for performance)
        private static readonly Brush EventBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        private static readonly Brush ActionBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
        private static readonly Brush ActionHoleBrush = new SolidColorBrush(Color.FromRgb(0x1f, 0x29, 0x37));
        private static readonly Brush WhiteBrush = Brushes.White;
        private static readonly Pen LanePen = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x68)), 1);
        private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x68)), 1);
        private static readonly Pen LiveLinePen = new Pen(Brushes.Red, 2) { DashStyle = new DashStyle(new double[] { 2.5, 2.5 }, 0) };

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<TimelineItem>), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StateDefinitionsProperty =
            DependencyProperty.Register(nameof(StateDefinitions), typeof(List<string>), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(null, OnStateDefinitionsChanged));

        public static readonly DependencyProperty ViewOffsetProperty =
            DependencyProperty.Register(nameof(ViewOffset), typeof(double), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ZoomFactorProperty =
            DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(0.01, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsRealtimeModeProperty =
            DependencyProperty.Register(nameof(IsRealtimeMode), typeof(bool), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SimulationTimeProperty =
            DependencyProperty.Register(nameof(SimulationTime), typeof(double), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsStepDisplayModeProperty =
            DependencyProperty.Register(nameof(IsStepDisplayMode), typeof(bool), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LaneHeaderOffsetProperty =
            DependencyProperty.Register(nameof(LaneHeaderOffset), typeof(double), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TimelineCurrentLineXProperty =
            DependencyProperty.Register(nameof(TimelineCurrentLineX), typeof(double), typeof(StateMachineTimelineControl), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));


        public IEnumerable<TimelineItem>? ItemsSource
        {
            get => (IEnumerable<TimelineItem>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }
        public List<string>? StateDefinitions
        {
            get => (List<string>?)GetValue(StateDefinitionsProperty);
            set => SetValue(StateDefinitionsProperty, value);
        }
        public double ViewOffset
        {
            get => (double)GetValue(ViewOffsetProperty);
            set => SetValue(ViewOffsetProperty, value);
        }
        public double ZoomFactor
        {
            get => (double)GetValue(ZoomFactorProperty);
            set => SetValue(ZoomFactorProperty, value);
        }
        public bool IsRealtimeMode
        {
            get => (bool)GetValue(IsRealtimeModeProperty);
            set => SetValue(IsRealtimeModeProperty, value);
        }
        public double SimulationTime
        {
            get => (double)GetValue(SimulationTimeProperty);
            set => SetValue(SimulationTimeProperty, value);
        }
        public bool IsStepDisplayMode
        {
            get => (bool)GetValue(IsStepDisplayModeProperty);
            set => SetValue(IsStepDisplayModeProperty, value);
        }
        public double LaneHeaderOffset
        {
            get => (double)GetValue(LaneHeaderOffsetProperty);
            set => SetValue(LaneHeaderOffsetProperty, value);
        }
        public double TimelineCurrentLineX
        {
            get => (double)GetValue(TimelineCurrentLineXProperty);
            set => SetValue(TimelineCurrentLineXProperty, value);
        }

        private static void OnStateDefinitionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StateMachineTimelineControl control)
            {
                control.MapStateColors();
                control.InvalidateVisual();
            }
        }

        private void MapStateColors()
        {
            _stateBrushes.Clear();
            if (StateDefinitions == null) return;

            var colors = new List<Color>
            {
                Color.FromRgb(52, 211, 153),   // Green
                Color.FromRgb(251, 191, 36),   // Yellow
                Color.FromRgb(96, 165, 250),   // Blue
                Color.FromRgb(248, 113, 113),  // Red
                Color.FromRgb(167, 139, 250),  // Purple
                Color.FromRgb(244, 114, 182)   // Pink
            };

            for (int i = 0; i < StateDefinitions.Count; i++)
            {
                var brush = new SolidColorBrush(colors[i % colors.Count]);
                brush.Freeze(); // Performance optimization
                _stateBrushes[StateDefinitions[i]] = brush;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (ItemsSource == null || ActualWidth == 0)
            {
                return;
            }

            // Calculate offset from TimelineBorder
            var timelineBorder = FindAncestor<Border>(this, "TimelineBorder");
            if (timelineBorder != null)
            {
                GeneralTransform transform = this.TransformToAncestor(timelineBorder);
                Point relativePosition = transform.Transform(new Point(0, 0));
                _offsetFromTimelineBorderLeft = relativePosition.X;
            }
            else
            {
                _offsetFromTimelineBorderLeft = 0; // Fallback
            }

            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));

            DrawLanes(dc);
            DrawObjects(dc);
            DrawGrid(dc);

            dc.Pop();
        }

        private const double LaneHeight = 25;
        private readonly string[] _lanes = { "State", "Event", "Action" };

        private void DrawLanes(DrawingContext dc)
        {
            // Draw the top-most lane line
            dc.DrawLine(LanePen, new Point(0, 0 + LaneHeaderOffset), new Point(ActualWidth, 0 + LaneHeaderOffset));

            // Draw lines between the lanes
            for (int i = 1; i < _lanes.Length; i++)
            {
                dc.DrawLine(LanePen, new Point(0, i * LaneHeight + LaneHeaderOffset), new Point(ActualWidth, i * LaneHeight + LaneHeaderOffset));
            }
        }

        private double TimeToPx(double time)
        {
            double currentTime = IsRealtimeMode ? SimulationTime : ViewOffset;

            if (IsRealtimeMode)
            {
                // In real-time mode, position the live line at 98% from the left of the control (consistent with TimelineComponent)
                // Timeline items appear to the left of this line based on how far back in time they are
                double liveLineXPosition = ActualWidth * 0.98;
                double timeOffset = currentTime - time; // How far back in time this item is
                return liveLineXPosition - (timeOffset * ZoomFactor);
            }
            else
            {
                // Playback mode - use the original calculation
                double liveLineXInControl = TimelineCurrentLineX - _offsetFromTimelineBorderLeft;
                return (time - currentTime) * ZoomFactor + liveLineXInControl;
            }
        }

        private void DrawObjects(DrawingContext dc)
        {
            double currentTime = IsRealtimeMode ? SimulationTime : ViewOffset;
            var itemList = ItemsSource.ToList();

            foreach (var item in itemList)
            {
                // In real-time mode, don't render future objects
                if (IsRealtimeMode && item.Time > currentTime)
                {
                    continue;
                }

                double x = TimeToPx(item.Time);
                int laneIndex = Array.IndexOf(_lanes, item.Type.ToString());

                if (laneIndex == -1) continue;

                double y = laneIndex * LaneHeight + LaneHeaderOffset;

                if (item.Type == TimelineItemType.State)
                {
                    double durationToDraw = item.Duration;
                    if (IsRealtimeMode && item.Time + item.Duration > currentTime)
                    {
                        durationToDraw = currentTime - item.Time;
                    }

                    double width = durationToDraw * ZoomFactor;
                    if (x + width < 0 || x > ActualWidth) continue;

                    DrawState(dc, item, x, y, width);
                }
                else
                {
                    if (x < -20 || x > ActualWidth + 20) continue;

                    double yPos = y + LaneHeight / 2;
                    if (item.Type == TimelineItemType.Event) DrawEventIcon(dc, item, x, yPos);
                    else if (item.Type == TimelineItemType.Action) DrawActionIcon(dc, item, x, yPos);
                }
            }
        }

        private void DrawState(DrawingContext dc, TimelineItem item, double x, double y, double width)
        {
            if (item.Name == null || !_stateBrushes.TryGetValue(item.Name, out Brush brush))
            {
                brush = Brushes.Gray;
            }

            if (IsStepDisplayMode)
            {
                if (StateDefinitions == null) return;
                if (StateDefinitions.Count == 0) return;
                if (item.Name == null) return;

                int stateIndex = StateDefinitions.IndexOf(item.Name);
                if (stateIndex == -1) return;

                // Calculate step positioning: distribute evenly across the lane height
                double stepHeight = LaneHeight / StateDefinitions.Count;
                double stepY = y + stateIndex * stepHeight + stepHeight / 2;

                // Draw horizontal step line with proper thickness
                var stepPen = new Pen(brush, 4);
                stepPen.Freeze();
                dc.DrawLine(stepPen, new Point(x, stepY), new Point(x + width, stepY));

                // Draw vertical connectors at start and end for continuity
                if (width > 5)
                {
                    var connectorPen = new Pen(brush, 2);
                    connectorPen.Freeze();

                    // Start vertical connector
                    dc.DrawLine(connectorPen, new Point(x, stepY - 2), new Point(x, stepY + 2));
                    // End vertical connector
                    dc.DrawLine(connectorPen, new Point(x + width, stepY - 2), new Point(x + width, stepY + 2));
                }

                // Draw state label with better positioning
                if (width > 40)
                {
                    var formattedText = new FormattedText(item.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, 10, WhiteBrush, 1.25);

                    // Position text above the step line, with padding
                    double textX = x + Math.Max(5, width / 2 - formattedText.Width / 2);
                    double textY = stepY - formattedText.Height - 3;

                    // Ensure text doesn't go above the lane
                    if (textY < y + 2)
                    {
                        textY = stepY + 3; // Position below line instead
                    }

                    dc.DrawText(formattedText, new Point(textX, textY));
                }
            }
            else // Block Mode
            {
                dc.DrawRectangle(brush, null, new Rect(x, y + 5, width, LaneHeight - 10));

                // Block mode 에서는 Text Foreground Color를 Black으로 고정
                if (width > 30)
                {
                    var formattedText = new FormattedText(item.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, 12, Brushes.Black, 1.25);
                    dc.DrawText(formattedText, new Point(x + width / 2 - formattedText.Width / 2, y + LaneHeight / 2 - formattedText.Height / 2));
                }
            }
        }

        private void DrawEventIcon(DrawingContext dc, TimelineItem item, double x, double y)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x + 2, y - 10), true, true);
                ctx.LineTo(new Point(x - 5, y + 1), true, false);
                ctx.LineTo(new Point(x, y + 1), true, false);
                ctx.LineTo(new Point(x - 2, y + 10), true, false);
                ctx.LineTo(new Point(x + 5, y - 1), true, false);
                ctx.LineTo(new Point(x, y - 1), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(EventBrush, null, geometry);
            DrawIconLabel(dc, item.Name, x, y);
        }

        private void DrawActionIcon(DrawingContext dc, TimelineItem item, double x, double y)
        {
            const int numTeeth = 8;
            const double outerRadius = 10;
            const double innerRadius = 7;
            const double holeRadius = 3;

            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(x, y - outerRadius), IsClosed = true };

            for (int i = 0; i < numTeeth * 2; i++)
            {
                double radius = (i % 2 == 0) ? outerRadius : innerRadius;
                double angle = (i / (double)(numTeeth * 2)) * Math.PI * 2 - Math.PI / 2;
                figure.Segments.Add(new LineSegment(new Point(x + radius * Math.Cos(angle), y + radius * Math.Sin(angle)), true));
            }
            geometry.Figures.Add(figure);

            // This creates the hole by combining geometries
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, geometry, new EllipseGeometry(new Point(x, y), holeRadius, holeRadius));
            combined.Freeze();
            dc.DrawGeometry(ActionBrush, new Pen(Brushes.Transparent, 0), combined);
            DrawIconLabel(dc, item.Name, x, y);
        }

        private void DrawIconLabel(DrawingContext dc, string name, double x, double y)
        {
            var formattedText = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, 10, WhiteBrush, 1.25);
            dc.DrawText(formattedText, new Point(x + 15, y - formattedText.Height / 2));
        }

        private void DrawGrid(DrawingContext dc)
        {
            double gridY = _lanes.Length * LaneHeight + LaneHeaderOffset;
            dc.DrawLine(GridPen, new Point(0, gridY), new Point(ActualWidth, gridY));

            double timePerPixel = 1 / ZoomFactor;
            double timeAtLiveLine = IsRealtimeMode ? SimulationTime : ViewOffset;
            double xAtLiveLineInControl = TimelineCurrentLineX - _offsetFromTimelineBorderLeft;

            double visibleTimeStart = timeAtLiveLine - (xAtLiveLineInControl / ZoomFactor);
            double visibleTimeEnd = timeAtLiveLine + ((ActualWidth - xAtLiveLineInControl) / ZoomFactor);

            long[] timeIntervals = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000, 1000000, 2000000, 5000000, 10000000 };
            long gridTimeInterval = timeIntervals[timeIntervals.Length - 1];
            foreach (var interval in timeIntervals)
            {
                if (interval > (visibleTimeEnd - visibleTimeStart) / (ActualWidth / 100))
                {
                    gridTimeInterval = interval;
                    break;
                }
            }

            int subdivisions = 10;
            if (gridTimeInterval.ToString().StartsWith("5")) subdivisions = 5;
            else if (gridTimeInterval.ToString().StartsWith("2")) subdivisions = 2;

            double subGridTimeInterval = (double)gridTimeInterval / subdivisions;

            long startIndex = (long)Math.Floor(visibleTimeStart / subGridTimeInterval);
            long endIndex = (long)Math.Ceiling(visibleTimeEnd / subGridTimeInterval);

            for (long i = startIndex; i <= endIndex; i++)
            {
                double t = i * subGridTimeInterval;
                double x = TimeToPx(t);
                if (x < 0 || x > ActualWidth) continue;

                bool isMainTick = i % subdivisions == 0;
                dc.DrawLine(GridPen, new Point(x, gridY), new Point(x, gridY + (isMainTick ? 10 : 5)));

                if (isMainTick)
                {
                    string timeLabel;
                    if (gridTimeInterval >= 1000000) timeLabel = $"{t / 1000000:G3}s";
                    else if (gridTimeInterval >= 1000) timeLabel = $"{t / 1000}ms";
                    else timeLabel = $"{t}µs";

                    var formattedText = new FormattedText(timeLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, 10, Brushes.LightGray, 1.25);
                    dc.DrawText(formattedText, new Point(x - formattedText.Width / 2, gridY + 15));
                }
            }
        }

        private T? FindAncestor<T>(DependencyObject? current, string name) where T : FrameworkElement
        {
            while (current != null)
            {
                if (current is T element && element.Name == name)
                {
                    return element;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}