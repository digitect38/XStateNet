using System.Text.RegularExpressions;

namespace XStateNet;

/// <summary>
/// Security utilities for XStateNet
/// </summary>
public static class Security
{
    // Maximum file size (10MB)
    private const long MaxFileSize = 10 * 1024 * 1024;

    // Maximum JSON complexity depth
    private const int MaxJsonDepth = 100;

    // Regex timeout (5 seconds - increased for complex state machines)
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Validates and sanitizes file paths to prevent path traversal attacks
    /// </summary>
    public static string ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty");

        // Get the full path and ensure it's within expected boundaries
        var fullPath = Path.GetFullPath(filePath);

        // Check for path traversal attempts
        if (filePath.Contains("..") || filePath.Contains("~"))
            throw new SecurityException("Invalid file path: potential path traversal detected");

        // Ensure file exists
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {Path.GetFileName(fullPath)}");

        // Check file size
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > MaxFileSize)
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {MaxFileSize} bytes");

        return fullPath;
    }

    /// <summary>
    /// Safely read file with size and path validation
    /// </summary>
    public static string SafeReadFile(string filePath)
    {
        var validPath = ValidateFilePath(filePath);
        return File.ReadAllText(validPath);
    }

    /// <summary>
    /// Validates JSON input size and complexity
    /// </summary>
    public static void ValidateJsonInput(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON input cannot be null or empty");

        // Check size
        if (json.Length > MaxFileSize)
            throw new InvalidOperationException($"JSON input exceeds maximum allowed size");

        // Basic complexity check (count depth by brackets)
        int depth = 0;
        int maxDepth = 0;

        foreach (char c in json)
        {
            if (c == '{' || c == '[')
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
            }
            else if (c == '}' || c == ']')
            {
                depth--;
            }

            if (maxDepth > MaxJsonDepth)
                throw new InvalidOperationException($"JSON complexity exceeds maximum allowed depth of {MaxJsonDepth}");
        }

        if (depth != 0)
            throw new InvalidOperationException("Invalid JSON structure: unmatched brackets");
    }

    /// <summary>
    /// Create a safe regex pattern with timeout
    /// </summary>
    public static Regex CreateSafeRegex(string pattern, RegexOptions options = RegexOptions.None)
    {
        try
        {
            // Add timeout to prevent ReDoS
            return new Regex(pattern, options | RegexOptions.Compiled, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("Invalid regex pattern", ex);
        }
    }

    /// <summary>
    /// Exception for security-related issues
    /// </summary>
    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
        public SecurityException(string message, Exception inner) : base(message, inner) { }
    }
}

/// <summary>
/// Configurable logging system
/// </summary>
public static class Logger
{
    public enum LogLevel
    {
        None = 0,
        Error = 1,
        Warning = 2,
        Info = 3,
        Debug = 4,
        Verbose = 5,
        Trace = 6
    }

    private static int _currentLevelInt = (int)LogLevel.Warning;
    private static readonly object _lockObject = new object();
    private static int _includeCallerInfoInt = 0; // 0 = false, 1 = true

    public static LogLevel CurrentLevel
    {
        get => (LogLevel)Interlocked.CompareExchange(ref _currentLevelInt, 0, 0);
        set => Interlocked.Exchange(ref _currentLevelInt, (int)value);
    }

    public static bool IncludeCallerInfo
    {
        get => Interlocked.CompareExchange(ref _includeCallerInfoInt, 0, 0) == 1;
        set => Interlocked.Exchange(ref _includeCallerInfoInt, value ? 1 : 0);
    }

    public static void Log(string message, LogLevel level = LogLevel.Info,
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        if (level <= CurrentLevel)
        {
            lock (_lockObject)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                if (IncludeCallerInfo && !string.IsNullOrEmpty(filePath))
                {
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    // Format: Fixed width for better alignment
                    // File: 20 chars, Line: 4 chars
                    var fileInfo = fileName.Length > 20 ? fileName.Substring(0, 20) : fileName.PadRight(20);
                    var lineInfo = lineNumber.ToString().PadLeft(4);

                    Console.WriteLine($"[{timestamp}] [{level,-7}] [{fileInfo}:{lineInfo}] {message}");
                }
                else
                {
                    Console.WriteLine($"[{timestamp}] [{level,-7}] {message}");
                }
            }
        }
    }

    public static void Error(string message,
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        => Log(message, LogLevel.Error, memberName, filePath, lineNumber);

    public static void Warning(string message,
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        => Log(message, LogLevel.Warning, memberName, filePath, lineNumber);

    public static void Info(string message,
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        => Log(message, LogLevel.Info, memberName, filePath, lineNumber);

    public static void Debug(string message,
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        => Log(message, LogLevel.Debug, memberName, filePath, lineNumber);

    public static void Verbose(string message,
        [System.Runtime.CompilerServices.CallerMemberName] string memberName = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
        => Log(message, LogLevel.Verbose, memberName, filePath, lineNumber);
}