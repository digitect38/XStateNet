using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;

namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// Improved circuit breaker with memory-efficient bucketed statistics,
    /// persistent state, jitter, and proper cancellation token propagation
    /// </summary>
    public sealed class ImprovedCircuitBreaker : ICircuitBreaker
    {
        private readonly string _name;
        private readonly ImprovedCircuitBreakerOptions _options;
        private readonly ICircuitBreakerMetrics _metrics;
        private readonly ICircuitBreakerStateStore? _stateStore;
        private readonly Random _random = new();

        // Lock-free state management
        private int _state;
        private long _consecutiveFailures;
        private long _successCount;
        private long _lastStateChangeTime;
        private readonly Timer _halfOpenTimer;

        // Bucketed sliding window for efficient memory usage
        private readonly BucketedSlidingWindow _failureWindow;

        public CircuitState State => (CircuitState)_state;
        public string Name => _name;

        public event EventHandler<CircuitStateChangedEventArgs>? StateChanged;

        public ImprovedCircuitBreaker(
            string name,
            ImprovedCircuitBreakerOptions options,
            ICircuitBreakerMetrics? metrics = null,
            ICircuitBreakerStateStore? stateStore = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics ?? new NullMetrics();
            _stateStore = stateStore;

            _failureWindow = new BucketedSlidingWindow(
                options.SamplingDuration,
                options.BucketCount);

            _lastStateChangeTime = Stopwatch.GetTimestamp();
            _halfOpenTimer = new Timer(_ => TryTransitionToHalfOpen(), null, Timeout.Infinite, Timeout.Infinite);

            // Restore state from persistent storage if available
            Task.Run(async () => await RestoreStateAsync());
        }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (_state == (int)CircuitState.Open)
            {
                if (ShouldAttemptReset())
                {
                    TransitionToHalfOpen();
                }
                else
                {
                    _metrics.RecordRejection(_name);
                    throw new CircuitBreakerOpenException($"Circuit breaker '{_name}' is open");
                }
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await operation(cancellationToken).ConfigureAwait(false);
                OnSuccess();
                _metrics.RecordSuccess(_name, stopwatch.Elapsed);
                return result;
            }
            catch (Exception ex) when (!(ex is CircuitBreakerOpenException))
            {
                OnFailure(ex);
                _metrics.RecordFailure(_name, stopwatch.Elapsed, ex.GetType().Name);
                throw;
            }
        }

        // Overload for non-cancellable operations (backward compatibility)
        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(_ => operation(), cancellationToken);
        }

        public Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(_ => Task.FromResult(operation()), cancellationToken);
        }

        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async ct =>
            {
                await operation().ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        private void OnSuccess()
        {
            _failureWindow.RecordSuccess();

            if (_state == (int)CircuitState.HalfOpen)
            {
                var successCount = Interlocked.Increment(ref _successCount);
                if (successCount >= _options.SuccessCountInHalfOpen)
                {
                    TransitionToClosed();
                }
            }
            else if (_state == (int)CircuitState.Closed)
            {
                Interlocked.Exchange(ref _consecutiveFailures, 0);
            }
        }

        private void OnFailure(Exception exception)
        {
            _failureWindow.RecordFailure();

            if (_state == (int)CircuitState.HalfOpen)
            {
                TransitionToOpen(exception);
                return;
            }

            if (_state == (int)CircuitState.Closed)
            {
                var failures = Interlocked.Increment(ref _consecutiveFailures);

                // Check consecutive failure threshold
                if (failures >= _options.FailureThreshold)
                {
                    TransitionToOpen(exception);
                    return;
                }

                // Check failure rate-based opening
                var stats = _failureWindow.GetStatistics();
                if (stats.TotalCount >= _options.MinimumThroughput &&
                    stats.FailureRate >= _options.FailureRateThreshold)
                {
                    TransitionToOpen(exception);
                }
            }
        }

        private void TransitionToOpen(Exception lastException)
        {
            var currentState = _state;
            if (currentState != (int)CircuitState.Open &&
                Interlocked.CompareExchange(ref _state, (int)CircuitState.Open, currentState) == currentState)
            {
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                Interlocked.Exchange(ref _lastStateChangeTime, Stopwatch.GetTimestamp());

                // Apply jitter to break duration to prevent thundering herd
                var jitteredDuration = GetJitteredBreakDuration();
                _halfOpenTimer.Change(jitteredDuration, Timeout.InfiniteTimeSpan);

                // Persist state change
                Task.Run(async () => await PersistStateAsync());

                StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                {
                    CircuitBreakerName = _name,
                    FromState = (CircuitState)currentState,
                    ToState = CircuitState.Open,
                    LastException = lastException
                });
            }
        }

        private void TransitionToHalfOpen()
        {
            if (Interlocked.CompareExchange(ref _state, (int)CircuitState.HalfOpen, (int)CircuitState.Open) == (int)CircuitState.Open)
            {
                Interlocked.Exchange(ref _successCount, 0);
                Interlocked.Exchange(ref _lastStateChangeTime, Stopwatch.GetTimestamp());

                // Persist state change
                Task.Run(async () => await PersistStateAsync());

                StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                {
                    CircuitBreakerName = _name,
                    FromState = CircuitState.Open,
                    ToState = CircuitState.HalfOpen,
                    LastException = null
                });
            }
        }

        private void TransitionToClosed()
        {
            var previousState = _state;
            if (previousState != (int)CircuitState.Closed &&
                Interlocked.CompareExchange(ref _state, (int)CircuitState.Closed, previousState) == previousState)
            {
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                Interlocked.Exchange(ref _lastStateChangeTime, Stopwatch.GetTimestamp());

                // Cancel any scheduled timer
                _halfOpenTimer.Change(Timeout.Infinite, Timeout.Infinite);

                // Clear persisted state
                Task.Run(async () => await ClearPersistedStateAsync());

                StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                {
                    CircuitBreakerName = _name,
                    FromState = (CircuitState)previousState,
                    ToState = CircuitState.Closed,
                    LastException = null
                });
            }
        }

        private bool ShouldAttemptReset()
        {
            var elapsed = Stopwatch.GetTimestamp() - _lastStateChangeTime;
            var elapsedTime = TimeSpan.FromTicks(elapsed * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

            // Use jittered break duration for checking
            var jitteredDuration = GetJitteredBreakDuration();
            return elapsedTime >= jitteredDuration;
        }

        private TimeSpan GetJitteredBreakDuration()
        {
            // Add random jitter of ±10% to prevent thundering herd
            var jitterFactor = 1 + (_random.NextDouble() * 0.2 - 0.1); // Range: 0.9 to 1.1
            return TimeSpan.FromMilliseconds(_options.BreakDuration.TotalMilliseconds * jitterFactor);
        }

        private void TryTransitionToHalfOpen()
        {
            if (_state == (int)CircuitState.Open)
            {
                TransitionToHalfOpen();
            }
        }

        private async Task RestoreStateAsync()
        {
            if (_stateStore == null) return;

            try
            {
                var persistedState = await _stateStore.GetStateAsync(_name);
                if (persistedState != null && persistedState.State == CircuitState.Open)
                {
                    // Check if the break duration has expired
                    var elapsed = DateTime.UtcNow - persistedState.LastStateChangeTime;
                    if (elapsed < _options.BreakDuration)
                    {
                        // Restore open state
                        _state = (int)CircuitState.Open;
                        _lastStateChangeTime = Stopwatch.GetTimestamp();

                        // Schedule transition to half-open
                        var remainingTime = _options.BreakDuration - elapsed;
                        var jitteredRemaining = GetJitteredTimeSpan(remainingTime);
                        _halfOpenTimer.Change(jitteredRemaining, Timeout.InfiniteTimeSpan);
                    }
                }
            }
            catch
            {
                // Don't fail startup if state restore fails
            }
        }

        private TimeSpan GetJitteredTimeSpan(TimeSpan baseTime)
        {
            var jitterFactor = 1 + (_random.NextDouble() * 0.2 - 0.1);
            return TimeSpan.FromMilliseconds(baseTime.TotalMilliseconds * jitterFactor);
        }

        private async Task PersistStateAsync()
        {
            if (_stateStore == null) return;

            try
            {
                await _stateStore.SaveStateAsync(_name, new CircuitBreakerState
                {
                    State = (CircuitState)_state,
                    LastStateChangeTime = DateTime.UtcNow,
                    ConsecutiveFailures = _consecutiveFailures
                });
            }
            catch
            {
                // Don't fail operations if persistence fails
            }
        }

        private async Task ClearPersistedStateAsync()
        {
            if (_stateStore == null) return;

            try
            {
                await _stateStore.DeleteStateAsync(_name);
            }
            catch
            {
                // Don't fail operations if persistence fails
            }
        }

        public void Dispose()
        {
            _halfOpenTimer?.Dispose();
        }
    }

    /// <summary>
    /// Memory-efficient bucketed sliding window implementation
    /// </summary>
    internal sealed class BucketedSlidingWindow
    {
        private readonly TimeSpan _windowSize;
        private readonly int _bucketCount;
        private readonly TimeSpan _bucketSize;
        private readonly Bucket[] _buckets;
        private long _currentBucketIndex;

        public BucketedSlidingWindow(TimeSpan windowSize, int bucketCount = 10)
        {
            _windowSize = windowSize;
            _bucketCount = bucketCount;
            _bucketSize = TimeSpan.FromMilliseconds(windowSize.TotalMilliseconds / bucketCount);
            _buckets = new Bucket[bucketCount];

            for (int i = 0; i < bucketCount; i++)
            {
                _buckets[i] = new Bucket();
            }
        }

        public void RecordSuccess()
        {
            GetCurrentBucket().RecordSuccess();
        }

        public void RecordFailure()
        {
            GetCurrentBucket().RecordFailure();
        }

        public (long TotalCount, double FailureRate) GetStatistics()
        {
            PruneOldBuckets();

            long totalSuccess = 0;
            long totalFailure = 0;

            for (int i = 0; i < _bucketCount; i++)
            {
                var bucket = _buckets[i];
                if (bucket.IsActive)
                {
                    totalSuccess += bucket.SuccessCount;
                    totalFailure += bucket.FailureCount;
                }
            }

            var total = totalSuccess + totalFailure;
            var failureRate = total == 0 ? 0 : (double)totalFailure / total;

            return (total, failureRate);
        }

        private Bucket GetCurrentBucket()
        {
            var now = Stopwatch.GetTimestamp();
            var bucketIndex = (now / (Stopwatch.Frequency * (long)_bucketSize.TotalSeconds)) % _bucketCount;

            var bucket = _buckets[bucketIndex];

            // Check if this bucket needs to be reset (it's from a previous window)
            if (bucket.LastUpdateTime < now - (long)(_windowSize.TotalSeconds * Stopwatch.Frequency))
            {
                bucket.Reset(now);
            }

            return bucket;
        }

        private void PruneOldBuckets()
        {
            var now = Stopwatch.GetTimestamp();
            var cutoff = now - (long)(_windowSize.TotalSeconds * Stopwatch.Frequency);

            for (int i = 0; i < _bucketCount; i++)
            {
                if (_buckets[i].LastUpdateTime < cutoff)
                {
                    _buckets[i].Deactivate();
                }
            }
        }

        private sealed class Bucket
        {
            private long _successCount;
            private long _failureCount;
            private long _lastUpdateTime;
            private int _isActive;

            public long SuccessCount => _successCount;
            public long FailureCount => _failureCount;
            public long LastUpdateTime => Volatile.Read(ref _lastUpdateTime);
            public bool IsActive => Volatile.Read(ref _isActive) == 1;

            public void RecordSuccess()
            {
                Interlocked.Increment(ref _successCount);
                UpdateTime();
            }

            public void RecordFailure()
            {
                Interlocked.Increment(ref _failureCount);
                UpdateTime();
            }

            public void Reset(long timestamp)
            {
                Interlocked.Exchange(ref _successCount, 0);
                Interlocked.Exchange(ref _failureCount, 0);
                Interlocked.Exchange(ref _lastUpdateTime, timestamp);
                Interlocked.Exchange(ref _isActive, 1);
            }

            public void Deactivate()
            {
                Interlocked.Exchange(ref _isActive, 0);
            }

            private void UpdateTime()
            {
                var now = Stopwatch.GetTimestamp();
                Volatile.Write(ref _lastUpdateTime, now);
                Volatile.Write(ref _isActive, 1);
            }
        }
    }

    public class ImprovedCircuitBreakerOptions : CircuitBreakerOptions
    {
        /// <summary>
        /// Number of buckets for the sliding window (default: 10)
        /// </summary>
        public int BucketCount { get; set; } = 10;

        /// <summary>
        /// Enable jitter for break duration to prevent thundering herd (default: true)
        /// </summary>
        public bool EnableJitter { get; set; } = true;

        /// <summary>
        /// Jitter percentage range (default: 0.1 = ±10%)
        /// </summary>
        public double JitterFactor { get; set; } = 0.1;
    }

    /// <summary>
    /// Interface for persisting circuit breaker state
    /// </summary>
    public interface ICircuitBreakerStateStore
    {
        Task<CircuitBreakerState?> GetStateAsync(string circuitBreakerName);
        Task SaveStateAsync(string circuitBreakerName, CircuitBreakerState state);
        Task DeleteStateAsync(string circuitBreakerName);
    }

    public class CircuitBreakerState
    {
        public CircuitState State { get; set; }
        public DateTime LastStateChangeTime { get; set; }
        public long ConsecutiveFailures { get; set; }
    }

    /// <summary>
    /// Redis-based implementation of circuit breaker state store
    /// </summary>
    public class RedisCircuitBreakerStateStore : ICircuitBreakerStateStore
    {
        private readonly IDistributedCache _cache;
        private readonly string _keyPrefix;

        public RedisCircuitBreakerStateStore(IDistributedCache cache, string keyPrefix = "cb:")
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _keyPrefix = keyPrefix;
        }

        public async Task<CircuitBreakerState?> GetStateAsync(string circuitBreakerName)
        {
            var key = $"{_keyPrefix}{circuitBreakerName}";
            var data = await _cache.GetAsync(key);

            if (data == null) return null;

            // Simple JSON deserialization (you could use System.Text.Json or Newtonsoft)
            var json = System.Text.Encoding.UTF8.GetString(data);
            return System.Text.Json.JsonSerializer.Deserialize<CircuitBreakerState>(json);
        }

        public async Task SaveStateAsync(string circuitBreakerName, CircuitBreakerState state)
        {
            var key = $"{_keyPrefix}{circuitBreakerName}";
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            var data = System.Text.Encoding.UTF8.GetBytes(json);

            await _cache.SetAsync(key, data, new DistributedCacheEntryOptions
            {
                // Keep state for 2x break duration to handle restarts
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });
        }

        public async Task DeleteStateAsync(string circuitBreakerName)
        {
            var key = $"{_keyPrefix}{circuitBreakerName}";
            await _cache.RemoveAsync(key);
        }
    }
}