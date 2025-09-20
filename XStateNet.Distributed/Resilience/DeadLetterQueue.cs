using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.Resilience
{
    public class DeadLetterStatistics
    {
        public long TotalMessages { get; set; }
        public long PendingRetry { get; set; }
        public long PermanentlyFailed { get; set; }
        public long SuccessfullyRedriven { get; set; }
        public Dictionary<string, long> MessagesBySource { get; set; } = new();
    }

    /// <summary>
    /// High-performance Dead Letter Queue for handling failed messages
    /// </summary>
    public sealed class DeadLetterQueue : IDeadLetterQueue
    {
        private readonly ILogger<DeadLetterQueue>? _logger;
        private readonly DeadLetterQueueOptions _options;
        private readonly IDeadLetterStorage _storage;
        private readonly IDeadLetterMetrics _metrics;

        // In-memory buffers for performance
        private readonly Channel<DeadLetterEntry> _incomingChannel;
        private readonly ConcurrentDictionary<string, DeadLetterEntry> _activeEntries;
        private readonly ConcurrentDictionary<string, RetryState> _retryStates;

        // Background processing
        private readonly Task _processingTask;
        private readonly Task _retryTask;
        private readonly CancellationTokenSource _cts;
        private readonly Timer _cleanupTimer;

        // Statistics
        private long _totalEnqueued;
        private long _totalProcessed;
        private long _totalRetried;
        private long _totalExpired;
        private long _totalRedriven;

        public long QueueDepth => _activeEntries.Count;
        public long TotalEnqueued => _totalEnqueued;
        public long TotalProcessed => _totalProcessed;

        public DeadLetterQueue(
            DeadLetterQueueOptions options,
            IDeadLetterStorage? storage = null,
            IDeadLetterMetrics? metrics = null,
            ILogger<DeadLetterQueue>? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _storage = storage ?? new InMemoryDeadLetterStorage();
            _metrics = metrics ?? new NullDeadLetterMetrics();
            _logger = logger;

            _activeEntries = new ConcurrentDictionary<string, DeadLetterEntry>();
            _retryStates = new ConcurrentDictionary<string, RetryState>();
            _cts = new CancellationTokenSource();

            // Create unbounded channel for high throughput
            _incomingChannel = Channel.CreateUnbounded<DeadLetterEntry>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            // Start background tasks
            _processingTask = Task.Run(() => ProcessIncomingAsync(_cts.Token));
            _retryTask = Task.Run(() => ProcessRetriesAsync(_cts.Token));

            // Cleanup timer for expired messages
            _cleanupTimer = new Timer(
                CleanupExpiredMessages,
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);

            _logger?.LogInformation("DeadLetterQueue initialized with max size: {MaxSize}", _options.MaxQueueSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<string> EnqueueAsync<T>(
            T message,
            string source,
            string reason,
            Exception? exception = null,
            Dictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            // Check queue capacity
            if (_activeEntries.Count >= _options.MaxQueueSize)
            {
                if (_options.OverflowStrategy == OverflowStrategy.Reject)
                {
                    _metrics.RecordRejection(source, "QueueFull");
                    throw new DeadLetterQueueFullException($"Queue is at maximum capacity: {_options.MaxQueueSize}");
                }
                else if (_options.OverflowStrategy == OverflowStrategy.DropOldest)
                {
                    await DropOldestMessageAsync();
                }
            }

            var entry = new DeadLetterEntry
            {
                Id = Guid.NewGuid().ToString(),
                MessageType = typeof(T).FullName!,
                MessageData = SerializeMessage(message),
                Source = source,
                Reason = reason,
                Exception = exception?.ToString(),
                Metadata = metadata ?? new Dictionary<string, string>(),
                EnqueuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(_options.MessageTTL),
                RetryCount = 0,
                MaxRetries = _options.MaxRetries
            };

            // Add trace context if available
            if (Activity.Current != null)
            {
                entry.Metadata["TraceId"] = Activity.Current.TraceId.ToString();
                entry.Metadata["SpanId"] = Activity.Current.SpanId.ToString();
            }

            // Write to channel for async processing
            if (!_incomingChannel.Writer.TryWrite(entry))
            {
                await _incomingChannel.Writer.WriteAsync(entry, cancellationToken);
            }

            Interlocked.Increment(ref _totalEnqueued);
            _metrics.RecordEnqueue(source, reason);
            _logger?.LogWarning("Message enqueued to DLQ. Source: {Source}, Reason: {Reason}, Id: {Id}",
                source, reason, entry.Id);

            return entry.Id;
        }

        public async Task<T?> DequeueAsync<T>(string messageId, CancellationToken cancellationToken = default)
        {
            if (_activeEntries.TryRemove(messageId, out var entry))
            {
                try
                {
                    var message = DeserializeMessage<T>(entry.MessageData);

                    Interlocked.Increment(ref _totalProcessed);
                    _metrics.RecordDequeue(entry.Source);

                    // Remove from persistent storage
                    await _storage.DeleteAsync(messageId, cancellationToken);

                    return message;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to dequeue message {MessageId}", messageId);

                    // Re-add to queue on failure
                    _activeEntries.TryAdd(messageId, entry);
                    throw;
                }
            }

            return default;
        }

        public async Task<bool> RetryAsync(
            string messageId,
            Func<object, Task<bool>> processor,
            CancellationToken cancellationToken = default)
        {
            if (!_activeEntries.TryGetValue(messageId, out var entry))
            {
                return false;
            }

            var retryState = _retryStates.GetOrAdd(messageId, _ => new RetryState());

            // Check if retry is allowed
            if (entry.RetryCount >= entry.MaxRetries)
            {
                _metrics.RecordRetryExhaustion(entry.Source);
                return false;
            }

            // Apply backoff if needed
            var backoffDelay = CalculateBackoffDelay(entry.RetryCount);
            if (backoffDelay > TimeSpan.Zero)
            {
                await Task.Delay(backoffDelay, cancellationToken);
            }

            try
            {
                // Deserialize and process message
                var messageType = Type.GetType(entry.MessageType);
                if (messageType == null)
                {
                    _logger?.LogError("Cannot find type {TypeName} for message {MessageId}",
                        entry.MessageType, messageId);
                    return false;
                }

                var message = DeserializeMessage(entry.MessageData, messageType);
                var success = await processor(message);

                if (success)
                {
                    // Remove from DLQ on successful retry
                    _activeEntries.TryRemove(messageId, out _);
                    _retryStates.TryRemove(messageId, out _);

                    await _storage.DeleteAsync(messageId, cancellationToken);

                    Interlocked.Increment(ref _totalRetried);
                    _metrics.RecordRetrySuccess(entry.Source, entry.RetryCount + 1);

                    _logger?.LogInformation("Message {MessageId} successfully retried after {RetryCount} attempts",
                        messageId, entry.RetryCount + 1);

                    return true;
                }
                else
                {
                    entry.RetryCount++;
                    entry.LastRetryAt = DateTime.UtcNow;

                    _metrics.RecordRetryFailure(entry.Source, entry.RetryCount);

                    // Update in storage
                    await _storage.UpdateAsync(entry, cancellationToken);

                    return false;
                }
            }
            catch (Exception ex)
            {
                entry.RetryCount++;
                entry.LastRetryAt = DateTime.UtcNow;
                entry.LastError = ex.Message;

                _metrics.RecordRetryError(entry.Source, ex.GetType().Name);
                _logger?.LogError(ex, "Retry failed for message {MessageId}", messageId);

                // Update in storage
                await _storage.UpdateAsync(entry, cancellationToken);

                return false;
            }
        }

        public async Task<int> RedriveAsync(
            Func<DeadLetterEntry, Task<bool>> processor,
            RedriveOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new RedriveOptions();
            var processedCount = 0;
            var entries = GetEligibleEntriesForRedrive(options);

            var semaphore = new SemaphoreSlim(options.MaxConcurrency);
            var tasks = new List<Task<bool>>();

            foreach (var entry in entries.Take(options.MaxMessages))
            {
                await semaphore.WaitAsync(cancellationToken);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var success = await processor(entry);

                        if (success)
                        {
                            _activeEntries.TryRemove(entry.Id, out _);
                            await _storage.DeleteAsync(entry.Id, cancellationToken);
                            Interlocked.Increment(ref _totalRedriven);
                            return true;
                        }

                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Redrive failed for message {MessageId}", entry.Id);
                        return false;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            var results = await Task.WhenAll(tasks);
            processedCount = results.Count(r => r);

            _metrics.RecordRedrive(processedCount, entries.Count());
            _logger?.LogInformation("Redrived {Processed}/{Total} messages from DLQ",
                processedCount, entries.Count());

            return processedCount;
        }

        public async Task<IEnumerable<DeadLetterEntry>> GetMessagesAsync(
            int maxCount = 100,
            string? source = null,
            DateTime? since = null,
            CancellationToken cancellationToken = default)
        {
            var query = _activeEntries.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(source))
            {
                query = query.Where(e => e.Source == source);
            }

            if (since.HasValue)
            {
                query = query.Where(e => e.EnqueuedAt >= since.Value);
            }

            return query
                .OrderBy(e => e.EnqueuedAt)
                .Take(maxCount)
                .ToList();
        }

        public async Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default)
        {
            if (_activeEntries.TryRemove(messageId, out var entry))
            {
                await _storage.DeleteAsync(messageId, cancellationToken);
                _metrics.RecordDeletion(entry.Source);
                return true;
            }

            return false;
        }

        public async Task<int> PurgeAsync(
            Func<DeadLetterEntry, bool>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            var entries = predicate != null
                ? _activeEntries.Values.Where(predicate)
                : _activeEntries.Values;

            var purgedCount = 0;
            var tasks = new List<Task>();

            foreach (var entry in entries.ToList())
            {
                if (_activeEntries.TryRemove(entry.Id, out _))
                {
                    tasks.Add(_storage.DeleteAsync(entry.Id, cancellationToken));
                    purgedCount++;
                }
            }

            await Task.WhenAll(tasks);

            _metrics.RecordPurge(purgedCount);
            _logger?.LogInformation("Purged {Count} messages from DLQ", purgedCount);

            return purgedCount;
        }

        public DeadLetterQueueStatistics GetStatistics()
        {
            var now = DateTime.UtcNow;
            var entries = _activeEntries.Values.ToList();

            return new DeadLetterQueueStatistics
            {
                QueueDepth = entries.Count,
                TotalEnqueued = _totalEnqueued,
                TotalProcessed = _totalProcessed,
                TotalRetried = _totalRetried,
                TotalExpired = _totalExpired,
                TotalRedriven = _totalRedriven,
                OldestMessageAge = entries.Any()
                    ? now - entries.Min(e => e.EnqueuedAt)
                    : TimeSpan.Zero,
                MessagesBySource = entries
                    .GroupBy(e => e.Source)
                    .ToDictionary(g => g.Key, g => g.Count()),
                MessagesByReason = entries
                    .GroupBy(e => e.Reason)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RetryDistribution = entries
                    .GroupBy(e => e.RetryCount)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        private async Task ProcessIncomingAsync(CancellationToken cancellationToken)
        {
            await foreach (var entry in _incomingChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Add to in-memory store
                    _activeEntries.TryAdd(entry.Id, entry);

                    // Persist to storage
                    await _storage.SaveAsync(entry, cancellationToken);

                    // Check for auto-retry
                    if (_options.EnableAutoRetry && entry.RetryCount < entry.MaxRetries)
                    {
                        ScheduleRetry(entry);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to process incoming DLQ message {MessageId}", entry.Id);
                }
            }
        }

        private async Task ProcessRetriesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var entriesToRetry = _activeEntries.Values
                        .Where(e => e.NextRetryAt.HasValue && e.NextRetryAt.Value <= now)
                        .Take(10) // Process in batches
                        .ToList();

                    foreach (var entry in entriesToRetry)
                    {
                        if (_options.AutoRetryProcessor != null)
                        {
                            _ = Task.Run(async () =>
                            {
                                await RetryAsync(entry.Id, _options.AutoRetryProcessor, cancellationToken);
                            }, cancellationToken);
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger?.LogError(ex, "Error in retry processing loop");
                }
            }
        }

        private void ScheduleRetry(DeadLetterEntry entry)
        {
            var delay = CalculateBackoffDelay(entry.RetryCount);
            entry.NextRetryAt = DateTime.UtcNow.Add(delay);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimeSpan CalculateBackoffDelay(int retryCount)
        {
            if (retryCount <= 0) return TimeSpan.Zero;

            var baseDelay = _options.RetryBackoffBase.TotalMilliseconds;
            var maxDelay = _options.MaxRetryDelay.TotalMilliseconds;

            var delayMs = Math.Min(baseDelay * Math.Pow(2, retryCount - 1), maxDelay);

            // Add jitter
            var jitter = new Random().NextDouble() * 0.3; // 30% jitter
            delayMs = delayMs * (1 + jitter);

            return TimeSpan.FromMilliseconds(delayMs);
        }

        private async Task DropOldestMessageAsync()
        {
            var oldest = _activeEntries.Values
                .OrderBy(e => e.EnqueuedAt)
                .FirstOrDefault();

            if (oldest != null)
            {
                _activeEntries.TryRemove(oldest.Id, out _);
                await _storage.DeleteAsync(oldest.Id);
                _metrics.RecordEviction(oldest.Source);
            }
        }

        private void CleanupExpiredMessages(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredEntries = _activeEntries.Values
                    .Where(e => e.ExpiresAt <= now)
                    .ToList();

                foreach (var entry in expiredEntries)
                {
                    if (_activeEntries.TryRemove(entry.Id, out _))
                    {
                        _ = _storage.DeleteAsync(entry.Id);
                        Interlocked.Increment(ref _totalExpired);
                        _metrics.RecordExpiration(entry.Source);
                    }
                }

                if (expiredEntries.Any())
                {
                    _logger?.LogInformation("Cleaned up {Count} expired messages from DLQ", expiredEntries.Count);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during DLQ cleanup");
            }
        }

        private IEnumerable<DeadLetterEntry> GetEligibleEntriesForRedrive(RedriveOptions options)
        {
            var query = _activeEntries.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(options.SourceFilter))
            {
                query = query.Where(e => e.Source == options.SourceFilter);
            }

            if (options.MaxAge.HasValue)
            {
                var cutoff = DateTime.UtcNow.Subtract(options.MaxAge.Value);
                query = query.Where(e => e.EnqueuedAt >= cutoff);
            }

            if (options.MaxRetries.HasValue)
            {
                query = query.Where(e => e.RetryCount <= options.MaxRetries.Value);
            }

            return query.OrderBy(e => e.EnqueuedAt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] SerializeMessage<T>(T message)
        {
            var options = MessagePackSerializerOptions.Standard
                .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
            return MessagePackSerializer.Serialize(message, options);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T DeserializeMessage<T>(byte[] data)
        {
            var options = MessagePackSerializerOptions.Standard
                .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
            return MessagePackSerializer.Deserialize<T>(data, options);
        }

        private object DeserializeMessage(byte[] data, Type messageType)
        {
            var options = MessagePackSerializerOptions.Standard
                .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
            return MessagePackSerializer.Deserialize(messageType, data, options);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cleanupTimer?.Dispose();
            _incomingChannel.Writer.TryComplete();

            try
            {
                Task.WaitAll(new[] { _processingTask, _retryTask }, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException) { }

            _cts.Dispose();
        }
    }

    // Interfaces and supporting types
    public interface IDeadLetterQueue : IDisposable
    {
        long QueueDepth { get; }
        long TotalEnqueued { get; }
        long TotalProcessed { get; }

        Task<string> EnqueueAsync<T>(T message, string source, string reason,
            Exception? exception = null, Dictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default);

        Task<T?> DequeueAsync<T>(string messageId, CancellationToken cancellationToken = default);

        Task<bool> RetryAsync(string messageId, Func<object, Task<bool>> processor,
            CancellationToken cancellationToken = default);

        Task<int> RedriveAsync(Func<DeadLetterEntry, Task<bool>> processor,
            RedriveOptions? options = null, CancellationToken cancellationToken = default);

        Task<IEnumerable<DeadLetterEntry>> GetMessagesAsync(int maxCount = 100,
            string? source = null, DateTime? since = null,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default);

        Task<int> PurgeAsync(Func<DeadLetterEntry, bool>? predicate = null,
            CancellationToken cancellationToken = default);

        DeadLetterQueueStatistics GetStatistics();
    }

    public class DeadLetterEntry
    {
        public string Id { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public byte[] MessageData { get; set; } = Array.Empty<byte>();
        public string Source { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public string? LastError { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTime EnqueuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? LastRetryAt { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
    }

    public class DeadLetterQueueOptions
    {
        public int MaxQueueSize { get; set; } = 10000;
        public TimeSpan MessageTTL { get; set; } = TimeSpan.FromDays(7);
        public int MaxRetries { get; set; } = 3;
        public TimeSpan RetryBackoffBase { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
        public OverflowStrategy OverflowStrategy { get; set; } = OverflowStrategy.DropOldest;
        public bool EnableAutoRetry { get; set; } = false;
        public Func<object, Task<bool>>? AutoRetryProcessor { get; set; }
    }

    public enum OverflowStrategy
    {
        Reject,
        DropOldest,
        DropNewest
    }

    public class RedriveOptions
    {
        public int MaxMessages { get; set; } = 100;
        public int MaxConcurrency { get; set; } = 10;
        public string? SourceFilter { get; set; }
        public TimeSpan? MaxAge { get; set; }
        public int? MaxRetries { get; set; }
    }

    public class DeadLetterQueueStatistics
    {
        public int QueueDepth { get; set; }
        public long TotalEnqueued { get; set; }
        public long TotalProcessed { get; set; }
        public long TotalRetried { get; set; }
        public long TotalExpired { get; set; }
        public long TotalRedriven { get; set; }
        public TimeSpan OldestMessageAge { get; set; }
        public Dictionary<string, int> MessagesBySource { get; set; } = new();
        public Dictionary<string, int> MessagesByReason { get; set; } = new();
        public Dictionary<int, int> RetryDistribution { get; set; } = new();
    }

    public class DeadLetterQueueFullException : Exception
    {
        public DeadLetterQueueFullException(string message) : base(message) { }
    }

    // Storage interfaces
    public interface IDeadLetterStorage
    {
        Task SaveAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);
        Task UpdateAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);
        Task<DeadLetterEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default);
        Task DeleteAsync(string messageId, CancellationToken cancellationToken = default);
        Task<IEnumerable<DeadLetterEntry>> ListAsync(int maxCount = 100, CancellationToken cancellationToken = default);
    }

    // In-memory storage implementation
    public class InMemoryDeadLetterStorage : IDeadLetterStorage
    {
        private readonly ConcurrentDictionary<string, DeadLetterEntry> _entries = new();

        public Task SaveAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.TryAdd(entry.Id, entry);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.AddOrUpdate(entry.Id, entry, (key, old) => entry);
            return Task.CompletedTask;
        }

        public Task<DeadLetterEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default)
        {
            _entries.TryGetValue(messageId, out var entry);
            return Task.FromResult(entry);
        }

        public Task DeleteAsync(string messageId, CancellationToken cancellationToken = default)
        {
            _entries.TryRemove(messageId, out _);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<DeadLetterEntry>> ListAsync(int maxCount = 100, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_entries.Values.Take(maxCount).AsEnumerable());
        }
    }

    // Metrics interface
    public interface IDeadLetterMetrics
    {
        void RecordEnqueue(string source, string reason);
        void RecordDequeue(string source);
        void RecordRetrySuccess(string source, int attemptCount);
        void RecordRetryFailure(string source, int attemptCount);
        void RecordRetryError(string source, string errorType);
        void RecordRetryExhaustion(string source);
        void RecordRedrive(int successCount, int totalCount);
        void RecordDeletion(string source);
        void RecordPurge(int count);
        void RecordExpiration(string source);
        void RecordEviction(string source);
        void RecordRejection(string source, string reason);
    }

    internal class NullDeadLetterMetrics : IDeadLetterMetrics
    {
        public void RecordEnqueue(string source, string reason) { }
        public void RecordDequeue(string source) { }
        public void RecordRetrySuccess(string source, int attemptCount) { }
        public void RecordRetryFailure(string source, int attemptCount) { }
        public void RecordRetryError(string source, string errorType) { }
        public void RecordRetryExhaustion(string source) { }
        public void RecordRedrive(int successCount, int totalCount) { }
        public void RecordDeletion(string source) { }
        public void RecordPurge(int count) { }
        public void RecordExpiration(string source) { }
        public void RecordEviction(string source) { }
        public void RecordRejection(string source, string reason) { }
    }

    // Retry state tracking
    internal class RetryState
    {
        public DateTime LastAttempt { get; set; }
        public int ConsecutiveFailures { get; set; }
        public bool IsRetrying { get; set; }
    }
}