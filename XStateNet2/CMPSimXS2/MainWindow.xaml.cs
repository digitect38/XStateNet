using System.Windows;
using System.Windows.Controls;
using CMPSimXS2.ViewModels;
using CMPSimXS2.Helpers;

namespace CMPSimXS2;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        InitializeStateTree();
    }

    private void InitializeStateTree()
    {
        // Create root node for CMP System
        var rootNode = new TreeViewItem
        {
            Header = "CMP System",
            IsExpanded = true
        };

        // Add station nodes
        var stationsNode = new TreeViewItem { Header = "Stations", IsExpanded = true };
        stationsNode.Items.Add(new TreeViewItem { Header = "LoadPort" });
        stationsNode.Items.Add(new TreeViewItem { Header = "Carrier" });
        stationsNode.Items.Add(new TreeViewItem { Header = "Polisher" });
        stationsNode.Items.Add(new TreeViewItem { Header = "Cleaner" });
        stationsNode.Items.Add(new TreeViewItem { Header = "Buffer" });
        rootNode.Items.Add(stationsNode);

        // Add robot nodes
        var robotsNode = new TreeViewItem { Header = "Robots", IsExpanded = true };
        robotsNode.Items.Add(new TreeViewItem { Header = "Robot 1" });
        robotsNode.Items.Add(new TreeViewItem { Header = "Robot 2" });
        robotsNode.Items.Add(new TreeViewItem { Header = "Robot 3" });
        rootNode.Items.Add(robotsNode);

        StateTreeView.Items.Add(rootNode);
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Start();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Stop();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Reset();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
    }

    private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
    {
        var logFilePath = Logger.Instance.GetLogFilePath();

        if (System.IO.File.Exists(logFilePath))
        {
            try
            {
                // Open the log file with the default text editor
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logFilePath,
                    UseShellExecute = true
                });
                Logger.Instance.Info("UI", "Log file opened by user");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("UI", $"Failed to open log file: {ex.Message}");
                System.Windows.MessageBox.Show($"Failed to open log file: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        else
        {
            System.Windows.MessageBox.Show("Log file not found.",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Shutdown();
        base.OnClosed(e);
    }
}
