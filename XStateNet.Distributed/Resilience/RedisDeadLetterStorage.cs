using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// Redis-based persistent storage for Dead Letter Queue
    /// </summary>
    public sealed class RedisDeadLetterStorage : IDeadLetterStorage, IDisposable
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _database;
        private readonly ILogger<RedisDeadLetterStorage>? _logger;
        private readonly RedisDeadLetterStorageOptions _options;

        // Redis key patterns
        private readonly string _entryKeyPrefix;
        private readonly string _indexKey;
        private readonly string _sourceIndexPrefix;
        private readonly string _expiryIndexKey;

        public RedisDeadLetterStorage(
            IConnectionMultiplexer redis,
            RedisDeadLetterStorageOptions? options = null,
            ILogger<RedisDeadLetterStorage>? logger = null)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _options = options ?? new RedisDeadLetterStorageOptions();
            _logger = logger;
            _database = _redis.GetDatabase(_options.DatabaseIndex);

            // Initialize key patterns
            _entryKeyPrefix = $"{_options.KeyPrefix}:entry:";
            _indexKey = $"{_options.KeyPrefix}:index";
            _sourceIndexPrefix = $"{_options.KeyPrefix}:source:";
            _expiryIndexKey = $"{_options.KeyPrefix}:expiry";
        }

        public async Task SaveAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
        {
            try
            {
                var entryKey = GetEntryKey(entry.Id);
                var entryData = MessagePackSerializer.Serialize(entry);

                var transaction = _database.CreateTransaction();

                // Store the entry
                _ = transaction.StringSetAsync(entryKey, entryData, entry.ExpiresAt - DateTime.UtcNow);

                // Add to main index (sorted by enqueue time)
                _ = transaction.SortedSetAddAsync(
                    _indexKey,
                    entry.Id,
                    entry.EnqueuedAt.ToUnixTimeMilliseconds());

                // Add to source index
                _ = transaction.SortedSetAddAsync(
                    GetSourceIndexKey(entry.Source),
                    entry.Id,
                    entry.EnqueuedAt.ToUnixTimeMilliseconds());

                // Add to expiry index
                _ = transaction.SortedSetAddAsync(
                    _expiryIndexKey,
                    entry.Id,
                    entry.ExpiresAt.ToUnixTimeMilliseconds());

                var success = await transaction.ExecuteAsync();

                if (!success)
                {
                    _logger?.LogWarning("Failed to save DLQ entry {MessageId} to Redis", entry.Id);
                    throw new InvalidOperationException($"Failed to save DLQ entry {entry.Id}");
                }

                _logger?.LogDebug("Saved DLQ entry {MessageId} to Redis", entry.Id);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger?.LogError(ex, "Error saving DLQ entry {MessageId} to Redis", entry.Id);
                throw;
            }
        }

        public async Task UpdateAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
        {
            try
            {
                var entryKey = GetEntryKey(entry.Id);
                var entryData = MessagePackSerializer.Serialize(entry);

                // Update with remaining TTL
                var ttl = entry.ExpiresAt - DateTime.UtcNow;
                if (ttl > TimeSpan.Zero)
                {
                    await _database.StringSetAsync(entryKey, entryData, ttl);
                    _logger?.LogDebug("Updated DLQ entry {MessageId} in Redis", entry.Id);
                }
                else
                {
                    // Entry has expired, delete it
                    await DeleteAsync(entry.Id, cancellationToken);
                    _logger?.LogDebug("Deleted expired DLQ entry {MessageId} from Redis", entry.Id);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating DLQ entry {MessageId} in Redis", entry.Id);
                throw;
            }
        }

        public async Task<DeadLetterEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default)
        {
            try
            {
                var entryKey = GetEntryKey(messageId);
                var entryData = await _database.StringGetAsync(entryKey);

                if (!entryData.HasValue)
                {
                    return null;
                }

                var entry = MessagePackSerializer.Deserialize<DeadLetterEntry>(entryData!);
                return entry;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving DLQ entry {MessageId} from Redis", messageId);
                throw;
            }
        }

        public async Task DeleteAsync(string messageId, CancellationToken cancellationToken = default)
        {
            try
            {
                var entryKey = GetEntryKey(messageId);

                // Get entry to find source for index cleanup
                var entryData = await _database.StringGetAsync(entryKey);
                if (entryData.HasValue)
                {
                    var entry = MessagePackSerializer.Deserialize<DeadLetterEntry>(entryData!);

                    var transaction = _database.CreateTransaction();

                    // Delete the entry
                    _ = transaction.KeyDeleteAsync(entryKey);

                    // Remove from indexes
                    _ = transaction.SortedSetRemoveAsync(_indexKey, messageId);
                    _ = transaction.SortedSetRemoveAsync(GetSourceIndexKey(entry.Source), messageId);
                    _ = transaction.SortedSetRemoveAsync(_expiryIndexKey, messageId);

                    await transaction.ExecuteAsync();

                    _logger?.LogDebug("Deleted DLQ entry {MessageId} from Redis", messageId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting DLQ entry {MessageId} from Redis", messageId);
                throw;
            }
        }

        public async Task<IEnumerable<DeadLetterEntry>> ListAsync(
            int maxCount = 100,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get message IDs from the index (ordered by enqueue time)
                var messageIds = await _database.SortedSetRangeByRankAsync(
                    _indexKey,
                    0,
                    maxCount - 1,
                    Order.Ascending);

                if (messageIds.Length == 0)
                {
                    return Enumerable.Empty<DeadLetterEntry>();
                }

                // Batch get all entries
                var keys = messageIds.Select(id => (RedisKey)GetEntryKey(id.ToString())).ToArray();
                var values = await _database.StringGetAsync(keys);

                var entries = new List<DeadLetterEntry>();
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i].HasValue)
                    {
                        var entry = MessagePackSerializer.Deserialize<DeadLetterEntry>(values[i]!);
                        entries.Add(entry);
                    }
                }

                return entries;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error listing DLQ entries from Redis");
                throw;
            }
        }

        public async Task<IEnumerable<DeadLetterEntry>> ListBySourceAsync(
            string source,
            int maxCount = 100,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var sourceKey = GetSourceIndexKey(source);
                var messageIds = await _database.SortedSetRangeByRankAsync(
                    sourceKey,
                    0,
                    maxCount - 1,
                    Order.Ascending);

                if (messageIds.Length == 0)
                {
                    return Enumerable.Empty<DeadLetterEntry>();
                }

                var keys = messageIds.Select(id => (RedisKey)GetEntryKey(id.ToString())).ToArray();
                var values = await _database.StringGetAsync(keys);

                var entries = new List<DeadLetterEntry>();
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i].HasValue)
                    {
                        var entry = MessagePackSerializer.Deserialize<DeadLetterEntry>(values[i]!);
                        entries.Add(entry);
                    }
                }

                return entries;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error listing DLQ entries by source from Redis");
                throw;
            }
        }

        public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var now = DateTime.UtcNow.ToUnixTimeMilliseconds();

                // Get expired message IDs
                var expiredIds = await _database.SortedSetRangeByScoreAsync(
                    _expiryIndexKey,
                    0,
                    now,
                    take: 100); // Process in batches

                if (expiredIds.Length == 0)
                {
                    return 0;
                }

                var deletedCount = 0;
                foreach (var messageId in expiredIds)
                {
                    await DeleteAsync(messageId.ToString(), cancellationToken);
                    deletedCount++;
                }

                _logger?.LogInformation("Cleaned up {Count} expired DLQ entries from Redis", deletedCount);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error cleaning up expired DLQ entries from Redis");
                throw;
            }
        }

        public async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _database.SortedSetLengthAsync(_indexKey);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting DLQ count from Redis");
                throw;
            }
        }

        public async Task<Dictionary<string, long>> GetCountBySourceAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = new Dictionary<string, long>();

                // Get all source index keys
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                var keys = server.Keys(
                    _options.DatabaseIndex,
                    $"{_sourceIndexPrefix}*",
                    pageSize: 100);

                foreach (var key in keys)
                {
                    var source = key.ToString().Replace(_sourceIndexPrefix, "");
                    var count = await _database.SortedSetLengthAsync(key);
                    result[source] = count;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting DLQ count by source from Redis");
                throw;
            }
        }

        private string GetEntryKey(string messageId) => $"{_entryKeyPrefix}{messageId}";
        private string GetSourceIndexKey(string source) => $"{_sourceIndexPrefix}{source}";

        public void Dispose()
        {
            // Connection multiplexer disposal should be handled by the caller
            // as it might be shared across multiple components
        }
    }

    public class RedisDeadLetterStorageOptions
    {
        public string KeyPrefix { get; set; } = "dlq";
        public int DatabaseIndex { get; set; } = 0;
        public bool EnableCompression { get; set; } = false;
    }

    /// <summary>
    /// Extension methods for Unix time conversions
    /// </summary>
    internal static class DateTimeExtensions
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long ToUnixTimeMilliseconds(this DateTime dateTime)
        {
            return (long)(dateTime.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
        }

        public static DateTime FromUnixTimeMilliseconds(long milliseconds)
        {
            return UnixEpoch.AddMilliseconds(milliseconds);
        }
    }
}