using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.Channels
{
    /// <summary>
    /// Priority-based channel manager with multiple priority levels
    /// </summary>
    public sealed class PriorityChannelManager<T> : IPriorityChannelManager<T>
    {
        private readonly string _channelName;
        private readonly PriorityChannelOptions _options;
        private readonly ILogger<PriorityChannelManager<T>>? _logger;

        // Priority channels (index 0 = highest priority)
        private readonly BoundedChannelManager<PriorityItem<T>>[] _priorityChannels;
        private readonly SemaphoreSlim _readSemaphore;

        // Round-robin state for fair processing
        private int _currentPriorityIndex;
        private readonly int[] _priorityWeights;
        private readonly int[] _priorityCounters;

        // Statistics
        private readonly long[] _itemsPerPriority;
        private long _totalItemsProcessed;

        public PriorityChannelManager(
            string channelName,
            PriorityChannelOptions options,
            ILogger<PriorityChannelManager<T>>? logger = null)
        {
            _channelName = channelName ?? throw new ArgumentNullException(nameof(channelName));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;

            var priorityCount = options.PriorityLevels;
            _priorityChannels = new BoundedChannelManager<PriorityItem<T>>[priorityCount];
            _itemsPerPriority = new long[priorityCount];
            _priorityWeights = new int[priorityCount];
            _priorityCounters = new int[priorityCount];

            // Initialize priority channels
            for (int i = 0; i < priorityCount; i++)
            {
                var channelOptions = new CustomBoundedChannelOptions
                {
                    Capacity = options.CapacityPerPriority,
                    FullMode = options.FullMode,
                    SingleReader = true,
                    SingleWriter = false,
                    EnableMonitoring = options.EnableMonitoring
                };

                var typedLogger = logger as ILogger<BoundedChannelManager<PriorityItem<T>>>;
                _priorityChannels[i] = new BoundedChannelManager<PriorityItem<T>>(
                    $"{_channelName}_P{i}",
                    channelOptions,
                    null,
                    typedLogger);

                // Set weights (higher priority = higher weight)
                _priorityWeights[i] = priorityCount - i;
            }

            _readSemaphore = new SemaphoreSlim(1, 1);

            _logger?.LogInformation("PriorityChannel '{ChannelName}' created with {Levels} priority levels",
                _channelName, priorityCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<bool> WriteAsync(T item, Priority priority, CancellationToken cancellationToken = default)
        {
            var priorityIndex = GetPriorityIndex(priority);
            if (priorityIndex < 0 || priorityIndex >= _priorityChannels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(priority), $"Invalid priority: {priority}");
            }

            var priorityItem = new PriorityItem<T>
            {
                Item = item,
                Priority = priority,
                EnqueuedAt = DateTime.UtcNow
            };

            var result = await _priorityChannels[priorityIndex].WriteAsync(priorityItem, cancellationToken);
            if (result)
            {
                Interlocked.Increment(ref _itemsPerPriority[priorityIndex]);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWrite(T item, Priority priority)
        {
            var priorityIndex = GetPriorityIndex(priority);
            if (priorityIndex < 0 || priorityIndex >= _priorityChannels.Length)
            {
                return false;
            }

            var priorityItem = new PriorityItem<T>
            {
                Item = item,
                Priority = priority,
                EnqueuedAt = DateTime.UtcNow
            };

            var result = _priorityChannels[priorityIndex].TryWrite(priorityItem);
            if (result)
            {
                Interlocked.Increment(ref _itemsPerPriority[priorityIndex]);
            }

            return result;
        }

        public async ValueTask<(bool Success, T? Item, Priority Priority)> ReadAsync(CancellationToken cancellationToken = default)
        {
            await _readSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Try priority-based reading
                if (_options.ProcessingMode == PriorityProcessingMode.StrictPriority)
                {
                    return await ReadStrictPriorityAsync(cancellationToken);
                }
                else if (_options.ProcessingMode == PriorityProcessingMode.WeightedRoundRobin)
                {
                    return await ReadWeightedRoundRobinAsync(cancellationToken);
                }
                else
                {
                    return await ReadFairShareAsync(cancellationToken);
                }
            }
            finally
            {
                _readSemaphore.Release();
            }
        }

        public async IAsyncEnumerable<(T Item, Priority Priority)> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await ReadAsync(cancellationToken);
                if (result.Success)
                {
                    yield return (result.Item!, result.Priority);
                }
                else
                {
                    // Check if all channels are completed
                    if (AllChannelsCompleted())
                    {
                        yield break;
                    }

                    // Small delay to prevent tight loop
                    await Task.Delay(10, cancellationToken);
                }
            }
        }

        public async Task<List<(T Item, Priority Priority)>> ReadBatchAsync(
            int maxItems,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var batch = new List<(T Item, Priority Priority)>(maxItems);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            while (batch.Count < maxItems && !cts.Token.IsCancellationRequested)
            {
                var result = await ReadAsync(cts.Token);
                if (result.Success)
                {
                    batch.Add((result.Item!, result.Priority));
                }
                else if (AllChannelsCompleted())
                {
                    break;
                }
            }

            return batch;
        }

        public void Complete(Priority? priority = null, Exception? exception = null)
        {
            if (priority.HasValue)
            {
                var index = GetPriorityIndex(priority.Value);
                _priorityChannels[index].Complete(exception);
            }
            else
            {
                foreach (var channel in _priorityChannels)
                {
                    channel.Complete(exception);
                }
            }

            _logger?.LogInformation("PriorityChannel '{ChannelName}' completed", _channelName);
        }

        public PriorityChannelStatistics GetStatistics()
        {
            var stats = new PriorityChannelStatistics
            {
                ChannelName = _channelName,
                PriorityLevels = _options.PriorityLevels,
                TotalItemsProcessed = _totalItemsProcessed,
                ProcessingMode = _options.ProcessingMode
            };

            for (int i = 0; i < _priorityChannels.Length; i++)
            {
                var channelStats = _priorityChannels[i].GetStatistics();
                var priorityStats = new PriorityLevelStatistics
                {
                    Priority = GetPriority(i),
                    ItemsQueued = channelStats.CurrentDepth,
                    ItemsProcessed = _itemsPerPriority[i],
                    Utilization = channelStats.UtilizationPercent,
                    IsFull = channelStats.IsFull
                };
                stats.PriorityStatistics.Add(priorityStats);
            }

            return stats;
        }

        private async ValueTask<(bool Success, T? Item, Priority Priority)> ReadStrictPriorityAsync(
            CancellationToken cancellationToken)
        {
            // Read from highest priority channel that has items
            for (int i = 0; i < _priorityChannels.Length; i++)
            {
                var result = await _priorityChannels[i].ReadAsync(cancellationToken);
                if (result.Success)
                {
                    Interlocked.Increment(ref _totalItemsProcessed);
                    return (true, result.Item!.Item, result.Item.Priority);
                }
            }

            return (false, default, Priority.Normal);
        }

        private async ValueTask<(bool Success, T? Item, Priority Priority)> ReadWeightedRoundRobinAsync(
            CancellationToken cancellationToken)
        {
            // Process based on weights
            for (int attempts = 0; attempts < _priorityChannels.Length * 2; attempts++)
            {
                var index = _currentPriorityIndex;

                if (_priorityCounters[index] < _priorityWeights[index])
                {
                    var result = await _priorityChannels[index].ReadAsync(cancellationToken);
                    if (result.Success)
                    {
                        _priorityCounters[index]++;
                        Interlocked.Increment(ref _totalItemsProcessed);
                        return (true, result.Item!.Item, result.Item.Priority);
                    }
                }

                // Move to next priority
                _currentPriorityIndex = (_currentPriorityIndex + 1) % _priorityChannels.Length;

                // Reset counter if all weights consumed
                if (_priorityCounters[index] >= _priorityWeights[index])
                {
                    _priorityCounters[index] = 0;
                }
            }

            return (false, default, Priority.Normal);
        }

        private async ValueTask<(bool Success, T? Item, Priority Priority)> ReadFairShareAsync(
            CancellationToken cancellationToken)
        {
            // Round-robin across all priorities
            var startIndex = _currentPriorityIndex;

            do
            {
                var result = await _priorityChannels[_currentPriorityIndex].ReadAsync(cancellationToken);
                if (result.Success)
                {
                    _currentPriorityIndex = (_currentPriorityIndex + 1) % _priorityChannels.Length;
                    Interlocked.Increment(ref _totalItemsProcessed);
                    return (true, result.Item!.Item, result.Item.Priority);
                }

                _currentPriorityIndex = (_currentPriorityIndex + 1) % _priorityChannels.Length;
            }
            while (_currentPriorityIndex != startIndex);

            return (false, default, Priority.Normal);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetPriorityIndex(Priority priority)
        {
            return priority switch
            {
                Priority.Critical => 0,
                Priority.High => 1,
                Priority.Normal => 2,
                Priority.Low => 3,
                Priority.VeryLow => 4,
                _ => 2 // Default to Normal
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Priority GetPriority(int index)
        {
            return index switch
            {
                0 => Priority.Critical,
                1 => Priority.High,
                2 => Priority.Normal,
                3 => Priority.Low,
                4 => Priority.VeryLow,
                _ => Priority.Normal
            };
        }

        private bool AllChannelsCompleted()
        {
            return _priorityChannels.All(c => c.Reader.Completion.IsCompleted);
        }

        public void Dispose()
        {
            foreach (var channel in _priorityChannels)
            {
                channel?.Dispose();
            }
            _readSemaphore?.Dispose();
        }
    }

    /// <summary>
    /// Multiplexing channel manager that combines multiple channels
    /// </summary>
    public sealed class MultiplexingChannelManager<T> : IMultiplexingChannelManager<T>
    {
        private readonly ConcurrentDictionary<string, IChannelManager<T>> _channels;
        private readonly MultiplexingOptions _options;
        private readonly ILogger<MultiplexingChannelManager<T>>? _logger;
        private readonly SemaphoreSlim _semaphore;

        // Load balancing state
        private int _currentChannelIndex;
        private readonly List<string> _channelKeys;

        public MultiplexingChannelManager(
            MultiplexingOptions options,
            ILogger<MultiplexingChannelManager<T>>? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
            _channels = new ConcurrentDictionary<string, IChannelManager<T>>();
            _channelKeys = new List<string>();
            _semaphore = new SemaphoreSlim(1, 1);

            _logger?.LogInformation("MultiplexingChannelManager created with strategy: {Strategy}",
                options.Strategy);
        }

        public void AddChannel(string name, IChannelManager<T> channel)
        {
            if (_channels.ContainsKey(name))
            {
                throw new InvalidOperationException($"Channel '{name}' already exists");
            }

            _channels[name] = channel;
            _channelKeys.Add(name);

            _logger?.LogDebug("Added channel '{ChannelName}' to multiplexer", name);
        }

        public bool RemoveChannel(string name)
        {
            if (_channels.TryRemove(name, out _))
            {
                _channelKeys.Remove(name);
                _logger?.LogDebug("Removed channel '{ChannelName}' from multiplexer", name);
                return true;
            }
            return false;
        }

        public async ValueTask<bool> WriteAsync(string channelName, T item, CancellationToken cancellationToken = default)
        {
            if (_channels.TryGetValue(channelName, out var channel))
            {
                return await channel.WriteAsync(item, cancellationToken);
            }

            _logger?.LogWarning("Channel '{ChannelName}' not found", channelName);
            return false;
        }

        public async ValueTask<bool> WriteToAnyAsync(T item, CancellationToken cancellationToken = default)
        {
            if (_channels.Count == 0)
                return false;

            switch (_options.Strategy)
            {
                case MultiplexingStrategy.RoundRobin:
                    return await WriteRoundRobinAsync(item, cancellationToken);

                case MultiplexingStrategy.LeastLoaded:
                    return await WriteLeastLoadedAsync(item, cancellationToken);

                case MultiplexingStrategy.Broadcast:
                    return await WriteBroadcastAsync(item, cancellationToken);

                case MultiplexingStrategy.FirstAvailable:
                    return await WriteFirstAvailableAsync(item, cancellationToken);

                default:
                    return await WriteRoundRobinAsync(item, cancellationToken);
            }
        }

        public async ValueTask<(bool Success, T? Item, string ChannelName)> ReadFromAnyAsync(
            CancellationToken cancellationToken = default)
        {
            if (_channels.Count == 0)
                return (false, default, string.Empty);

            // Create tasks for all channels
            var readTasks = _channels.Select(kvp => ReadWithNameAsync(kvp.Key, kvp.Value, cancellationToken)).ToList();

            // Wait for any to complete
            var completedTask = await Task.WhenAny(readTasks);
            return await completedTask;
        }

        public async IAsyncEnumerable<(T Item, string ChannelName)> ReadAllFromAnyAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await ReadFromAnyAsync(cancellationToken);
                if (result.Success)
                {
                    yield return (result.Item!, result.ChannelName);
                }
                else
                {
                    // Check if all channels are completed
                    if (_channels.Values.All(c => c.Reader.Completion.IsCompleted))
                    {
                        yield break;
                    }

                    await Task.Delay(10, cancellationToken);
                }
            }
        }

        private async Task<(bool Success, T? Item, string ChannelName)> ReadWithNameAsync(
            string name,
            IChannelManager<T> channel,
            CancellationToken cancellationToken)
        {
            var result = await channel.ReadAsync(cancellationToken);
            return (result.Success, result.Item, name);
        }

        private async ValueTask<bool> WriteRoundRobinAsync(T item, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (_channelKeys.Count == 0)
                    return false;

                var startIndex = _currentChannelIndex;
                do
                {
                    var channelName = _channelKeys[_currentChannelIndex];
                    _currentChannelIndex = (_currentChannelIndex + 1) % _channelKeys.Count;

                    if (_channels[channelName].TryWrite(item))
                    {
                        return true;
                    }
                }
                while (_currentChannelIndex != startIndex);

                // All channels full, use configured overflow strategy
                return await HandleOverflowAsync(item, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async ValueTask<bool> WriteLeastLoadedAsync(T item, CancellationToken cancellationToken)
        {
            // Find channel with lowest utilization
            var leastLoaded = _channels
                .OrderBy(kvp => kvp.Value.CurrentQueueDepth)
                .FirstOrDefault();

            if (leastLoaded.Value != null)
            {
                return await leastLoaded.Value.WriteAsync(item, cancellationToken);
            }

            return false;
        }

        private async ValueTask<bool> WriteBroadcastAsync(T item, CancellationToken cancellationToken)
        {
            var tasks = _channels.Values.Select(c => c.WriteAsync(item, cancellationToken).AsTask()).ToList();
            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }

        private async ValueTask<bool> WriteFirstAvailableAsync(T item, CancellationToken cancellationToken)
        {
            foreach (var channel in _channels.Values)
            {
                if (!channel.IsFull && await channel.WriteAsync(item, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private async ValueTask<bool> HandleOverflowAsync(T item, CancellationToken cancellationToken)
        {
            if (_options.OverflowStrategy == OverflowStrategy.Wait)
            {
                // Wait for any channel to have space
                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var channel in _channels.Values)
                    {
                        if (await channel.WriteAsync(item, cancellationToken))
                        {
                            return true;
                        }
                    }
                    await Task.Delay(10, cancellationToken);
                }
            }

            return false;
        }

        public void Dispose()
        {
            foreach (var channel in _channels.Values)
            {
                channel?.Dispose();
            }
            _semaphore?.Dispose();
        }
    }

    // Interfaces and supporting types
    public interface IPriorityChannelManager<T> : IDisposable
    {
        ValueTask<bool> WriteAsync(T item, Priority priority, CancellationToken cancellationToken = default);
        bool TryWrite(T item, Priority priority);
        ValueTask<(bool Success, T? Item, Priority Priority)> ReadAsync(CancellationToken cancellationToken = default);
        IAsyncEnumerable<(T Item, Priority Priority)> ReadAllAsync(CancellationToken cancellationToken = default);
        Task<List<(T Item, Priority Priority)>> ReadBatchAsync(int maxItems, TimeSpan timeout, CancellationToken cancellationToken = default);
        void Complete(Priority? priority = null, Exception? exception = null);
        PriorityChannelStatistics GetStatistics();
    }

    public interface IMultiplexingChannelManager<T> : IDisposable
    {
        void AddChannel(string name, IChannelManager<T> channel);
        bool RemoveChannel(string name);
        ValueTask<bool> WriteAsync(string channelName, T item, CancellationToken cancellationToken = default);
        ValueTask<bool> WriteToAnyAsync(T item, CancellationToken cancellationToken = default);
        ValueTask<(bool Success, T? Item, string ChannelName)> ReadFromAnyAsync(CancellationToken cancellationToken = default);
        IAsyncEnumerable<(T Item, string ChannelName)> ReadAllFromAnyAsync(CancellationToken cancellationToken = default);
    }

    public class PriorityChannelOptions
    {
        public int PriorityLevels { get; set; } = 5;
        public int CapacityPerPriority { get; set; } = 100;
        public ChannelFullMode FullMode { get; set; } = ChannelFullMode.Wait;
        public PriorityProcessingMode ProcessingMode { get; set; } = PriorityProcessingMode.StrictPriority;
        public bool EnableMonitoring { get; set; } = true;
    }

    public enum Priority
    {
        Critical = 0,
        High = 1,
        Normal = 2,
        Low = 3,
        VeryLow = 4
    }

    public enum PriorityProcessingMode
    {
        StrictPriority,      // Always process higher priority first
        WeightedRoundRobin,  // Process based on weights
        FairShare           // Equal processing across priorities
    }

    public class MultiplexingOptions
    {
        public MultiplexingStrategy Strategy { get; set; } = MultiplexingStrategy.RoundRobin;
        public OverflowStrategy OverflowStrategy { get; set; } = OverflowStrategy.Wait;
    }

    public enum MultiplexingStrategy
    {
        RoundRobin,
        LeastLoaded,
        Broadcast,
        FirstAvailable
    }

    public enum OverflowStrategy
    {
        Wait,
        Drop,
        Redirect
    }

    internal class PriorityItem<T>
    {
        public T Item { get; set; } = default!;
        public Priority Priority { get; set; }
        public DateTime EnqueuedAt { get; set; }
    }

    public class PriorityChannelStatistics
    {
        public string ChannelName { get; set; } = string.Empty;
        public int PriorityLevels { get; set; }
        public long TotalItemsProcessed { get; set; }
        public PriorityProcessingMode ProcessingMode { get; set; }
        public List<PriorityLevelStatistics> PriorityStatistics { get; set; } = new();
    }

    public class PriorityLevelStatistics
    {
        public Priority Priority { get; set; }
        public long ItemsQueued { get; set; }
        public long ItemsProcessed { get; set; }
        public double Utilization { get; set; }
        public bool IsFull { get; set; }
    }
}