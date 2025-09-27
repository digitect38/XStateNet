using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XStateNet.Semi.Transport
{
    /// <summary>
    /// Thread-safe circuit breaker implementation that prevents race conditions
    /// in state transitions and failure counting.
    /// </summary>
    public class ThreadSafeCircuitBreaker
    {
        private readonly ILogger? _logger;
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;
        private readonly TimeSpan _halfOpenTestDelay;

        // Thread-safe state management using Interlocked operations
        private int _state = (int)CircuitState.Closed;
        private long _failureCount = 0;
        private long _successCount = 0;
        private long _lastFailureTimeTicks = 0;
        private long _openedTimeTicks = 0;

        // Lock for complex state transitions only
        private readonly ReaderWriterLockSlim _stateTransitionLock = new(LockRecursionPolicy.NoRecursion);

        public enum CircuitState
        {
            Closed = 0,
            Open = 1,
            HalfOpen = 2
        }

        public event EventHandler<CircuitState>? StateChanged;
        public event EventHandler<(CircuitState oldState, CircuitState newState, string reason)>? StateTransitioned;

        public ThreadSafeCircuitBreaker(
            int failureThreshold,
            TimeSpan openDuration,
            TimeSpan? halfOpenTestDelay = null,
            ILogger? logger = null)
        {
            if (failureThreshold <= 0)
                throw new ArgumentException("Failure threshold must be positive", nameof(failureThreshold));
            if (openDuration <= TimeSpan.Zero)
                throw new ArgumentException("Open duration must be positive", nameof(openDuration));

            _failureThreshold = failureThreshold;
            _openDuration = openDuration;
            _halfOpenTestDelay = halfOpenTestDelay ?? TimeSpan.FromSeconds(1);
            _logger = logger;
        }

        public CircuitState State => (CircuitState)Volatile.Read(ref _state);
        public long FailureCount => Interlocked.Read(ref _failureCount);
        public long SuccessCount => Interlocked.Read(ref _successCount);
        public bool IsOpen => State == CircuitState.Open;
        public bool IsClosed => State == CircuitState.Closed;
        public bool IsHalfOpen => State == CircuitState.HalfOpen;

        /// <summary>
        /// Execute an operation through the circuit breaker
        /// </summary>
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            // Fast path: Check if circuit is open without taking lock
            if (ShouldRejectFast())
            {
                throw new CircuitBreakerOpenException(
                    $"Circuit breaker is open. Will retry after {GetRemainingOpenTime():F1} seconds");
            }

            // Attempt state transition if needed (Open -> HalfOpen after timeout)
            await AttemptStateRecoveryAsync();

            var currentState = State;

            // Check again after potential state change
            if (currentState == CircuitState.Open)
            {
                throw new CircuitBreakerOpenException(
                    $"Circuit breaker is open. Will retry after {GetRemainingOpenTime():F1} seconds");
            }

            try
            {
                var result = await operation(cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
        }

        /// <summary>
        /// Record a successful operation
        /// </summary>
        public void RecordSuccess()
        {
            var newCount = Interlocked.Increment(ref _successCount);
            var currentState = State;

            if (currentState == CircuitState.HalfOpen)
            {
                // After success in half-open, transition to closed
                TransitionToClosed("Successful operation in half-open state");
            }
            else if (currentState == CircuitState.Closed && newCount > 0)
            {
                // Reset failure count on success in closed state
                Interlocked.Exchange(ref _failureCount, 0);
            }

            _logger?.LogTrace("Circuit breaker recorded success. State: {State}, SuccessCount: {Count}",
                State, newCount);
        }

        /// <summary>
        /// Record a failed operation
        /// </summary>
        public void RecordFailure(Exception? exception = null)
        {
            var newCount = Interlocked.Increment(ref _failureCount);
            Interlocked.Exchange(ref _lastFailureTimeTicks, DateTime.UtcNow.Ticks);

            var currentState = State;

            _logger?.LogTrace("Circuit breaker recorded failure #{Count}. State: {State}, Threshold: {Threshold}",
                newCount, currentState, _failureThreshold);

            if (currentState == CircuitState.HalfOpen)
            {
                // Any failure in half-open immediately opens the circuit
                TransitionToOpen("Failure in half-open state", exception);
            }
            else if (currentState == CircuitState.Closed && newCount >= _failureThreshold)
            {
                // Threshold reached in closed state
                TransitionToOpen($"Failure threshold reached: {newCount}/{_failureThreshold}", exception);
            }
        }

        /// <summary>
        /// Reset the circuit breaker to closed state
        /// </summary>
        public void Reset()
        {
            _stateTransitionLock.EnterWriteLock();
            try
            {
                Interlocked.Exchange(ref _failureCount, 0);
                Interlocked.Exchange(ref _successCount, 0);
                Interlocked.Exchange(ref _lastFailureTimeTicks, 0);
                Interlocked.Exchange(ref _openedTimeTicks, 0);

                var oldState = (CircuitState)Interlocked.Exchange(ref _state, (int)CircuitState.Closed);

                if (oldState != CircuitState.Closed)
                {
                    _logger?.LogInformation("Circuit breaker manually reset from {OldState} to Closed", oldState);
                    OnStateChanged(CircuitState.Closed, oldState, "Manual reset");
                }
            }
            finally
            {
                _stateTransitionLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Get statistics about the circuit breaker
        /// </summary>
        public CircuitBreakerStats GetStats()
        {
            return new CircuitBreakerStats
            {
                State = State,
                FailureCount = FailureCount,
                SuccessCount = SuccessCount,
                LastFailureTime = _lastFailureTimeTicks > 0
                    ? new DateTime(_lastFailureTimeTicks, DateTimeKind.Utc)
                    : (DateTime?)null,
                OpenedTime = _openedTimeTicks > 0
                    ? new DateTime(_openedTimeTicks, DateTimeKind.Utc)
                    : (DateTime?)null,
                RemainingOpenTime = GetRemainingOpenTime()
            };
        }

        private bool ShouldRejectFast()
        {
            var currentState = State;
            if (currentState != CircuitState.Open)
                return false;

            var openedTicks = Interlocked.Read(ref _openedTimeTicks);
            if (openedTicks == 0)
                return false;

            var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - openedTicks);
            return elapsed < _openDuration;
        }

        private double GetRemainingOpenTime()
        {
            var openedTicks = Interlocked.Read(ref _openedTimeTicks);
            if (openedTicks == 0 || State != CircuitState.Open)
                return 0;

            var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - openedTicks);
            var remaining = _openDuration - elapsed;
            return remaining.TotalSeconds > 0 ? remaining.TotalSeconds : 0;
        }

        private async Task AttemptStateRecoveryAsync()
        {
            var currentState = State;
            if (currentState != CircuitState.Open)
                return;

            var openedTicks = Interlocked.Read(ref _openedTimeTicks);
            if (openedTicks == 0)
                return;

            var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - openedTicks);
            if (elapsed >= _openDuration)
            {
                // Small delay before transitioning to half-open to prevent thundering herd
                await Task.Delay(_halfOpenTestDelay).ConfigureAwait(false);
                TransitionToHalfOpen("Open duration expired");
            }
        }

        private void TransitionToOpen(string reason, Exception? exception = null)
        {
            _stateTransitionLock.EnterWriteLock();
            try
            {
                var oldState = (CircuitState)Volatile.Read(ref _state);

                // Double-check state under lock
                if (oldState == CircuitState.Open)
                    return;

                // For closed -> open, verify threshold is still exceeded
                if (oldState == CircuitState.Closed)
                {
                    var currentFailures = Interlocked.Read(ref _failureCount);
                    if (currentFailures < _failureThreshold)
                        return; // Another thread may have reset the count
                }

                // Perform state transition
                Interlocked.Exchange(ref _openedTimeTicks, DateTime.UtcNow.Ticks);
                Interlocked.Exchange(ref _state, (int)CircuitState.Open);

                _logger?.LogWarning(exception,
                    "Circuit breaker opened from {OldState}. Reason: {Reason}. Will remain open for {Duration:F1}s",
                    oldState, reason, _openDuration.TotalSeconds);

                OnStateChanged(CircuitState.Open, oldState, reason);
            }
            finally
            {
                _stateTransitionLock.ExitWriteLock();
            }
        }

        private void TransitionToHalfOpen(string reason)
        {
            _stateTransitionLock.EnterWriteLock();
            try
            {
                var oldState = (CircuitState)Volatile.Read(ref _state);

                // Only transition from Open to HalfOpen
                if (oldState != CircuitState.Open)
                    return;

                // Verify timeout has actually expired
                var openedTicks = Interlocked.Read(ref _openedTimeTicks);
                if (openedTicks > 0)
                {
                    var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - openedTicks);
                    if (elapsed < _openDuration)
                        return; // Not ready yet
                }

                // Reset counters when entering half-open
                Interlocked.Exchange(ref _failureCount, 0);
                Interlocked.Exchange(ref _successCount, 0);
                Interlocked.Exchange(ref _state, (int)CircuitState.HalfOpen);

                _logger?.LogInformation(
                    "Circuit breaker transitioned to half-open from {OldState}. Reason: {Reason}",
                    oldState, reason);

                OnStateChanged(CircuitState.HalfOpen, oldState, reason);
            }
            finally
            {
                _stateTransitionLock.ExitWriteLock();
            }
        }

        private void TransitionToClosed(string reason)
        {
            _stateTransitionLock.EnterWriteLock();
            try
            {
                var oldState = (CircuitState)Volatile.Read(ref _state);

                // Can transition to closed from half-open or on reset
                if (oldState == CircuitState.Closed)
                    return;

                // Reset all counters
                Interlocked.Exchange(ref _failureCount, 0);
                Interlocked.Exchange(ref _successCount, 0);
                Interlocked.Exchange(ref _openedTimeTicks, 0);
                Interlocked.Exchange(ref _state, (int)CircuitState.Closed);

                _logger?.LogInformation(
                    "Circuit breaker closed from {OldState}. Reason: {Reason}",
                    oldState, reason);

                OnStateChanged(CircuitState.Closed, oldState, reason);
            }
            finally
            {
                _stateTransitionLock.ExitWriteLock();
            }
        }

        private void OnStateChanged(CircuitState newState, CircuitState oldState, string reason)
        {
            try
            {
                StateChanged?.Invoke(this, newState);
                StateTransitioned?.Invoke(this, (oldState, newState, reason));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in circuit breaker state change event handler");
            }
        }

        public void Dispose()
        {
            _stateTransitionLock?.Dispose();
        }
    }

    public class CircuitBreakerStats
    {
        public ThreadSafeCircuitBreaker.CircuitState State { get; set; }
        public long FailureCount { get; set; }
        public long SuccessCount { get; set; }
        public DateTime? LastFailureTime { get; set; }
        public DateTime? OpenedTime { get; set; }
        public double RemainingOpenTime { get; set; }
    }

    public class CircuitBreakerOpenException : InvalidOperationException
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
        public CircuitBreakerOpenException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}