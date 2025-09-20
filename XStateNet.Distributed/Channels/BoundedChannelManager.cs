using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.Channels
{
    /// <summary>
    /// High-performance bounded channel manager with backpressure control
    /// </summary>
    public sealed class BoundedChannelManager<T> : IChannelManager<T>
    {
        private readonly string _channelName;
        private readonly CustomBoundedChannelOptions _options;
        private readonly Channel<T> _channel;
        private readonly IChannelMetrics _metrics;
        private readonly ILogger<BoundedChannelManager<T>>? _logger;

        // Backpressure handling
        private readonly SemaphoreSlim? _writeSemaphore;
        private readonly ConcurrentQueue<TaskCompletionSource<bool>> _waitingWriters;

        // Performance counters
        private long _totalItemsWritten;
        private long _totalItemsRead;
        private long _totalWritesFailed;
        private long _totalReadsCompleted;
        private long _totalBackpressureEvents;
        private long _totalDroppedItems;

        // Monitoring
        private readonly Timer? _monitoringTimer;
        private DateTime _lastMonitoringTime;
        private long _lastItemsWritten;
        private long _lastItemsRead;

        public ChannelReader<T> Reader => _channel.Reader;
        public ChannelWriter<T> Writer => _channel.Writer;

        public long TotalItemsWritten => _totalItemsWritten;
        public long TotalItemsRead => _totalItemsRead;
        public long CurrentQueueDepth
        {
            get
            {
                // Try to get actual count from reader if available
                try
                {
                    if (_channel.Reader.CanCount)
                    {
                        return _channel.Reader.Count;
                    }
                }
                catch { }

                // Fallback to calculated depth (may be inaccurate with drops)
                var calculated = _totalItemsWritten - _totalItemsRead - _totalDroppedItems;
                return Math.Max(0, calculated);
            }
        }
        public bool IsFull => CurrentQueueDepth >= _options.Capacity;

        public BoundedChannelManager(
            string channelName,
            CustomBoundedChannelOptions options,
            IChannelMetrics? metrics = null,
            ILogger<BoundedChannelManager<T>>? logger = null)
        {
            _channelName = channelName ?? throw new ArgumentNullException(nameof(channelName));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics ?? new NullChannelMetrics();
            _logger = logger;

            _waitingWriters = new ConcurrentQueue<TaskCompletionSource<bool>>();

            // Create the bounded channel
            var channelOptions = new System.Threading.Channels.BoundedChannelOptions(options.Capacity)
            {
                FullMode = ConvertFullMode(options.FullMode),
                SingleReader = options.SingleReader,
                SingleWriter = options.SingleWriter,
                AllowSynchronousContinuations = options.AllowSynchronousContinuations
            };

            _channel = Channel.CreateBounded<T>(channelOptions);

            // Initialize write semaphore for custom backpressure handling
            if (_options.EnableCustomBackpressure)
            {
                _writeSemaphore = new SemaphoreSlim(_options.Capacity, _options.Capacity);
            }

            // Start monitoring if enabled
            if (_options.EnableMonitoring)
            {
                _lastMonitoringTime = DateTime.UtcNow;
                _monitoringTimer = new Timer(
                    MonitoringCallback,
                    null,
                    _options.MonitoringInterval,
                    _options.MonitoringInterval);
            }

            _logger?.LogInformation("BoundedChannel '{ChannelName}' created with capacity {Capacity}",
                _channelName, _options.Capacity);
        }

        private static BoundedChannelFullMode ConvertFullMode(ChannelFullMode mode)
        {
            return mode switch
            {
                ChannelFullMode.Wait => BoundedChannelFullMode.Wait,
                ChannelFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                ChannelFullMode.DropNewest => BoundedChannelFullMode.DropNewest,
                _ => BoundedChannelFullMode.Wait
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<bool> WriteAsync(T item, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Custom backpressure handling
                if (_writeSemaphore != null)
                {
                    if (!await _writeSemaphore.WaitAsync(0, cancellationToken))
                    {
                        Interlocked.Increment(ref _totalBackpressureEvents);
                        _metrics.RecordBackpressure(_channelName);

                        // Apply backpressure strategy
                        var backpressureHandled = await HandleBackpressureAsync(item, cancellationToken);

                        // For Redirect, return immediately after redirecting
                        if (_options.BackpressureStrategy == BackpressureStrategy.Redirect)
                        {
                            return backpressureHandled;
                        }

                        // For Drop, return false to indicate the item was dropped
                        if (_options.BackpressureStrategy == BackpressureStrategy.Drop)
                        {
                            // Item has been dropped, return false
                            return false;
                        }

                        // For Throttle, we've delayed, now try to write anyway (might overwrite)
                        if (_options.BackpressureStrategy == BackpressureStrategy.Throttle)
                        {
                            // Don't wait for semaphore, just try to write
                            // The channel might use DropNewest or other full mode
                        }
                        // For Wait strategy, continue to wait for semaphore
                        else if (_options.BackpressureStrategy == BackpressureStrategy.Wait)
                        {
                            if (!backpressureHandled)
                            {
                                return false;
                            }
                            await _writeSemaphore.WaitAsync(cancellationToken);
                        }
                    }
                }

                // Try to write to channel
                while (await _channel.Writer.WaitToWriteAsync(cancellationToken))
                {
                    var depthBeforeWrite = CurrentQueueDepth;
                    var wasAtCapacity = depthBeforeWrite >= _options.Capacity;

                    if (_channel.Writer.TryWrite(item))
                    {
                        Interlocked.Increment(ref _totalItemsWritten);
                        _metrics.RecordWrite(_channelName, stopwatch.Elapsed);

                        // Check if item was dropped (DropNewest mode at capacity)
                        if (_options.FullMode == ChannelFullMode.DropNewest && wasAtCapacity)
                        {
                            Interlocked.Increment(ref _totalDroppedItems);
                            _metrics.RecordDrop(_channelName);
                        }

                        // Notify waiting readers if any
                        NotifyWaitingReaders();

                        return true;
                    }

                    // Channel is full, apply full mode strategy
                    if (!await HandleFullChannelAsync(item, cancellationToken))
                    {
                        return false;
                    }
                }

                return false;
            }
            catch (ChannelClosedException)
            {
                Interlocked.Increment(ref _totalWritesFailed);
                _metrics.RecordWriteFailure(_channelName, "ChannelClosed");
                return false;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalWritesFailed);
                _metrics.RecordWriteFailure(_channelName, ex.GetType().Name);
                _logger?.LogError(ex, "Error writing to channel '{ChannelName}'", _channelName);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWrite(T item)
        {
            try
            {
                var depthBeforeWrite = CurrentQueueDepth;
                var wasAtCapacity = depthBeforeWrite >= _options.Capacity;

                if (_channel.Writer.TryWrite(item))
                {
                    Interlocked.Increment(ref _totalItemsWritten);
                    _metrics.RecordWrite(_channelName, TimeSpan.Zero);

                    // Check if item was dropped (DropNewest mode at capacity)
                    if (_options.FullMode == ChannelFullMode.DropNewest && wasAtCapacity)
                    {
                        Interlocked.Increment(ref _totalDroppedItems);
                        _metrics.RecordDrop(_channelName);
                    }

                    NotifyWaitingReaders();
                    return true;
                }

                // Handle full channel based on mode
                if (_options.FullMode == ChannelFullMode.DropNewest)
                {
                    Interlocked.Increment(ref _totalDroppedItems);
                    _metrics.RecordDrop(_channelName);
                    return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in TryWrite for channel '{ChannelName}'", _channelName);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<(bool Success, T? Item)> ReadAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // WORKAROUND: WaitToReadAsync seems to hang even when items are available
                // Check if we can read directly first
                if (_channel.Reader.TryRead(out var item))
                {
                    Interlocked.Increment(ref _totalItemsRead);
                    _metrics.RecordRead(_channelName, stopwatch.Elapsed);

                    // Release semaphore if using custom backpressure
                    _writeSemaphore?.Release();

                    // Notify waiting writers if any
                    NotifyWaitingWriters();

                    return (true, item);
                }

                // If no items immediately available, then wait
                if (await _channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_channel.Reader.TryRead(out item))
                    {
                        Interlocked.Increment(ref _totalItemsRead);
                        _metrics.RecordRead(_channelName, stopwatch.Elapsed);

                        // Release semaphore if using custom backpressure
                        _writeSemaphore?.Release();

                        // Notify waiting writers if any
                        NotifyWaitingWriters();

                        return (true, item);
                    }
                }

                return (false, default);
            }
            catch (ChannelClosedException)
            {
                Interlocked.Increment(ref _totalReadsCompleted);
                return (false, default);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading from channel '{ChannelName}'", _channelName);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRead(out T? item)
        {
            try
            {
                if (_channel.Reader.TryRead(out item))
                {
                    Interlocked.Increment(ref _totalItemsRead);
                    _metrics.RecordRead(_channelName, TimeSpan.Zero);
                    _writeSemaphore?.Release();
                    NotifyWaitingWriters();
                    return true;
                }

                item = default;
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in TryRead for channel '{ChannelName}'", _channelName);
                item = default;
                return false;
            }
        }

        public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                Interlocked.Increment(ref _totalItemsRead);
                _writeSemaphore?.Release();
                NotifyWaitingWriters();
                yield return item;
            }
        }

        public async Task<List<T>> ReadBatchAsync(int maxItems, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var batch = new List<T>(Math.Min(maxItems, 100));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                while (batch.Count < maxItems && !cts.Token.IsCancellationRequested)
                {
                    if (_channel.Reader.TryRead(out var item))
                    {
                        batch.Add(item);
                        Interlocked.Increment(ref _totalItemsRead);
                        _writeSemaphore?.Release();
                    }
                    else
                    {
                        try
                        {
                            if (await _channel.Reader.WaitToReadAsync(cts.Token))
                            {
                                continue;
                            }
                            else
                            {
                                break;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Timeout occurred
                            break;
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    _metrics.RecordBatchRead(_channelName, batch.Count);
                    NotifyWaitingWriters();
                }

                return batch;
            }
            catch (OperationCanceledException)
            {
                // Return whatever we have collected (might be empty on timeout)
                if (batch.Count > 0)
                {
                    _metrics.RecordBatchRead(_channelName, batch.Count);
                }
                return batch;
            }
        }

        public void Complete(Exception? exception = null)
        {
            _channel.Writer.TryComplete(exception);

            // Cancel all waiting writers
            while (_waitingWriters.TryDequeue(out var tcs))
            {
                tcs.TrySetCanceled();
            }

            _logger?.LogInformation("Channel '{ChannelName}' completed. Total items: Written={Written}, Read={Read}",
                _channelName, _totalItemsWritten, _totalItemsRead);
        }

        public ChannelStatistics GetStatistics()
        {
            var now = DateTime.UtcNow;
            var timeSinceLastMonitoring = now - _lastMonitoringTime;
            var itemsWrittenSinceLastMonitoring = _totalItemsWritten - _lastItemsWritten;
            var itemsReadSinceLastMonitoring = _totalItemsRead - _lastItemsRead;

            return new ChannelStatistics
            {
                ChannelName = _channelName,
                Capacity = _options.Capacity,
                CurrentDepth = CurrentQueueDepth,
                TotalItemsWritten = _totalItemsWritten,
                TotalItemsRead = _totalItemsRead,
                TotalItemsDropped = _totalDroppedItems,
                TotalBackpressureEvents = _totalBackpressureEvents,
                TotalWritesFailed = _totalWritesFailed,
                WriteRate = timeSinceLastMonitoring.TotalSeconds > 0
                    ? itemsWrittenSinceLastMonitoring / timeSinceLastMonitoring.TotalSeconds
                    : 0,
                ReadRate = timeSinceLastMonitoring.TotalSeconds > 0
                    ? itemsReadSinceLastMonitoring / timeSinceLastMonitoring.TotalSeconds
                    : 0,
                UtilizationPercent = _options.Capacity > 0
                    ? (double)CurrentQueueDepth / _options.Capacity * 100
                    : 0,
                IsFull = IsFull,
                IsCompleted = _channel.Reader.Completion.IsCompleted
            };
        }

        private async ValueTask<bool> HandleBackpressureAsync(T item, CancellationToken cancellationToken)
        {
            switch (_options.BackpressureStrategy)
            {
                case BackpressureStrategy.Wait:
                    // Already handled by semaphore wait
                    return true;

                case BackpressureStrategy.Drop:
                    Interlocked.Increment(ref _totalDroppedItems);
                    _metrics.RecordDrop(_channelName);
                    _logger?.LogWarning("Dropped item due to backpressure in channel '{ChannelName}'", _channelName);
                    return false;

                case BackpressureStrategy.Throttle:
                    var delay = CalculateThrottleDelay();
                    await Task.Delay(delay, cancellationToken);
                    return true;

                case BackpressureStrategy.Redirect:
                    if (_options.OverflowChannel != null)
                    {
                        // Cast item to object for the overflow channel
                        return await _options.OverflowChannel.WriteAsync((object)(item!), cancellationToken);
                    }
                    return false;

                default:
                    return true;
            }
        }

        private ValueTask<bool> HandleFullChannelAsync(T item, CancellationToken cancellationToken)
        {
            switch (_options.FullMode)
            {
                case ChannelFullMode.Wait:
                    // Default behavior - wait for space
                    return ValueTask.FromResult(true);

                case ChannelFullMode.DropOldest:
                    // Try to read and discard oldest item
                    if (_channel.Reader.TryRead(out _))
                    {
                        Interlocked.Increment(ref _totalItemsRead);
                        Interlocked.Increment(ref _totalDroppedItems);
                        _metrics.RecordDrop(_channelName);
                        return ValueTask.FromResult(true);
                    }
                    return ValueTask.FromResult(false);

                case ChannelFullMode.DropNewest:
                    // Drop the current item
                    Interlocked.Increment(ref _totalDroppedItems);
                    _metrics.RecordDrop(_channelName);
                    return ValueTask.FromResult(false);

                case ChannelFullMode.Reject:
                    throw new ChannelFullException($"Channel '{_channelName}' is full");

                default:
                    return ValueTask.FromResult(false);
            }
        }

        private TimeSpan CalculateThrottleDelay()
        {
            var utilization = (double)CurrentQueueDepth / _options.Capacity;

            // Exponential throttle based on utilization
            if (utilization > 0.9)
                return TimeSpan.FromMilliseconds(100);
            if (utilization > 0.8)
                return TimeSpan.FromMilliseconds(50);
            if (utilization > 0.7)
                return TimeSpan.FromMilliseconds(20);

            return TimeSpan.FromMilliseconds(10);
        }

        private void NotifyWaitingWriters()
        {
            if (_waitingWriters.TryDequeue(out var tcs))
            {
                tcs.TrySetResult(true);
            }
        }

        private void NotifyWaitingReaders()
        {
            // Implement if needed for custom reader notification
        }

        private void MonitoringCallback(object? state)
        {
            try
            {
                var stats = GetStatistics();
                _metrics.RecordStatistics(stats);

                if (stats.UtilizationPercent > _options.HighWatermark)
                {
                    _logger?.LogWarning("Channel '{ChannelName}' high utilization: {Utilization:F1}%",
                        _channelName, stats.UtilizationPercent);
                }

                _lastMonitoringTime = DateTime.UtcNow;
                _lastItemsWritten = _totalItemsWritten;
                _lastItemsRead = _totalItemsRead;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in monitoring callback for channel '{ChannelName}'", _channelName);
            }
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _writeSemaphore?.Dispose();
            Complete();
        }
    }

    // Interfaces and supporting types
    public interface IChannelManager<T> : IDisposable
    {
        ChannelReader<T> Reader { get; }
        ChannelWriter<T> Writer { get; }

        long TotalItemsWritten { get; }
        long TotalItemsRead { get; }
        long CurrentQueueDepth { get; }
        bool IsFull { get; }

        ValueTask<bool> WriteAsync(T item, CancellationToken cancellationToken = default);
        bool TryWrite(T item);

        ValueTask<(bool Success, T? Item)> ReadAsync(CancellationToken cancellationToken = default);
        bool TryRead(out T? item);

        IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
        Task<List<T>> ReadBatchAsync(int maxItems, TimeSpan timeout, CancellationToken cancellationToken = default);

        void Complete(Exception? exception = null);
        ChannelStatistics GetStatistics();
    }

    public class CustomBoundedChannelOptions
    {
        public int Capacity { get; set; } = 1000;
        public ChannelFullMode FullMode { get; set; } = ChannelFullMode.Wait;
        public BackpressureStrategy BackpressureStrategy { get; set; } = BackpressureStrategy.Wait;
        public bool SingleReader { get; set; } = false;
        public bool SingleWriter { get; set; } = false;
        public bool AllowSynchronousContinuations { get; set; } = false;
        public bool EnableCustomBackpressure { get; set; } = false;
        public bool EnableMonitoring { get; set; } = true;
        public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(10);
        public double HighWatermark { get; set; } = 80.0; // Percent
        public double LowWatermark { get; set; } = 20.0;  // Percent
        public IChannelManager<object>? OverflowChannel { get; set; }
    }

    public enum ChannelFullMode
    {
        Wait,
        DropOldest,
        DropNewest,
        Reject
    }

    public enum BackpressureStrategy
    {
        Wait,
        Drop,
        Throttle,
        Redirect
    }

    public class ChannelStatistics
    {
        public string ChannelName { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public long CurrentDepth { get; set; }
        public long TotalItemsWritten { get; set; }
        public long TotalItemsRead { get; set; }
        public long TotalItemsDropped { get; set; }
        public long TotalBackpressureEvents { get; set; }
        public long TotalWritesFailed { get; set; }
        public double WriteRate { get; set; }
        public double ReadRate { get; set; }
        public double UtilizationPercent { get; set; }
        public bool IsFull { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class ChannelFullException : Exception
    {
        public ChannelFullException(string message) : base(message) { }
    }

    // Metrics interface
    public interface IChannelMetrics
    {
        void RecordWrite(string channelName, TimeSpan duration);
        void RecordRead(string channelName, TimeSpan duration);
        void RecordBatchRead(string channelName, int batchSize);
        void RecordDrop(string channelName);
        void RecordBackpressure(string channelName);
        void RecordWriteFailure(string channelName, string reason);
        void RecordStatistics(ChannelStatistics stats);
    }

    internal class NullChannelMetrics : IChannelMetrics
    {
        public void RecordWrite(string channelName, TimeSpan duration) { }
        public void RecordRead(string channelName, TimeSpan duration) { }
        public void RecordBatchRead(string channelName, int batchSize) { }
        public void RecordDrop(string channelName) { }
        public void RecordBackpressure(string channelName) { }
        public void RecordWriteFailure(string channelName, string reason) { }
        public void RecordStatistics(ChannelStatistics stats) { }
    }
}