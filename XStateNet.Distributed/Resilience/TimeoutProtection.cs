using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// High-performance timeout protection with cancellation and monitoring
    /// </summary>
    public sealed class TimeoutProtection : ITimeoutProtection
    {
        private readonly TimeoutOptions _options;
        private readonly ITimeoutMetrics _metrics;
        private readonly ILogger<TimeoutProtection>? _logger;

        // Timeout tracking
        private readonly ConcurrentDictionary<string, TimeoutContext> _activeOperations;
        private readonly Timer _monitoringTimer;

        // Performance counters
        private long _totalOperations;
        private long _totalTimeouts;
        private long _totalCompletions;
        private long _totalCancellations;

        public TimeoutProtection(
            TimeoutOptions options,
            ITimeoutMetrics? metrics = null,
            ILogger<TimeoutProtection>? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics ?? new NullTimeoutMetrics();
            _logger = logger;

            _activeOperations = new ConcurrentDictionary<string, TimeoutContext>();

            // Start monitoring timer for detecting stuck operations
            _monitoringTimer = new Timer(
                MonitorActiveOperations,
                null,
                _options.MonitoringInterval,
                _options.MonitoringInterval);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan? timeout = null,
            string? operationName = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveTimeout = timeout ?? _options.DefaultTimeout;
            operationName ??= operation.Method.Name;
            var operationId = Guid.NewGuid().ToString();

            Interlocked.Increment(ref _totalOperations);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            var context = new TimeoutContext
            {
                OperationId = operationId,
                OperationName = operationName,
                StartTime = DateTime.UtcNow,
                Timeout = effectiveTimeout,
                CancellationTokenSource = cts
            };

            _activeOperations.TryAdd(operationId, context);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await operation(cts.Token).ConfigureAwait(false);

                Interlocked.Increment(ref _totalCompletions);
                _metrics.RecordSuccess(operationName, stopwatch.Elapsed);

                return result;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _totalTimeouts);
                _metrics.RecordTimeout(operationName, effectiveTimeout);

                _logger?.LogWarning("Operation '{OperationName}' timed out after {Timeout}ms",
                    operationName, effectiveTimeout.TotalMilliseconds);

                throw new TimeoutException($"Operation '{operationName}' timed out after {effectiveTimeout.TotalSeconds:F1} seconds");
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _totalCancellations);
                _metrics.RecordCancellation(operationName, stopwatch.Elapsed);
                throw;
            }
            catch (Exception ex)
            {
                _metrics.RecordFailure(operationName, stopwatch.Elapsed, ex.GetType().Name);
                throw;
            }
            finally
            {
                _activeOperations.TryRemove(operationId, out _);
                context.Dispose();
            }
        }

        public async Task<T> ExecuteAsync<T>(
            Func<T> operation,
            TimeSpan? timeout = null,
            string? operationName = null,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(
                ct => Task.FromResult(operation()),
                timeout,
                operationName,
                cancellationToken);
        }

        public async Task<TimeoutResult<T>> TryExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan? timeout = null,
            string? operationName = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await ExecuteAsync(operation, timeout, operationName, cancellationToken);
                return TimeoutResult<T>.Success(result);
            }
            catch (TimeoutException ex)
            {
                return TimeoutResult<T>.TimedOut(ex);
            }
            catch (OperationCanceledException)
            {
                return TimeoutResult<T>.Cancelled();
            }
            catch (Exception ex)
            {
                return TimeoutResult<T>.Failed(ex);
            }
        }

        public ITimeoutScope CreateScope(TimeSpan timeout, string? scopeName = null)
        {
            return new TimeoutScope(timeout, scopeName, _metrics, _logger);
        }

        public async Task<T> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout,
            int maxRetries = 3,
            string? operationName = null,
            CancellationToken cancellationToken = default)
        {
            operationName ??= operation.Method.Name;
            var lastException = default(Exception);

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Increase timeout for each retry
                    var attemptTimeout = TimeSpan.FromMilliseconds(
                        timeout.TotalMilliseconds * Math.Pow(_options.RetryTimeoutMultiplier, attempt));

                    var result = await ExecuteAsync(operation, attemptTimeout, operationName, cancellationToken);

                    if (attempt > 0)
                    {
                        _metrics.RecordRetrySuccess(operationName, attempt);
                    }

                    return result;
                }
                catch (TimeoutException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    _metrics.RecordRetryAttempt(operationName, attempt + 1);

                    var delay = TimeSpan.FromMilliseconds(
                        _options.RetryDelay.TotalMilliseconds * Math.Pow(2, attempt));

                    _logger?.LogWarning("Operation '{OperationName}' timed out on attempt {Attempt}. Retrying after {Delay}ms",
                        operationName, attempt + 1, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                }
                catch
                {
                    throw;
                }
            }

            throw new TimeoutException(
                $"Operation '{operationName}' timed out after {maxRetries + 1} attempts",
                lastException);
        }

        public TimeoutStatistics GetStatistics()
        {
            var activeOps = _activeOperations.Values;
            var now = DateTime.UtcNow;

            return new TimeoutStatistics
            {
                TotalOperations = _totalOperations,
                TotalTimeouts = _totalTimeouts,
                TotalCompletions = _totalCompletions,
                TotalCancellations = _totalCancellations,
                TimeoutRate = _totalOperations > 0
                    ? (double)_totalTimeouts / _totalOperations
                    : 0,
                ActiveOperations = _activeOperations.Count,
                LongestRunningOperation = activeOps.Count > 0
                    ? activeOps.Max(op => now - op.StartTime)
                    : TimeSpan.Zero
            };
        }

        private void MonitorActiveOperations(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var stuckOperations = new List<TimeoutContext>();

                foreach (var kvp in _activeOperations)
                {
                    var context = kvp.Value;
                    var elapsed = now - context.StartTime;

                    // Detect stuck operations (running longer than 2x timeout)
                    if (elapsed > context.Timeout.Add(context.Timeout))
                    {
                        stuckOperations.Add(context);
                    }
                    // Warn about long-running operations
                    else if (elapsed > context.Timeout.Multiply(0.8))
                    {
                        _logger?.LogWarning("Operation '{OperationName}' ({OperationId}) is running for {Elapsed:F1}s (timeout: {Timeout:F1}s)",
                            context.OperationName, context.OperationId,
                            elapsed.TotalSeconds, context.Timeout.TotalSeconds);
                    }
                }

                // Handle stuck operations
                foreach (var stuck in stuckOperations)
                {
                    _logger?.LogError("Operation '{OperationName}' ({OperationId}) is stuck! Running for {Elapsed:F1}s",
                        stuck.OperationName, stuck.OperationId,
                        (now - stuck.StartTime).TotalSeconds);

                    _metrics.RecordStuckOperation(stuck.OperationName);

                    // Force cancellation if enabled
                    if (_options.ForceTimeoutOnStuckOperations)
                    {
                        stuck.CancellationTokenSource?.Cancel();
                        _activeOperations.TryRemove(stuck.OperationId, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in timeout monitoring");
            }
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();

            // Cancel all active operations
            foreach (var context in _activeOperations.Values)
            {
                context.CancellationTokenSource?.Cancel();
                context.Dispose();
            }

            _activeOperations.Clear();
        }
    }

    /// <summary>
    /// Scoped timeout protection for hierarchical timeout management
    /// </summary>
    public sealed class TimeoutScope : ITimeoutScope
    {
        private readonly TimeSpan _timeout;
        private readonly string? _scopeName;
        private readonly ITimeoutMetrics _metrics;
        private readonly ILogger? _logger;
        private readonly CancellationTokenSource _cts;
        private readonly Stopwatch _stopwatch;
        private readonly List<ITimeoutScope> _childScopes;
        private bool _disposed;

        public CancellationToken Token => _cts.Token;
        public TimeSpan Remaining => _timeout - _stopwatch.Elapsed;
        public bool IsTimedOut => _cts.IsCancellationRequested;

        public TimeoutScope(
            TimeSpan timeout,
            string? scopeName,
            ITimeoutMetrics metrics,
            ILogger? logger)
        {
            _timeout = timeout;
            _scopeName = scopeName;
            _metrics = metrics;
            _logger = logger;
            _cts = new CancellationTokenSource(timeout);
            _stopwatch = Stopwatch.StartNew();
            _childScopes = new List<ITimeoutScope>();
        }

        public ITimeoutScope CreateChildScope(TimeSpan timeout, string? scopeName = null)
        {
            ThrowIfDisposed();

            var effectiveTimeout = TimeSpan.FromMilliseconds(
                Math.Min(timeout.TotalMilliseconds, Remaining.TotalMilliseconds));

            var childScope = new TimeoutScope(effectiveTimeout, scopeName, _metrics, _logger);
            _childScopes.Add(childScope);

            return childScope;
        }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string? operationName = null)
        {
            ThrowIfDisposed();

            if (_cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Scope '{_scopeName}' has already timed out");
            }

            operationName ??= operation.Method.Name;
            var operationStopwatch = Stopwatch.StartNew();

            try
            {
                var result = await operation(_cts.Token).ConfigureAwait(false);
                _metrics.RecordScopeSuccess(_scopeName ?? "unnamed", operationName, operationStopwatch.Elapsed);
                return result;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                _metrics.RecordScopeTimeout(_scopeName ?? "unnamed", operationName);
                throw new TimeoutException($"Operation '{operationName}' timed out in scope '{_scopeName}'");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TimeoutScope));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _stopwatch.Stop();

            // Dispose child scopes
            foreach (var child in _childScopes)
            {
                child.Dispose();
            }

            _cts.Dispose();

            if (!_cts.IsCancellationRequested)
            {
                _logger?.LogDebug("Timeout scope '{ScopeName}' completed in {Elapsed:F1}s (timeout: {Timeout:F1}s)",
                    _scopeName, _stopwatch.Elapsed.TotalSeconds, _timeout.TotalSeconds);
            }
        }
    }

    /// <summary>
    /// Adaptive timeout manager that adjusts timeouts based on historical performance
    /// </summary>
    public sealed class AdaptiveTimeoutManager : IAdaptiveTimeoutManager
    {
        private readonly ConcurrentDictionary<string, OperationStatistics> _operationStats;
        private readonly AdaptiveTimeoutOptions _options;
        private readonly ILogger<AdaptiveTimeoutManager>? _logger;

        public AdaptiveTimeoutManager(
            AdaptiveTimeoutOptions options,
            ILogger<AdaptiveTimeoutManager>? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
            _operationStats = new ConcurrentDictionary<string, OperationStatistics>();
        }

        public TimeSpan GetTimeout(string operationName)
        {
            if (!_operationStats.TryGetValue(operationName, out var stats))
            {
                return _options.DefaultTimeout;
            }

            // Calculate adaptive timeout based on percentile
            var percentileTime = stats.GetPercentile(_options.TargetPercentile);

            // Apply safety margin
            var adaptiveTimeout = TimeSpan.FromMilliseconds(
                percentileTime.TotalMilliseconds * _options.SafetyMargin);

            // Enforce bounds
            if (adaptiveTimeout < _options.MinTimeout)
                return _options.MinTimeout;

            if (adaptiveTimeout > _options.MaxTimeout)
                return _options.MaxTimeout;

            return adaptiveTimeout;
        }

        public void RecordExecution(string operationName, TimeSpan duration, bool success)
        {
            var stats = _operationStats.GetOrAdd(operationName, _ => new OperationStatistics());
            stats.Record(duration, success);

            // Periodically clean old data
            if (stats.TotalSamples % 100 == 0)
            {
                stats.CleanOldData(_options.DataRetention);
            }
        }

        public void AdjustTimeout(string operationName, double factor)
        {
            if (_operationStats.TryGetValue(operationName, out var stats))
            {
                stats.AdjustBaseline(factor);

                _logger?.LogInformation("Adjusted timeout for '{OperationName}' by factor {Factor:F2}",
                    operationName, factor);
            }
        }

        public AdaptiveTimeoutStatistics GetStatistics(string operationName)
        {
            if (!_operationStats.TryGetValue(operationName, out var stats))
            {
                return new AdaptiveTimeoutStatistics
                {
                    OperationName = operationName,
                    CurrentTimeout = _options.DefaultTimeout,
                    SampleCount = 0
                };
            }

            return new AdaptiveTimeoutStatistics
            {
                OperationName = operationName,
                CurrentTimeout = GetTimeout(operationName),
                AverageDuration = stats.GetAverage(),
                P50Duration = stats.GetPercentile(0.5),
                P95Duration = stats.GetPercentile(0.95),
                P99Duration = stats.GetPercentile(0.99),
                SuccessRate = stats.SuccessRate,
                SampleCount = stats.TotalSamples
            };
        }
    }

    // Supporting classes
    internal class TimeoutContext : IDisposable
    {
        public string OperationId { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public TimeSpan Timeout { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }

        public void Dispose()
        {
            CancellationTokenSource?.Dispose();
        }
    }

    internal class OperationStatistics
    {
        private readonly object _lock = new();
        private readonly List<(DateTime Timestamp, TimeSpan Duration, bool Success)> _samples;
        private double _baselineMultiplier = 1.0;

        public int TotalSamples => _samples.Count;
        public double SuccessRate => _samples.Count > 0
            ? _samples.Count(s => s.Success) / (double)_samples.Count
            : 1.0;

        public OperationStatistics()
        {
            _samples = new List<(DateTime, TimeSpan, bool)>();
        }

        public void Record(TimeSpan duration, bool success)
        {
            lock (_lock)
            {
                _samples.Add((DateTime.UtcNow, duration, success));

                // Keep only recent samples (max 1000)
                if (_samples.Count > 1000)
                {
                    _samples.RemoveAt(0);
                }
            }
        }

        public TimeSpan GetPercentile(double percentile)
        {
            lock (_lock)
            {
                if (_samples.Count == 0)
                    return TimeSpan.Zero;

                var sorted = _samples
                    .OrderBy(s => s.Duration)
                    .Select(s => s.Duration)
                    .ToList();

                var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
                index = Math.Max(0, Math.Min(index, sorted.Count - 1));

                return TimeSpan.FromMilliseconds(
                    sorted[index].TotalMilliseconds * _baselineMultiplier);
            }
        }

        public TimeSpan GetAverage()
        {
            lock (_lock)
            {
                if (_samples.Count == 0)
                    return TimeSpan.Zero;

                var avg = _samples.Average(s => s.Duration.TotalMilliseconds);
                return TimeSpan.FromMilliseconds(avg * _baselineMultiplier);
            }
        }

        public void AdjustBaseline(double factor)
        {
            lock (_lock)
            {
                _baselineMultiplier *= factor;
            }
        }

        public void CleanOldData(TimeSpan retention)
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow - retention;
                _samples.RemoveAll(s => s.Timestamp < cutoff);
            }
        }
    }

    // Interfaces
    public interface ITimeoutProtection : IDisposable
    {
        Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            TimeSpan? timeout = null, string? operationName = null,
            CancellationToken cancellationToken = default);

        Task<T> ExecuteAsync<T>(Func<T> operation,
            TimeSpan? timeout = null, string? operationName = null,
            CancellationToken cancellationToken = default);

        Task<TimeoutResult<T>> TryExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            TimeSpan? timeout = null, string? operationName = null,
            CancellationToken cancellationToken = default);

        Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation,
            TimeSpan timeout, int maxRetries = 3, string? operationName = null,
            CancellationToken cancellationToken = default);

        ITimeoutScope CreateScope(TimeSpan timeout, string? scopeName = null);

        TimeoutStatistics GetStatistics();
    }

    public interface ITimeoutScope : IDisposable
    {
        CancellationToken Token { get; }
        TimeSpan Remaining { get; }
        bool IsTimedOut { get; }

        ITimeoutScope CreateChildScope(TimeSpan timeout, string? scopeName = null);
        Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, string? operationName = null);
    }

    public interface IAdaptiveTimeoutManager
    {
        TimeSpan GetTimeout(string operationName);
        void RecordExecution(string operationName, TimeSpan duration, bool success);
        void AdjustTimeout(string operationName, double factor);
        AdaptiveTimeoutStatistics GetStatistics(string operationName);
    }

    // Options and results
    public class TimeoutOptions
    {
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(10);
        public bool ForceTimeoutOnStuckOperations { get; set; } = false;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public double RetryTimeoutMultiplier { get; set; } = 1.5;
    }

    public class AdaptiveTimeoutOptions
    {
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan MinTimeout { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public double TargetPercentile { get; set; } = 0.95;
        public double SafetyMargin { get; set; } = 1.2;
        public TimeSpan DataRetention { get; set; } = TimeSpan.FromHours(1);
    }

    public class TimeoutResult<T>
    {
        public bool IsSuccess { get; }
        public bool IsTimeout { get; }
        public bool IsCancelled { get; }
        public T? Value { get; }
        public string? Error { get; }
        public Exception? Exception { get; }

        private TimeoutResult(bool success, bool timeout, bool cancelled, T? value, string? error, Exception? exception)
        {
            IsSuccess = success;
            IsTimeout = timeout;
            IsCancelled = cancelled;
            Value = value;
            Error = error;
            Exception = exception;
        }

        public static TimeoutResult<T> Success(T value) =>
            new(true, false, false, value, null, null);

        public static TimeoutResult<T> TimedOut(TimeoutException ex) =>
            new(false, true, false, default, ex.Message, ex);

        public static TimeoutResult<T> Cancelled() =>
            new(false, false, true, default, "Operation was cancelled", null);

        public static TimeoutResult<T> Failed(Exception exception) =>
            new(false, false, false, default, exception.Message, exception);
    }

    public class TimeoutStatistics
    {
        public long TotalOperations { get; set; }
        public long TotalTimeouts { get; set; }
        public long TotalCompletions { get; set; }
        public long TotalCancellations { get; set; }
        public double TimeoutRate { get; set; }
        public int ActiveOperations { get; set; }
        public TimeSpan LongestRunningOperation { get; set; }
    }

    public class AdaptiveTimeoutStatistics
    {
        public string OperationName { get; set; } = string.Empty;
        public TimeSpan CurrentTimeout { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public TimeSpan P50Duration { get; set; }
        public TimeSpan P95Duration { get; set; }
        public TimeSpan P99Duration { get; set; }
        public double SuccessRate { get; set; }
        public int SampleCount { get; set; }
    }

    // Metrics interface
    public interface ITimeoutMetrics
    {
        void RecordSuccess(string operationName, TimeSpan duration);
        void RecordTimeout(string operationName, TimeSpan timeout);
        void RecordCancellation(string operationName, TimeSpan elapsed);
        void RecordFailure(string operationName, TimeSpan elapsed, string errorType);
        void RecordRetryAttempt(string operationName, int attemptNumber);
        void RecordRetrySuccess(string operationName, int totalAttempts);
        void RecordStuckOperation(string operationName);
        void RecordScopeSuccess(string scopeName, string operationName, TimeSpan duration);
        void RecordScopeTimeout(string scopeName, string operationName);
    }

    internal class NullTimeoutMetrics : ITimeoutMetrics
    {
        public void RecordSuccess(string operationName, TimeSpan duration) { }
        public void RecordTimeout(string operationName, TimeSpan timeout) { }
        public void RecordCancellation(string operationName, TimeSpan elapsed) { }
        public void RecordFailure(string operationName, TimeSpan elapsed, string errorType) { }
        public void RecordRetryAttempt(string operationName, int attemptNumber) { }
        public void RecordRetrySuccess(string operationName, int totalAttempts) { }
        public void RecordStuckOperation(string operationName) { }
        public void RecordScopeSuccess(string scopeName, string operationName, TimeSpan duration) { }
        public void RecordScopeTimeout(string scopeName, string operationName) { }
    }
}