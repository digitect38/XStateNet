using System;
using System.IO;
using System.Windows;

namespace CMPSimulator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CMPSimulator_Crash.log");
            File.WriteAllText(logPath, $"CRASH: {ex?.ToString() ?? "Unknown error"}");
            MessageBox.Show($"Fatal error: {ex?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CMPSimulator_Error.log");
            File.WriteAllText(logPath, $"ERROR: {args.Exception}");
            MessageBox.Show($"Error: {args.Exception.Message}\n\n{args.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
