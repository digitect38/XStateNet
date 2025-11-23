using System.Collections.Concurrent;

namespace CMPSimXS2.EventDriven.Services;

/// <summary>
/// Centralized logger that serializes all log messages with timestamps
/// Ensures logs are printed in order without interleaving
/// </summary>
public class CMPLogger
{
    private static readonly Lazy<CMPLogger> _instance = new(() => new CMPLogger());
    private readonly BlockingCollection<LogEntry> _logQueue = new();
    private readonly Task _logTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly DateTime _startTime;

    public static CMPLogger Instance => _instance.Value;

    private CMPLogger()
    {
        _startTime = DateTime.Now;
        // Start background task to process logs
        _logTask = Task.Run(ProcessLogs);
    }

    private record LogEntry(DateTime Timestamp, string Level, string Message);

    public void Info(string message)
    {
        _logQueue.Add(new LogEntry(DateTime.Now, "INFO", message));
    }

    public void Debug(string message)
    {
        _logQueue.Add(new LogEntry(DateTime.Now, "DEBUG", message));
    }

    public void Warning(string message)
    {
        _logQueue.Add(new LogEntry(DateTime.Now, "WARN", message));
    }

    public void Error(string message)
    {
        _logQueue.Add(new LogEntry(DateTime.Now, "ERROR", message));
    }

    private void ProcessLogs()
    {
        try
        {
            foreach (var entry in _logQueue.GetConsumingEnumerable(_cts.Token))
            {
                var elapsed = entry.Timestamp - _startTime;
                var timestamp = $"{elapsed.Minutes:D2}.{elapsed.Seconds:D2}.{elapsed.Milliseconds:D3}";

                // Align component prefixes by padding to fixed width
                var alignedMessage = AlignComponentPrefix(entry.Message);

                Console.WriteLine($"[{timestamp}] {alignedMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when shutting down
        }
    }

    private string AlignComponentPrefix(string message)
    {
        // Split message into prefix and rest at first space
        var spaceIndex = message.IndexOf(' ');
        if (spaceIndex > 0)
        {
            var prefix = message.Substring(0, spaceIndex);
            var rest = message.Substring(spaceIndex + 1);

            // Pad the prefix to 10 characters for alignment
            var paddedPrefix = prefix.PadRight(10);
            return paddedPrefix + rest;
        }

        return message;
    }

    public void Shutdown()
    {
        _logQueue.CompleteAdding();
        _cts.Cancel();
        _logTask.Wait(TimeSpan.FromSeconds(2));
    }
}
