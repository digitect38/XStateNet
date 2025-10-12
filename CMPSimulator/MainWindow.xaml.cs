using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CMPSimulator.Controllers;
using CMPSimulator.Models;

namespace CMPSimulator;

public partial class MainWindow : Window
{
    private readonly ForwardPriorityController _controller;
    private readonly Storyboard _waferAnimationStoryboard;
    private double _currentZoom = 1.0;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;

    private bool _isPanning = false;
    private Point _panStart;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

    public MainWindow()
    {
        InitializeComponent();

        // Use Forward Priority Controller
        _controller = new ForwardPriorityController();
        _waferAnimationStoryboard = new Storyboard();

        // Set data context for wafer binding
        DataContext = _controller;

        // Subscribe to events
        _controller.LogMessage += Controller_LogMessage;
        _controller.StationStatusChanged += Controller_StationStatusChanged;

        Log("═══════════════════════════════════════════════════════════");
        Log("CMP Tool Simulator - Forward Priority Scheduler");
        Log("Priority: P1(C→B) > P2(P→C) > P3(L→P) > P4(B→L)");
        Log("Press Start to begin simulation");
        Log("═══════════════════════════════════════════════════════════");

        // Setup panning events
        SimulationCanvas.MouseLeftButtonDown += SimulationCanvas_MouseLeftButtonDown;
        SimulationCanvas.MouseMove += SimulationCanvas_MouseMove;
        SimulationCanvas.MouseLeftButtonUp += SimulationCanvas_MouseLeftButtonUp;
        SimulationCanvas.MouseLeave += SimulationCanvas_MouseLeave;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ResetButton.IsEnabled = false;

        await _controller.StartSimulation();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.StopSimulation();

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ResetButton.IsEnabled = true;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.ResetSimulation();

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;

        // Update station status displays
        UpdateStationDisplays();
    }

    private void Controller_LogMessage(object? sender, string message)
    {
        Dispatcher.Invoke(() => Log(message));
    }

    private void Controller_StationStatusChanged(object? sender, EventArgs e)
    {
        // Already on UI thread (called from Dispatcher.Invoke in UIUpdateService)
        UpdateStationDisplays();
    }

    private void Log(string message)
    {
        LogTextBlock.Text += message + Environment.NewLine;

        // Auto-scroll to bottom (TextBox has built-in scroll support)
        LogTextBlock.ScrollToEnd();

        // Update station displays when relevant events occur
        if (message.Contains("LoadPort") || message.Contains("Polisher") || message.Contains("Cleaner"))
        {
            UpdateStationDisplays();
        }
    }

    private void UpdateStationDisplays()
    {
        // Update LoadPort count (count wafers whose CurrentStation is LoadPort)
        var loadPortCount = _controller.Wafers.Count(w => w.CurrentStation == "LoadPort");
        LoadPortCountText.Text = $"{loadPortCount}/25";

        // Update station status displays
        R1StatusText.Text = _controller.R1Status;
        PolisherStatusText.Text = _controller.PolisherStatus;
        R2StatusText.Text = _controller.R2Status;
        CleanerStatusText.Text = _controller.CleanerStatus;
        R3StatusText.Text = _controller.R3Status;
        BufferStatusText.Text = _controller.BufferStatus;
    }

    private void SelectAllLog_Click(object sender, RoutedEventArgs e)
    {
        LogTextBlock.SelectAll();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(LogTextBlock.SelectedText))
        {
            Clipboard.SetText(LogTextBlock.SelectedText);
        }
        else
        {
            // If no text is selected, copy all text
            if (!string.IsNullOrEmpty(LogTextBlock.Text))
            {
                Clipboard.SetText(LogTextBlock.Text);
            }
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only zoom when Ctrl key is pressed
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;

            // Calculate new zoom level
            if (e.Delta > 0)
            {
                // Zoom in
                _currentZoom = Math.Min(_currentZoom + ZoomStep, ZoomMax);
            }
            else
            {
                // Zoom out
                _currentZoom = Math.Max(_currentZoom - ZoomStep, ZoomMin);
            }

            // Apply zoom to canvas
            CanvasScaleTransform.ScaleX = _currentZoom;
            CanvasScaleTransform.ScaleY = _currentZoom;
        }
    }

    private void SimulationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only pan when Ctrl key is pressed
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            _isPanning = true;
            _panStart = e.GetPosition(SimulationScrollViewer);
            _panStartHorizontalOffset = SimulationScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = SimulationScrollViewer.VerticalOffset;
            SimulationCanvas.Cursor = Cursors.Hand;
            SimulationCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SimulationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Point currentPosition = e.GetPosition(SimulationScrollViewer);
            double deltaX = _panStart.X - currentPosition.X;
            double deltaY = _panStart.Y - currentPosition.Y;

            SimulationScrollViewer.ScrollToHorizontalOffset(_panStartHorizontalOffset + deltaX);
            SimulationScrollViewer.ScrollToVerticalOffset(_panStartVerticalOffset + deltaY);
            e.Handled = true;
        }
    }

    private void SimulationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            SimulationCanvas.Cursor = Cursors.Arrow;
            SimulationCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void SimulationCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            SimulationCanvas.Cursor = Cursors.Arrow;
            SimulationCanvas.ReleaseMouseCapture();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _controller?.Dispose();
    }
}
