using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace XStateNet
{
    /// <summary>
    /// Simple bounded cache implementation
    /// </summary>
    public class BoundedCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, (TValue value, DateTime lastAccess)> _cache = new();
        private readonly long _sizeLimit;
        private long _currentSize;
        private readonly ReaderWriterLockSlim _sizeLock = new();
        private readonly TimeSpan _itemTimeout;

        public BoundedCache(string name, long sizeLimit = 1000)
        {
            _sizeLimit = sizeLimit;
            _itemTimeout = TimeSpan.FromMinutes(10);
        }

        /// <summary>
        /// Get or add item to cache
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            // Try to get existing value
            if (_cache.TryGetValue(key, out var existing))
            {
                // Update last access time
                _cache.TryUpdate(key, (existing.value, DateTime.UtcNow), existing);
                return existing.value;
            }

            // Check size limit
            _sizeLock.EnterUpgradeableReadLock();
            try
            {
                if (Interlocked.Read(ref _currentSize) >= _sizeLimit)
                {
                    // Evict oldest items (approx 10% of cache)
                    EvictOldest((int)(_sizeLimit * 0.1));
                }

                _sizeLock.EnterWriteLock();
                try
                {
                    var value = valueFactory(key);
                    var entry = (value, DateTime.UtcNow);
                    if (_cache.TryAdd(key, entry))
                    {
                        Interlocked.Increment(ref _currentSize);
                    }
                    return value;
                }
                finally
                {
                    _sizeLock.ExitWriteLock();
                }
            }
            finally
            {
                _sizeLock.ExitUpgradeableReadLock();
            }
        }

        /// <summary>
        /// Try get value from cache
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                // Update last access time
                _cache.TryUpdate(key, (entry.value, DateTime.UtcNow), entry);
                value = entry.value;
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Remove item from cache
        /// </summary>
        public bool Remove(TKey key)
        {
            if (_cache.TryRemove(key, out _))
            {
                Interlocked.Decrement(ref _currentSize);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clear all items
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            Interlocked.Exchange(ref _currentSize, 0);
        }

        /// <summary>
        /// Get current cache size
        /// </summary>
        public long Size => Interlocked.Read(ref _currentSize);

        private void EvictOldest(int count)
        {
            // Remove oldest accessed items
            var toRemove = _cache
                .OrderBy(x => x.Value.lastAccess)
                .Take(count)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                if (_cache.TryRemove(key, out _))
                {
                    Interlocked.Decrement(ref _currentSize);
                }
            }
        }

        public void Dispose()
        {
            _sizeLock?.Dispose();
        }
    }

    /// <summary>
    /// Bounded concurrent dictionary with size limits
    /// </summary>
    public class BoundedConcurrentDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, (TValue value, DateTime lastAccess)> _dictionary;
        private readonly int _maxSize;
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _itemTimeout;

        public BoundedConcurrentDictionary(int maxSize = 10000, TimeSpan? itemTimeout = null)
        {
            _dictionary = new ConcurrentDictionary<TKey, (TValue, DateTime)>();
            _maxSize = maxSize;
            _itemTimeout = itemTimeout ?? TimeSpan.FromMinutes(30);

            // Periodic cleanup every minute
            _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            // Check size before adding
            if (_dictionary.Count >= _maxSize)
            {
                RemoveOldest();
            }

            var result = _dictionary.GetOrAdd(key, k => (valueFactory(k), DateTime.UtcNow));

            // Update last access time
            _dictionary.TryUpdate(key, (result.value, DateTime.UtcNow), result);

            return result.value;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_dictionary.TryGetValue(key, out var item))
            {
                // Update last access time
                _dictionary.TryUpdate(key, (item.value, DateTime.UtcNow), item);
                value = item.value;
                return true;
            }

            value = default!;
            return false;
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            if (_dictionary.TryRemove(key, out var item))
            {
                value = item.value;
                return true;
            }

            value = default!;
            return false;
        }

        private void RemoveOldest()
        {
            // Remove 10% of oldest items
            var toRemove = _maxSize / 10;
            var items = _dictionary.ToList()
                .OrderBy(x => x.Value.lastAccess)
                .Take(toRemove)
                .Select(x => x.Key);

            foreach (var key in items)
            {
                _dictionary.TryRemove(key, out _);
            }
        }

        private void Cleanup(object? state)
        {
            var cutoff = DateTime.UtcNow - _itemTimeout;
            var expiredKeys = _dictionary
                .Where(x => x.Value.lastAccess < cutoff)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _dictionary.TryRemove(key, out _);
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}