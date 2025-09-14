using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Diagnostics;

namespace SemiStandard.Simulator.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Log("[APP] Application starting...");
        Logger.Log($"[APP] Log file: {Logger.GetLogFilePath()}");
        
        // Set shutdown mode to only shutdown when the main window closes
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        
        try
        {
            // Check command line arguments
            Logger.Log($"[APP] Command line args count: {e.Args.Length}");
            if (e.Args.Length > 0)
            {
                string arg = e.Args[0].ToLower();
                Logger.Log($"[APP] Processing argument: {arg}");
                if (arg == "realistic")
                {
                    Logger.Log("[APP] Starting Realistic Simulator from command line");
                    var realisticWindow = new RealisticSimulatorWindow();
                    MainWindow = realisticWindow;
                    realisticWindow.Show();
                }
                else if (arg == "xstate")
                {
                    Logger.Log("[APP] Starting XState Simulator from command line");
                    var mainWindow = new MainWindow();
                    MainWindow = mainWindow;
                    mainWindow.Show();
                }
                else if (arg == "timeline")
                {
                    Logger.Log("[APP] Starting Timeline Window from command line");
                    var timelineWindow = new TimelineWPF.TimelineWindow();
                    MainWindow = timelineWindow;
                    timelineWindow.Show();
                }
                else
                {
                    // Unknown argument, show selection dialog
                    Logger.Log($"[APP] Unknown argument '{arg}', showing selection dialog");
                    ShowSelectionDialog();
                }
            }
            else
            {
                // No arguments, show selection dialog
                Logger.Log("[APP] No arguments, showing selection dialog");
                ShowSelectionDialog();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error starting application: {ex.Message}", "Startup Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
    
    private void ShowSelectionDialog()
    {
        Logger.Log("[APP] ShowSelectionDialog called");
        // Prevent auto-shutdown while dialog is open
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Logger.Log("[APP] ShutdownMode set to OnExplicitShutdown");
        
        var selectionWindow = new SimulatorSelectionWindow();
        Logger.Log("[APP] Showing selection dialog...");
        bool? result = selectionWindow.ShowDialog();
        Logger.Log($"[APP] Dialog result: {result}");
        
        if (result == true)
        {
            // User made a selection
            Logger.Log($"[APP] User selected: {selectionWindow.SelectedSimulator}");
            if (selectionWindow.SelectedSimulator == "XState")
            {
                Logger.Log("[APP] Creating XState MainWindow");
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
                Logger.Log("[APP] XState MainWindow shown");
            }
            else if (selectionWindow.SelectedSimulator == "Realistic")
            {
                Logger.Log("[APP] Creating Realistic SimulatorWindow");
                var realisticWindow = new RealisticSimulatorWindow();
                MainWindow = realisticWindow;
                realisticWindow.Show();
                Logger.Log("[APP] Realistic SimulatorWindow shown");
            }
            
            // Switch back to normal shutdown mode
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Logger.Log("[APP] ShutdownMode set back to OnMainWindowClose");
        }
        else
        {
            // User cancelled - shutdown the application
            Logger.Log("[APP] User cancelled selection, shutting down");
            Shutdown();
        }
    }
}

