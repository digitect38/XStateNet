using System.IO;
using System.Runtime.CompilerServices;

namespace CMPSimXS2.WPF.Helpers;

/// <summary>
/// Unified singleton logger for CMP Simulator.
/// Provides centralized logging with compiler-provided caller information.
/// Thread-safe singleton pattern with double-checked locking.
/// </summary>
public sealed class Logger
{
    private static Logger? _instance;
    private static readonly object _lock = new object();
    private readonly string _logFilePath;
    private readonly object _fileLock = new object();
    private DateTime _simulationStartTime;

    /// <summary>
    /// Private constructor to prevent external instantiation
    /// </summary>
    private Logger()
    {
        _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recent processing history.log");
        _simulationStartTime = DateTime.Now;
    }

    /// <summary>
    /// Get the singleton instance of the logger
    /// </summary>
    public static Logger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new Logger();
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

            var header = $@"?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═??
                            CMP Simulator - Processing History Log
                            Session Start: {_simulationStartTime:yyyy-MM-dd HH:mm:ss.fff}
                            Configuration: CMP_Forward_Priority_Scheduling v1.0.0
                            Total Wafers: {totalWafers}
                            Log File: {_logFilePath}
                            ?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═?�═??
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
    /// Thread-safe logging to file with caller information from compiler attributes
    /// Format: [timestamp] [filename.cs.......................lineNumber] message (40 chars)
    /// </summary>
    /// <param name="message">The message to log</param>
    /// <param name="file">Caller file path (auto-populated by compiler)</param>
    /// <param name="line">Caller line number (auto-populated by compiler)</param>
    public void Log(string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        try
        {
            lock (_fileLock)
            {
                // Calculate elapsed time since simulation start
                var elapsed = DateTime.Now - _simulationStartTime;
                string timestamp = $"[{elapsed.TotalSeconds:000.000}] ";

                // Extract filename from full path
                string callerInfo = "Unknown";
                int lineNumber = line;

                if (!string.IsNullOrEmpty(file))
                {
                    // Use only filename (e.g., "CleanerMachine.cs")
                    callerInfo = Path.GetFileName(file);
                }

                // Format the caller info in a 40-character space: [filename.cs........lineNumber]
                // Left-align callerInfo, right-align lineNumber with 4-digit format (0000)
                string lineNumberStr = lineNumber.ToString("D4");
                int dotsNeeded = 40 - callerInfo.Length - lineNumberStr.Length;

                // Ensure we have at least 1 dot, and truncate callerInfo if too long
                if (dotsNeeded < 1)
                {
                    int maxCallerInfoLength = 40 - lineNumberStr.Length - 1;
                    if (maxCallerInfoLength > 0)
                    {
                        callerInfo = callerInfo.Substring(0, maxCallerInfoLength);
                        dotsNeeded = 1;
                    }
                    else
                    {
                        callerInfo = "";
                        dotsNeeded = 40 - lineNumberStr.Length;
                    }
                }

                string dots = new string('.', dotsNeeded);
                string callerInfoFormatted = $"[{callerInfo}{dots}{lineNumberStr}] ";

                File.AppendAllText(_logFilePath, timestamp + callerInfoFormatted + message + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // Fallback to console if file write fails
            Console.WriteLine($"[Logger] Failed to write to log file: {ex.Message}");
            Console.WriteLine($"[Logger] Message was: {message}");
        }
    }

    /// <summary>
    /// Get the log file path
    /// </summary>
    public string LogFilePath => _logFilePath;

    // Level-specific logging methods
    public void Debug(string source, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log($"[DEBUG] [{source}] {message}", file, line);

    public void Info(string source, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log($"[INFO] [{source}] {message}", file, line);

    public void Warning(string source, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log($"[WARN] [{source}] {message}", file, line);

    public void Error(string source, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => Log($"[ERROR] [{source}] {message}", file, line);
}
