using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// High-performance circuit breaker implementation for fault tolerance
    /// </summary>
    public sealed class CircuitBreaker : ICircuitBreaker
    {
        private readonly string _name;
        private readonly CircuitBreakerOptions _options;
        private readonly ICircuitBreakerMetrics _metrics;

        // Lock-free state management
        private int _state = (int)CircuitState.Closed;
        private long _consecutiveFailures;
        private long _successCount;
        private long _lastStateChangeTime;
        private readonly Timer _halfOpenTimer;

        // Sliding window for failure rate calculation
        private readonly SlidingWindow _failureWindow;

        public CircuitState State => (CircuitState)_state;
        public string Name => _name;

        public event EventHandler<CircuitStateChangedEventArgs>? StateChanged;

        public CircuitBreaker(string name, CircuitBreakerOptions options, ICircuitBreakerMetrics? metrics = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics ?? new NullMetrics();

            _failureWindow = new SlidingWindow(options.SamplingDuration);
            _lastStateChangeTime = Stopwatch.GetTimestamp();

            _halfOpenTimer = new Timer(_ => TryTransitionToHalfOpen(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
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
                var result = await operation().ConfigureAwait(false);
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

        public Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() => Task.FromResult(operation()), cancellationToken);
        }

        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
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

                // Check threshold-based opening
                if (failures >= _options.FailureThreshold)
                {
                    TransitionToOpen(exception);
                    return;
                }

                // Check failure rate-based opening
                var failureRate = _failureWindow.GetFailureRate();
                if (failureRate > _options.FailureRateThreshold &&
                    _failureWindow.TotalCount >= _options.MinimumThroughput)
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
                Interlocked.Exchange(ref _lastStateChangeTime, Stopwatch.GetTimestamp());
                _halfOpenTimer.Change(_options.BreakDuration, Timeout.InfiniteTimeSpan);

                StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                {
                    CircuitBreakerName = _name,
                    FromState = (CircuitState)_state,
                    ToState = CircuitState.Open,
                    LastException = lastException
                });

                _metrics.RecordStateChange(_name, CircuitState.Open);
            }
        }

        private void TransitionToHalfOpen()
        {
            if (Interlocked.CompareExchange(ref _state, (int)CircuitState.HalfOpen, (int)CircuitState.Open) == (int)CircuitState.Open)
            {
                Interlocked.Exchange(ref _successCount, 0);
                Interlocked.Exchange(ref _lastStateChangeTime, Stopwatch.GetTimestamp());

                StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                {
                    CircuitBreakerName = _name,
                    FromState = CircuitState.Open,
                    ToState = CircuitState.HalfOpen
                });

                _metrics.RecordStateChange(_name, CircuitState.HalfOpen);
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
                _halfOpenTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _failureWindow.Reset();

                StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                {
                    CircuitBreakerName = _name,
                    FromState = (CircuitState)previousState,
                    ToState = CircuitState.Closed
                });

                _metrics.RecordStateChange(_name, CircuitState.Closed);
            }
        }

        private bool ShouldAttemptReset()
        {
            var elapsed = Stopwatch.GetTimestamp() - _lastStateChangeTime;
            var elapsedTime = TimeSpan.FromTicks(elapsed * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            return elapsedTime >= _options.BreakDuration;
        }

        private void TryTransitionToHalfOpen()
        {
            if (_state == (int)CircuitState.Open)
            {
                TransitionToHalfOpen();
            }
        }

        public void Dispose()
        {
            _halfOpenTimer?.Dispose();
        }
    }

    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }

    public class CircuitBreakerOptions
    {
        public int FailureThreshold { get; set; } = 5;
        public double FailureRateThreshold { get; set; } = 0.5;
        public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
        public int SuccessCountInHalfOpen { get; set; } = 3;
        public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(1);
        public int MinimumThroughput { get; set; } = 10;
    }

    public interface ICircuitBreaker : IDisposable
    {
        string Name { get; }
        CircuitState State { get; }
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
        Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default);
        event EventHandler<CircuitStateChangedEventArgs> StateChanged;
    }

    public class CircuitStateChangedEventArgs : EventArgs
    {
        public string CircuitBreakerName { get; set; } = string.Empty;
        public CircuitState FromState { get; set; }
        public CircuitState ToState { get; set; }
        public Exception? LastException { get; set; }
    }

    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }

    /// <summary>
    /// Lock-free sliding window for tracking success/failure rates
    /// </summary>
    internal sealed class SlidingWindow
    {
        private readonly TimeSpan _windowSize;
        private readonly ConcurrentQueue<(long timestamp, bool success)> _events;
        private long _successCount;
        private long _failureCount;

        public long TotalCount => _successCount + _failureCount;

        public SlidingWindow(TimeSpan windowSize)
        {
            _windowSize = windowSize;
            _events = new ConcurrentQueue<(long, bool)>();
        }

        public void RecordSuccess()
        {
            Record(true);
        }

        public void RecordFailure()
        {
            Record(false);
        }

        private void Record(bool success)
        {
            var now = Stopwatch.GetTimestamp();
            _events.Enqueue((now, success));

            if (success)
                Interlocked.Increment(ref _successCount);
            else
                Interlocked.Increment(ref _failureCount);

            PruneOldEvents(now);
        }

        public double GetFailureRate()
        {
            var total = TotalCount;
            return total == 0 ? 0 : (double)_failureCount / total;
        }

        private void PruneOldEvents(long now)
        {
            var cutoff = now - (long)(_windowSize.TotalSeconds * Stopwatch.Frequency);

            while (_events.TryPeek(out var oldest) && oldest.timestamp < cutoff)
            {
                if (_events.TryDequeue(out var removed))
                {
                    if (removed.success)
                        Interlocked.Decrement(ref _successCount);
                    else
                        Interlocked.Decrement(ref _failureCount);
                }
            }
        }

        public void Reset()
        {
            while (_events.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _successCount, 0);
            Interlocked.Exchange(ref _failureCount, 0);
        }
    }

    public interface ICircuitBreakerMetrics
    {
        void RecordSuccess(string circuitBreakerName, TimeSpan duration);
        void RecordFailure(string circuitBreakerName, TimeSpan duration, string exceptionType);
        void RecordRejection(string circuitBreakerName);
        void RecordStateChange(string circuitBreakerName, CircuitState newState);
    }

    internal class NullMetrics : ICircuitBreakerMetrics
    {
        public void RecordSuccess(string circuitBreakerName, TimeSpan duration) { }
        public void RecordFailure(string circuitBreakerName, TimeSpan duration, string exceptionType) { }
        public void RecordRejection(string circuitBreakerName) { }
        public void RecordStateChange(string circuitBreakerName, CircuitState newState) { }
    }
}