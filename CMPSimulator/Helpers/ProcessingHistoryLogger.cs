using System.IO;
using System.Diagnostics;

namespace CMPSimulator.Helpers;

/// <summary>
/// Singleton logger for processing history.
/// Ensures all components write to the same log file with a single, consistent timeline.
/// Thread-safe singleton pattern with double-checked locking.
/// </summary>
public sealed class ProcessingHistoryLogger
{
    private static ProcessingHistoryLogger? _instance;
    private static readonly object _lock = new object();
    private readonly string _logFilePath;
    private readonly object _fileLock = new object();
    private DateTime _simulationStartTime;

    /// <summary>
    /// Private constructor to prevent external instantiation
    /// </summary>
    private ProcessingHistoryLogger()
    {
        _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recent processing history.log");
        _simulationStartTime = DateTime.Now;
    }

    /// <summary>
    /// Get the singleton instance of the logger
    /// </summary>
    public static ProcessingHistoryLogger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new ProcessingHistoryLogger();
                    }
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Initialize the log file with a header
    /// Call this when starting a new simulation session
    /// </summary>
    public void InitializeLog(int totalWafers)
    {
        lock (_fileLock)
        {
            _simulationStartTime = DateTime.Now;

            var header = $@"═══════════════════════════════════════════════════════════
                            CMP Simulator - Processing History Log
                            Session Start: {_simulationStartTime:yyyy-MM-dd HH:mm:ss.fff}
                            Configuration: CMP_Forward_Priority_Scheduling v1.0.0
                            Total Wafers: {totalWafers}
                            Log File: {_logFilePath}
                            ═══════════════════════════════════════════════════════════
                            ";
            File.WriteAllText(_logFilePath, header);
        }
    }

    /// <summary>
    /// Reset the simulation start time
    /// Call this when resetting the simulation
    /// </summary>
    public void ResetSimulationStartTime()
    {
        lock (_fileLock)
        {
            _simulationStartTime = DateTime.Now;
        }
    }

    /// <summary>
    /// Log a message with timestamp relative to simulation start
    /// Thread-safe logging to file
    /// Format: [timestamp] [fileName........................lineNumber] message (40 chars)
    /// Uses reflection to capture actual caller information from stack trace
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Log(string message)
    {
        try
        {
            lock (_fileLock)
            {
                // Calculate elapsed time since simulation start
                var elapsed = DateTime.Now - _simulationStartTime;
                string timestamp = $"[{elapsed.TotalSeconds:000.000}] ";

                // Use StackTrace to get caller information via reflection
                // Skip 1 frame to get past Log() method, then get frame 0 to get the actual caller
                // (the code that called the method that called ProcessingHistoryLogger.Instance.Log())
                var stackTrace = new StackTrace(2, true);
                var frame = stackTrace.GetFrame(0);

                string fileName = "Unknown";
                int lineNumber = 0;

                if (frame != null)
                {
                    // Get the filename (without path, but with extension)
                    var fullPath = frame.GetFileName();
                    if (!string.IsNullOrEmpty(fullPath))
                    {
                        fileName = Path.GetFileName(fullPath);
                    }
                    else
                    {
                        // Fallback to method name if file info not available
                        var method = frame.GetMethod();
                        if (method != null && method.DeclaringType != null)
                        {
                            fileName = method.DeclaringType.Name;
                        }
                    }

                    lineNumber = frame.GetFileLineNumber();
                }

                // Format the caller info in a 40-character space: [fileName........lineNumber]
                // Left-align fileName, right-align lineNumber with 4-digit format (0000)
                string lineNumberStr = lineNumber.ToString("D4");
                int dotsNeeded = 40 - fileName.Length - lineNumberStr.Length;

                // Ensure we have at least 1 dot, and truncate filename if too long
                if (dotsNeeded < 1)
                {
                    int maxFileNameLength = 40 - lineNumberStr.Length - 1;
                    if (maxFileNameLength > 0)
                    {
                        fileName = fileName.Substring(0, maxFileNameLength);
                        dotsNeeded = 1;
                    }
                    else
                    {
                        fileName = "";
                        dotsNeeded = 40 - lineNumberStr.Length;
                    }
                }

                string dots = new string('.', dotsNeeded);
                string callerInfo = $"[{fileName}{dots}{lineNumberStr}] ";

                File.AppendAllText(_logFilePath, timestamp + callerInfo + message + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // Fallback to console if file write fails
            Console.WriteLine($"[ProcessingHistoryLogger] Failed to write to log file: {ex.Message}");
            Console.WriteLine($"[ProcessingHistoryLogger] Message was: {message}");
        }
    }

    /// <summary>
    /// Get the log file path
    /// </summary>
    public string LogFilePath => _logFilePath;
}
