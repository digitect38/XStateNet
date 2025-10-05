using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.Caching;
using XStateNet.Semi.Secs;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// High-performance cache for SECS messages with memory management
    /// </summary>
    public class SecsMessageCache : IDisposable
    {
        private readonly MemoryCache _cache;
        private readonly ConcurrentDictionary<string, CacheStatistics> _statistics = new();
        private readonly ILogger<SecsMessageCache>? _logger;
        private readonly object _lock = new();
        private bool _disposed;

        // Cache configuration
        public long MaxMemorySize { get; set; } = 100 * 1024 * 1024; // 100MB default
        public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromMinutes(2);
        public int MaxItemCount { get; set; } = 10000;
        public bool EnableCompression { get; set; } = true;

        // Global statistics - thread-safe counters
        private long _totalHits;
        private long _totalMisses;
        private long _totalEvictions;

        public long TotalHits => Interlocked.Read(ref _totalHits);
        public long TotalMisses => Interlocked.Read(ref _totalMisses);
        public long TotalEvictions => Interlocked.Read(ref _totalEvictions);
        public double HitRate
        {
            get
            {
                var hits = Interlocked.Read(ref _totalHits);
                var misses = Interlocked.Read(ref _totalMisses);
                return hits + misses > 0 ? (double)hits / (hits + misses) : 0;
            }
        }

        public SecsMessageCache(ILogger<SecsMessageCache>? logger = null)
        {
            _logger = logger;

            // Create a unique instance with a simple configuration
            var cacheId = $"SecsMessageCache_{Guid.NewGuid():N}";
            _cache = new MemoryCache(cacheId);
        }

        /// <summary>
        /// Cache a SECS message
        /// </summary>
        public void CacheMessage(string key, SecsMessage message, CachePriority priority = CachePriority.Normal)
        {
            if (_disposed)
            {
                _logger?.LogWarning("Cache is disposed, cannot cache message");
                return;
            }

            if (message == null)
            {
                _logger?.LogWarning("Cannot cache null message for key {Key}", key);
                return;
            }

            try
            {
                var policy = new CacheItemPolicy
                {
                    // Use only AbsoluteExpiration, not both
                    AbsoluteExpiration = DateTimeOffset.UtcNow.Add(DefaultExpiration),
                    Priority = priority == CachePriority.High
                        ? System.Runtime.Caching.CacheItemPriority.NotRemovable
                        : System.Runtime.Caching.CacheItemPriority.Default,
                    RemovedCallback = OnCacheItemRemoved
                };

                var now = DateTime.UtcNow;
                var cacheItem = new CachedMessage
                {
                    Message = message,
                    CachedAt = now,
                    LastAccessTime = now,
                    AccessCount = 0,
                    Size = EstimateMessageSize(message),
                    CompressionLevel = EnableCompression ? CompressionLevel.Optimal : CompressionLevel.None
                };

                _cache.Set(key, cacheItem, policy);
                UpdateStatistics(key, CacheOperation.Add);

                _logger?.LogDebug("Cached message {Key} ({Size} bytes, S{Stream}F{Function})",
                    key, cacheItem.Size, message.Stream, message.Function);

                // Verify the cache immediately
                var test = _cache.Get(key);
                if (test == null)
                {
                    _logger?.LogError("Cache verification failed - item not found immediately after caching for key {Key}", key);
                }
                else
                {
                    _logger?.LogDebug("Cache verification successful for key {Key}, type: {Type}", key, test.GetType().FullName);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to cache message {Key}", key);
            }
        }

        /// <summary>
        /// Retrieve a cached SECS message
        /// </summary>
        public SecsMessage? GetMessage(string key)
        {
            if (_disposed)
                return null;

            try
            {
                var cacheItem = _cache.Get(key);

                if (cacheItem != null)
                {
                    _logger?.LogDebug("Cache item found for key {Key}, type: {Type}", key, cacheItem.GetType().FullName);

                    if (cacheItem is CachedMessage cached)
                    {
                        Interlocked.Increment(ref _totalHits);
                        UpdateStatistics(key, CacheOperation.Hit);
                        cached.LastAccessTime = DateTime.UtcNow;
                        cached.AccessCount++;  // CachedMessage is a reference type, so direct increment is fine
                        return cached.Message;
                    }
                    else
                    {
                        _logger?.LogWarning("Cache item is not CachedMessage type, actual type: {Type}", cacheItem.GetType().FullName);
                    }
                }

                Interlocked.Increment(ref _totalMisses);
                UpdateStatistics(key, CacheOperation.Miss);
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to retrieve message {Key}", key);
                return null;
            }
        }

        /// <summary>
        /// Batch cache multiple messages
        /// </summary>
        public void CacheMessageBatch(ConcurrentDictionary<string, SecsMessage> messages, CachePriority priority = CachePriority.Normal)
        {
            if (_disposed || messages == null)
                return;

            foreach (var kvp in messages)
            {
                CacheMessage(kvp.Key, kvp.Value, priority);
            }
        }

        /// <summary>
        /// Get or create a message with factory function
        /// </summary>
        public SecsMessage GetOrCreate(string key, Func<SecsMessage> factory, CachePriority priority = CachePriority.Normal)
        {
            var existing = GetMessage(key);
            if (existing != null)
                return existing;

            var message = factory();
            CacheMessage(key, message, priority);
            return message;
        }

        /// <summary>
        /// Remove a specific message from cache
        /// </summary>
        public bool Remove(string key)
        {
            if (_disposed)
                return false;

            var removed = _cache.Remove(key) != null;
            if (removed)
            {
                UpdateStatistics(key, CacheOperation.Remove);
            }
            return removed;
        }

        /// <summary>
        /// Clear all cached messages
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                return;

            var keys = _cache.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                _cache.Remove(key);
            }

            _statistics.Clear();
            Interlocked.Exchange(ref _totalHits, 0);
            Interlocked.Exchange(ref _totalMisses, 0);
            Interlocked.Exchange(ref _totalEvictions, 0);

            _logger?.LogInformation("Cache cleared ({Count} items removed)", keys.Count);
        }

        /// <summary>
        /// Get cache statistics for a specific key pattern
        /// </summary>
        public IEnumerable<KeyValuePair<string, CacheStatistics>> GetStatistics(string? pattern = null)
        {
            if (string.IsNullOrEmpty(pattern))
                return _statistics.ToList();

            return _statistics.Where(kvp => kvp.Key.Contains(pattern));
        }

        /// <summary>
        /// Preload commonly used messages
        /// </summary>
        public void PreloadCommonMessages()
        {
            // Preload common SECS messages for better performance
            var commonMessages = new ConcurrentDictionary<string, SecsMessage>
            {
                ["S1F1"] = SecsMessageLibrary.S1F1(),
                ["S1F2"] = SecsMessageLibrary.S1F2(),
                ["S1F13"] = SecsMessageLibrary.S1F13(),
                ["S1F14"] = SecsMessageLibrary.S1F14(),
                ["S5F2"] = SecsMessageLibrary.S5F2(SecsMessageLibrary.ResponseCodes.ACKC5_ACCEPTED),
                ["S6F12"] = SecsMessageLibrary.S6F12(SecsMessageLibrary.ResponseCodes.ACKC6_ACCEPTED)
            };

            CacheMessageBatch(commonMessages, CachePriority.High);
            _logger?.LogInformation("Preloaded {Count} common messages", commonMessages.Count);
        }

        private void OnCacheItemRemoved(CacheEntryRemovedArguments args)
        {
            if (args.RemovedReason == CacheEntryRemovedReason.Evicted ||
                args.RemovedReason == CacheEntryRemovedReason.Expired)
            {
                Interlocked.Increment(ref _totalEvictions);
                UpdateStatistics(args.CacheItem.Key, CacheOperation.Evict);

                _logger?.LogDebug("Cache item {Key} removed: {Reason}",
                    args.CacheItem.Key, args.RemovedReason);
            }
        }

        private void UpdateStatistics(string key, CacheOperation operation)
        {
            var stats = _statistics.GetOrAdd(key, k => new CacheStatistics { Key = k });

            switch (operation)
            {
                case CacheOperation.Add:
                    Interlocked.Increment(ref stats._addCount);
                    break;
                case CacheOperation.Hit:
                    Interlocked.Increment(ref stats._hitCount);
                    break;
                case CacheOperation.Miss:
                    Interlocked.Increment(ref stats._missCount);
                    break;
                case CacheOperation.Remove:
                    Interlocked.Increment(ref stats._removeCount);
                    break;
                case CacheOperation.Evict:
                    Interlocked.Increment(ref stats._evictCount);
                    break;
            }

            stats.LastOperation = operation;
            stats.LastOperationTime = DateTime.UtcNow;
        }

        private long EstimateMessageSize(SecsMessage message)
        {
            // Rough estimation of message size in bytes
            long size = 16; // Base overhead

            if (message.Data != null)
            {
                size += EstimateItemSize(message.Data);
            }

            return size;
        }

        private long EstimateItemSize(SecsItem item)
        {
            return item.Format switch
            {
                SecsFormat.List => item is SecsList list
                    ? list.Items.Sum(i => EstimateItemSize(i)) + 8
                    : 8,
                SecsFormat.Binary => item.Length + 8,
                SecsFormat.ASCII => item.Length + 8,
                SecsFormat.U1 => item.Length + 8,
                SecsFormat.U2 => item.Length * 2 + 8,
                SecsFormat.U4 => item.Length * 4 + 8,
                SecsFormat.U8 => item.Length * 8 + 8,
                SecsFormat.I1 => item.Length + 8,
                SecsFormat.I2 => item.Length * 2 + 8,
                SecsFormat.I4 => item.Length * 4 + 8,
                SecsFormat.I8 => item.Length * 8 + 8,
                SecsFormat.F4 => item.Length * 4 + 8,
                SecsFormat.F8 => item.Length * 8 + 8,
                _ => 16
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cache?.Dispose();
        }

        private class CachedMessage
        {
            public SecsMessage Message { get; set; } = null!;
            public DateTime CachedAt { get; set; }
            public DateTime LastAccessTime { get; set; }
            public long AccessCount { get; set; }  // Changed to long for Interlocked operations
            public long Size { get; set; }
            public CompressionLevel CompressionLevel { get; set; }
        }

        public class CacheStatistics
        {
            public string Key { get; set; } = "";
            internal long _hitCount;
            internal long _missCount;
            internal long _addCount;
            internal long _removeCount;
            internal long _evictCount;

            public long HitCount => Interlocked.Read(ref _hitCount);
            public long MissCount => Interlocked.Read(ref _missCount);
            public long AddCount => Interlocked.Read(ref _addCount);
            public long RemoveCount => Interlocked.Read(ref _removeCount);
            public long EvictCount => Interlocked.Read(ref _evictCount);

            public CacheOperation LastOperation { get; set; }
            public DateTime LastOperationTime { get; set; }
            public double HitRate
            {
                get
                {
                    var hits = Interlocked.Read(ref _hitCount);
                    var misses = Interlocked.Read(ref _missCount);
                    return hits + misses > 0 ? (double)hits / (hits + misses) : 0;
                }
            }
        }

        public enum CachePriority
        {
            Low,
            Normal,
            High
        }

        public enum CacheOperation
        {
            Add,
            Hit,
            Miss,
            Remove,
            Evict
        }

        private enum CompressionLevel
        {
            None,
            Fastest,
            Optimal
        }
    }
}