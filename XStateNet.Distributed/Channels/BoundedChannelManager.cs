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

        // Performance counters
        private long _totalItemsWritten;
        private long _totalItemsRead;
        private long _totalWritesFailed;
        private long _totalReadsCompleted;
        private long _totalDroppedItems;

        // Thread safety for DropNewest operations
        private readonly object _dropLock = new object();

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
                try
                {
                    if (_channel.Reader.CanCount)
                    {
                        return _channel.Reader.Count;
                    }
                }
                catch { }

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

            var channelOptions = new System.Threading.Channels.BoundedChannelOptions(options.Capacity)
            {
                FullMode = ConvertFullMode(options.FullMode),
                SingleReader = options.SingleReader,
                SingleWriter = options.SingleWriter,
                AllowSynchronousContinuations = options.AllowSynchronousContinuations
            };

            _channel = Channel.CreateBounded<T>(channelOptions);

            if (_options.EnableMonitoring)
            {
                _lastMonitoringTime = DateTime.UtcNow;
                _monitoringTimer = new Timer(
                    MonitoringCallback,
                    null,
                    _options.MonitoringInterval,
                    _options.MonitoringInterval);
            }

            _logger?.LogInformation("BoundedChannel '{ChannelName}' created with capacity {Capacity} and FullMode {FullMode}",
                _channelName, _options.Capacity, _options.FullMode);
        }

        private static BoundedChannelFullMode ConvertFullMode(ChannelFullMode mode)
        {
            return mode switch
            {
                ChannelFullMode.Wait => BoundedChannelFullMode.Wait,
                ChannelFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                // For DropNewest, use Wait mode and handle dropping manually
                ChannelFullMode.DropNewest => BoundedChannelFullMode.Wait,
                _ => BoundedChannelFullMode.Wait
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<bool> WriteAsync(T item, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Check if channel is at capacity before writing
                bool wasAtCapacity = false;
                if (_options.FullMode == ChannelFullMode.DropNewest && _channel.Reader.CanCount)
                {
                    wasAtCapacity = _channel.Reader.Count >= _options.Capacity;
                }

                bool writeSucceeded = _channel.Writer.TryWrite(item);

                if (writeSucceeded)
                {
                    // With DropWrite mode, write might succeed but drop an item
                    if (wasAtCapacity && _options.FullMode == ChannelFullMode.DropNewest)
                    {
                        // The write succeeded by dropping the newest item
                        Interlocked.Increment(ref _totalDroppedItems);
                        _metrics.RecordDrop(_channelName);
                    }

                    Interlocked.Increment(ref _totalItemsWritten);
                    _metrics.RecordWrite(_channelName, stopwatch.Elapsed);
                    return true;
                }

                // If TryWrite failed, the channel is full. Handle based on FullMode.
                switch (_options.FullMode)
                {
                    case ChannelFullMode.DropNewest:
                        // DropNewest means: drop the newest existing item in the channel to make room
                        // We need thread-safe handling here
                        lock (_dropLock)
                        {
                            // Double-check if channel is still full
                            if (_channel.Writer.TryWrite(item))
                            {
                                Interlocked.Increment(ref _totalItemsWritten);
                                _metrics.RecordWrite(_channelName, stopwatch.Elapsed);
                                return true;
                            }

                            // Channel is full, need to drop newest existing item
                            var items = new List<T>();
                            while (_channel.Reader.TryRead(out var existingItem))
                            {
                                items.Add(existingItem);
                            }

                            if (items.Count > 0)
                            {
                                // Drop the newest (last) item from the channel
                                items.RemoveAt(items.Count - 1);
                                Interlocked.Increment(ref _totalDroppedItems);
                                _metrics.RecordDrop(_channelName);

                                // Write back the remaining items
                                foreach (var oldItem in items)
                                {
                                    _channel.Writer.TryWrite(oldItem);
                                }
                            }

                            // Now write the new item
                            if (_channel.Writer.TryWrite(item))
                            {
                                Interlocked.Increment(ref _totalItemsWritten);
                                _metrics.RecordWrite(_channelName, stopwatch.Elapsed);
                                return true;
                            }

                            // Shouldn't happen, but handle gracefully
                            Interlocked.Increment(ref _totalDroppedItems);
                            _metrics.RecordDrop(_channelName);
                            return false;
                        }

                    case ChannelFullMode.DropOldest:
                        // Try to make space by dropping the oldest item.
                        if (_channel.Reader.TryRead(out _))
                        {
                            Interlocked.Increment(ref _totalDroppedItems);
                            // Now try to write the new item.
                            if (_channel.Writer.TryWrite(item))
                            {
                                Interlocked.Increment(ref _totalItemsWritten);
                                _metrics.RecordWrite(_channelName, stopwatch.Elapsed);
                                return true;
                            }
                        }
                        // If we couldn't make space, the write fails for this attempt.
                        return false;

                    case ChannelFullMode.Wait:
                        // Asynchronously wait for space to be available.
                        if (await _channel.Writer.WaitToWriteAsync(cancellationToken))
                        {
                            // After waiting, try writing again. This should succeed unless another writer got there first.
                            if (_channel.Writer.TryWrite(item))
                            {
                                Interlocked.Increment(ref _totalItemsWritten);
                                _metrics.RecordWrite(_channelName, stopwatch.Elapsed);
                                return true;
                            }
                        }
                        return false;

                    default: // Includes Reject, which is not a standard option but we'll treat as fail
                        return false;
                }
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
                if (_channel.Writer.TryWrite(item))
                {
                    Interlocked.Increment(ref _totalItemsWritten);
                    _metrics.RecordWrite(_channelName, TimeSpan.Zero);
                    return true;
                }

                // If TryWrite returns false, it means the channel is full.
                // In DropNewest mode, this is expected. In other modes, it indicates a problem.
                if (_options.FullMode == ChannelFullMode.DropNewest)
                {
                    Interlocked.Increment(ref _totalDroppedItems);
                    _metrics.RecordDrop(_channelName);
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
                // Asynchronously wait for an item to become available
                if (await _channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_channel.Reader.TryRead(out var item))
                    {
                        Interlocked.Increment(ref _totalItemsRead);
                        _metrics.RecordRead(_channelName, stopwatch.Elapsed);
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
                    }
                    else
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
                }

                if (batch.Count > 0)
                {
                    _metrics.RecordBatchRead(_channelName, batch.Count);
                }

                return batch;
            }
            catch (OperationCanceledException)
            {
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
        public bool SingleReader { get; set; } = false;
        public bool SingleWriter { get; set; } = false;
        public bool AllowSynchronousContinuations { get; set; } = false;
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
        Reject // Note: Reject is not a standard BoundedChannelFullMode, custom handling would be needed if this is desired.
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