using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;

namespace CMPSimXS2.Helpers;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class LogEntry : INotifyPropertyChanged
{
    private DateTime _timestamp;
    private LogLevel _level;
    private string _source = string.Empty;
    private string _message = string.Empty;

    public DateTime Timestamp
    {
        get => _timestamp;
        set => SetProperty(ref _timestamp, value);
    }

    public LogLevel Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    public string Source
    {
        get => _source;
        set => SetProperty(ref _source, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] [{Level}] [{Source}] {Message}";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public class Logger
{
    private static Logger? _instance;
    private static readonly object _lock = new object();
    private static readonly object _fileLock = new object();
    private const string LogFileName = "CMPSimXS2.log";
    private readonly string _logFilePath;

    public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();

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

    private Logger()
    {
        // Private constructor for singleton
        // Get the log file path in the application directory
        _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFileName);

        // Clear old log file on startup
        InitializeLogFile();
    }

    private void InitializeLogFile()
    {
        try
        {
            lock (_fileLock)
            {
                // Delete old log file if it exists
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }

                // Create new log file with header
                var header = $"=== CMPSimXS2 Log File ==={Environment.NewLine}" +
                           $"Session started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}" +
                           $"Log file: {_logFilePath}{Environment.NewLine}" +
                           $"==========================================={Environment.NewLine}{Environment.NewLine}";

                File.WriteAllText(_logFilePath, header);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize log file: {ex.Message}");
        }
    }

    private void WriteToFile(string formattedMessage)
    {
        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write to log file: {ex.Message}");
        }
    }

    public void Log(LogLevel level, string source, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message
        };

        // Add to collection on UI thread if needed
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Logs.Add(entry);

            // Keep only last 1000 entries to prevent memory issues
            if (Logs.Count > 1000)
            {
                Logs.RemoveAt(0);
            }
        });

        // Write to console for debugging
        Console.WriteLine(entry.FormattedMessage);

        // Write to file
        WriteToFile(entry.FormattedMessage);
    }

    public void Debug(string source, string message) => Log(LogLevel.Debug, source, message);
    public void Info(string source, string message) => Log(LogLevel.Info, source, message);
    public void Warning(string source, string message) => Log(LogLevel.Warning, source, message);
    public void Error(string source, string message) => Log(LogLevel.Error, source, message);

    public void Clear()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Logs.Clear();
        });
    }

    public string GetLogFilePath() => _logFilePath;
}
