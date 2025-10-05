using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimelineWPF.Control;
using TimelineWPF.ViewModels;

namespace TimelineWPF
{
    public partial class TimelineComponent : UserControl
    {
        private MainViewModel? _viewModel;
        private Point _lastMousePosition;
        private bool _isPanning;
        private DispatcherTimer? _timer;

        public static readonly DependencyProperty CurrentLineXProperty =
            DependencyProperty.Register(nameof(CurrentLineX), typeof(double), typeof(TimelineComponent), new PropertyMetadata(0.0));

        public double CurrentLineX
        {
            get => (double)GetValue(CurrentLineXProperty);
            set => SetValue(CurrentLineXProperty, value);
        }

        /// <summary>
        /// Gets the timeline data provider for managing state machines and timeline data
        /// </summary>
        public ITimelineDataProvider? DataProvider => _viewModel;

        public TimelineComponent()
        {
            InitializeComponent();
            _viewModel = DataContext as MainViewModel;

            // Attach mouse handlers for global panning control
            this.PreviewMouseWheel += TimelineComponent_PreviewMouseWheel;
            this.MouseDown += TimelineComponent_MouseDown;
            this.MouseMove += TimelineComponent_MouseMove;
            this.MouseUp += TimelineComponent_MouseUp;

            // Timer for timeline overlay updates
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(30);
            _timer.Tick += (s, e) => DrawCurrentLine();
            _timer.Start();
        }

        private void TimelineComponent_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewModel == null) return;
            if (_viewModel.IsRealtimeMode) return;

            var chartControl = FindVisualChild<StateMachineTimelineControl>(this);
            if (chartControl == null) return;

            var mousePos = e.GetPosition(chartControl);
            _viewModel.UpdateZoom(e.Delta, mousePos.X, chartControl.ActualWidth);
            e.Handled = true;
        }

        private void TimelineComponent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null || _viewModel.IsRealtimeMode || e.LeftButton != MouseButtonState.Pressed) return;

            _isPanning = true;
            _lastMousePosition = e.GetPosition(this);
            this.Cursor = Cursors.ScrollWE;
        }

        private void TimelineComponent_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning || _viewModel == null) return;

            var currentMousePosition = e.GetPosition(this);
            var dx = currentMousePosition.X - _lastMousePosition.X;
            _lastMousePosition = currentMousePosition;

            _viewModel.Pan(dx);
        }

        private void TimelineComponent_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                this.Cursor = Cursors.Arrow;
            }
        }

        private void ChartControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewModel != null && e.NewSize.Width > 0)
            {
                _viewModel.SetDefaultZoomFactor(e.NewSize.Width);
            }
        }

        private T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject? child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T typedChild)
                    return typedChild;
                else if (child != null)
                {
                    T? childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        private void DrawCurrentLine()
        {
            if (_viewModel == null || TimelineBorder == null || TimelineOverlay == null)
                return;

            TimelineOverlay.Children.Clear();

            double width = TimelineBorder.ActualWidth;
            double height = TimelineBorder.ActualHeight;

            double x = width * (_viewModel.IsRealtimeMode ? 0.98 : 0.5);
            CurrentLineX = x;
            double currentTime = _viewModel.IsRealtimeMode ? _viewModel.SimulationTime : _viewModel.ViewOffset;
            string label = $"{currentTime / 1000000:F3}s";

            var line = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = height,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            TimelineOverlay.Children.Add(line);

            var formattedText = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Inter"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                12,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip
            );

            var text = new TextBlock
            {
                Text = label,
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xEF, 0x44, 0x44)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(8, 2, 8, 2)
            };
            Canvas.SetLeft(text, x - formattedText.Width - 10);
            Canvas.SetTop(text, 10);
            TimelineOverlay.Children.Add(text);
        }

        private void TimelineBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCurrentLine();
        }
    }

    [ValueConversion(typeof(double), typeof(double))]
    public class MicrosecondToSecondConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double microseconds)
                return microseconds / 1000000.0;
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double seconds)
                return seconds * 1000000.0;
            return 0.0;
        }
    }

    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = false;
            if (value is bool) flag = (bool)value;

            if (parameter != null && parameter.ToString()?.ToLower() == "invert") flag = !flag;

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}