using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CMPSimulator.Controllers;
using CMPSimulator.Models;

namespace CMPSimulator;

public partial class MainWindow : Window
{
    private readonly IForwardPriorityController _controller;
    private readonly Storyboard _waferAnimationStoryboard;
    private double _currentZoom = 1.0;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;

    private bool _isPanning = false;
    private Point _panStart;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

    private DateTime _simulationStartTime = DateTime.Now;

    public MainWindow()
    {
        InitializeComponent();

        // Switch between implementations:
        // _controller = new ForwardPriorityController();  // Non-XStateNet version
        _controller = new OrchestratedForwardPriorityController();  // XStateNet Orchestrated version
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

    private void ExecutionMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;

        bool isSyncMode = SyncModeRadio.IsChecked == true;
        _controller.SetExecutionMode(isSyncMode ? ExecutionMode.Sync : ExecutionMode.Async);

        // Update UI based on mode
        // Note: Don't enable StepButton here - it's enabled by StartButton_Click
        // This just switches the mode for when Start is pressed
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        StepButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ResetButton.IsEnabled = false;

        // Reset simulation start time when starting
        _simulationStartTime = DateTime.Now;

        await _controller.StartSimulation();
    }

    private async void StepButton_Click(object sender, RoutedEventArgs e)
    {
        StepButton.IsEnabled = false;
        StartButton.IsEnabled = false;

        await _controller.ExecuteOneStep();

        StepButton.IsEnabled = true;
        StartButton.IsEnabled = true;
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

        // Reset simulation start time
        _simulationStartTime = DateTime.Now;

        StartButton.IsEnabled = true;
        StepButton.IsEnabled = true;
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
        // Calculate elapsed time since simulation start
        var elapsed = DateTime.Now - _simulationStartTime;
        string timestamp = $"[{elapsed.TotalSeconds:000.000}] ";

        LogTextBlock.Text += timestamp + message + Environment.NewLine;

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
        try
        {
            string textToCopy;

            if (!string.IsNullOrEmpty(LogTextBlock.SelectedText))
            {
                textToCopy = LogTextBlock.SelectedText;
            }
            else
            {
                // If no text is selected, copy all text
                textToCopy = LogTextBlock.Text;
            }

            if (string.IsNullOrEmpty(textToCopy))
                return;

            // Check if text is too large (> 10MB can cause clipboard issues)
            const int MaxClipboardSize = 10 * 1024 * 1024; // 10MB

            if (textToCopy.Length > MaxClipboardSize)
            {
                // Copy only the last N lines to avoid clipboard overflow
                var lines = textToCopy.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                int linesToCopy = Math.Min(5000, lines.Length); // Last 5000 lines

                var result = MessageBox.Show(
                    $"로그가 너무 큽니다 ({textToCopy.Length:N0} 문자).\n마지막 {linesToCopy}줄만 복사하시겠습니까?",
                    "클립보드 크기 초과",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var lastLines = lines.Skip(lines.Length - linesToCopy).ToArray();
                    textToCopy = string.Join(Environment.NewLine, lastLines);
                }
                else
                {
                    return;
                }
            }

            // Retry clipboard operation (CLIPBRD_E_CANT_OPEN workaround)
            bool success = false;
            int retries = 5;
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    Clipboard.SetText(textToCopy);
                    success = true;
                    break;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    if (i < retries - 1)
                    {
                        System.Threading.Thread.Sleep(100); // Wait 100ms before retry
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            if (success)
            {
                MessageBox.Show($"로그가 클립보드에 복사되었습니다.\n({textToCopy.Length:N0} 문자)",
                    "복사 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"클립보드 복사 실패: {ex.Message}\n\n다시 시도해주세요.",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Create temp directory if not exists
            string tempDir = Path.Combine(Path.GetTempPath(), "CMPSimulator");
            Directory.CreateDirectory(tempDir);

            // Generate timestamped filename
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logFileName = $"CMPSimulator_Log_{timestamp}.txt";
            string logFilePath = Path.Combine(tempDir, logFileName);

            // Write log to file
            File.WriteAllText(logFilePath, LogTextBlock.Text);

            // Open with default editor
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = logFilePath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processStartInfo);

            Log($"Log file opened: {logFilePath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"로그 파일 열기 실패: {ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
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
