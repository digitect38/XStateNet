using CMPSimXS2.ViewModels;
using CMPSimXS2.Helpers;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CMPSimXS2;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private double _currentZoom = 1.0;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;
    private ScaleTransform _zoomTransform = new ScaleTransform(1.0, 1.0);

    private bool _isPanning = false;
    private Point _panStart;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

    public MainWindow()
    {
        InitializeComponent();

        // Create and bind ViewModel
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // Apply zoom transform to simulation area
        if (SimulationCanvas != null)
        {
            SimulationCanvas.LayoutTransform = _zoomTransform;
        }

        Logger.Instance.Info("Application", "=== CMPSimXS2 Application Started ===");
        Logger.Instance.Info("Application", $"Log file location: {Logger.Instance.GetLogFilePath()}");
        Logger.Instance.Info("Application", $"Version: 1.0.0");
        Logger.Instance.Info("Application", $"Start time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.StartCommand.CanExecute(null))
        {
            _viewModel.StartCommand.Execute(null);
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ResetButton.IsEnabled = false;

            Logger.Instance.Info("MainWindow", "Simulation started");
        }
    }

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("MainWindow", "Step execution not yet implemented for XStateNet2");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.StopCommand.CanExecute(null))
        {
            _viewModel.StopCommand.Execute(null);
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ResetButton.IsEnabled = true;

            Logger.Instance.Info("MainWindow", "Simulation stopped");
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ResetCommand.CanExecute(null))
        {
            _viewModel.ResetCommand.Execute(null);
            StartButton.IsEnabled = true;
            StepButton.IsEnabled = true;
            StopButton.IsEnabled = false;

            Logger.Instance.Info("MainWindow", "Simulation reset");
        }
    }

    private void SeeLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string logFilePath = Logger.Instance.GetLogFilePath();

            if (!File.Exists(logFilePath))
            {
                MessageBox.Show($"Log file not found.\n\nPath: {logFilePath}\n\nRun the simulation first to generate the log.",
                    "Log File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Open with default text editor
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = logFilePath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processStartInfo);

            Logger.Instance.Info("MainWindow", $"Log file opened: {logFilePath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open log file: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
    {
        SeeLogButton_Click(sender, e);
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only zoom when Ctrl key is pressed
        if (Keyboard.Modifiers == ModifierKeys.Control && SimulationCanvas != null)
        {
            e.Handled = true;

            // Calculate new zoom level
            if (e.Delta > 0)
            {
                _currentZoom = Math.Min(_currentZoom + ZoomStep, ZoomMax);
            }
            else
            {
                _currentZoom = Math.Max(_currentZoom - ZoomStep, ZoomMin);
            }

            // Apply zoom
            _zoomTransform.ScaleX = _currentZoom;
            _zoomTransform.ScaleY = _currentZoom;
        }
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        // Only allow numeric input
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Parse timing values
            if (!int.TryParse(R1TransferTextBox.Text, out int r1Transfer) || r1Transfer < 0)
            {
                MessageBox.Show("R1 Transfer time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(PolisherTextBox.Text, out int polisher) || polisher < 0)
            {
                MessageBox.Show("Polisher time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(R2TransferTextBox.Text, out int r2Transfer) || r2Transfer < 0)
            {
                MessageBox.Show("R2 Transfer time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(CleanerTextBox.Text, out int cleaner) || cleaner < 0)
            {
                MessageBox.Show("Cleaner time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(R3TransferTextBox.Text, out int r3Transfer) || r3Transfer < 0)
            {
                MessageBox.Show("R3 Transfer time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(BufferHoldTextBox.Text, out int bufferHold) || bufferHold < 0)
            {
                MessageBox.Show("Buffer Hold time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(LoadPortReturnTextBox.Text, out int loadPortReturn) || loadPortReturn < 0)
            {
                MessageBox.Show("LoadPort Return time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Show success message
            if (SettingsStatusTextBlock != null)
            {
                SettingsStatusTextBlock.Text = "Settings applied successfully!";
                SettingsStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;

                // Clear success message after 3 seconds
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (s, args) =>
                {
                    SettingsStatusTextBlock.Text = "";
                    timer.Stop();
                };
                timer.Start();
            }

            Logger.Instance.Info("MainWindow", $"Settings updated: R1={r1Transfer}ms, Polisher={polisher}ms, R2={r2Transfer}ms, Cleaner={cleaner}ms, R3={r3Transfer}ms, Buffer={bufferHold}ms, LoadPort={loadPortReturn}ms");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply settings: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            if (SettingsStatusTextBlock != null)
            {
                SettingsStatusTextBlock.Text = "Failed to apply settings";
                SettingsStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
    }

    private void EditMode_Checked(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("MainWindow", "Edit Mode enabled - station editing not yet implemented for XStateNet2");
    }

    private void EditMode_Unchecked(object sender, RoutedEventArgs e)
    {
        Logger.Instance.Info("MainWindow", "Edit Mode disabled");
    }

    private void ExecutionMode_Changed(object sender, RoutedEventArgs e)
    {
        if (AsyncModeRadio != null && SyncModeRadio != null)
        {
            bool isSyncMode = SyncModeRadio.IsChecked == true;
            Logger.Instance.Info("MainWindow", $"Execution mode changed to: {(isSyncMode ? "Sync" : "Async")}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Logger.Instance.Info("Application", "=== CMPSimXS2 Application Closed ===");
    }
}
