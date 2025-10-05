using System.IO;
using System.Text;

namespace SemiStandard.Simulator.Wpf
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private static readonly string LogFilePath;

        static Logger()
        {
            // Create logs directory if it doesn't exist
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            // Create log file with timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            LogFilePath = Path.Combine(LogDirectory, $"simulator_{timestamp}.log");

            // Write initial log entry
            WriteLog("=================================================");
            WriteLog($"SEMI Equipment Simulator Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteLog("=================================================");
        }

        public static void Log(string message)
        {
            string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            // Write to console
            Console.WriteLine(message);

            // Write to file
            WriteLog(logMessage);
        }

        public static void LogSection(string section)
        {
            string separator = new string('-', 50);
            Log(separator);
            Log(section);
            Log(separator);
        }

        private static void WriteLog(string message)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(LogFilePath, message + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to log file: {ex.Message}");
                }
            }
        }

        public static string GetLogFilePath()
        {
            return LogFilePath;
        }
    }
}