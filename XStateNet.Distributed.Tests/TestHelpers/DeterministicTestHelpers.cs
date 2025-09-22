using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace XStateNet.Distributed.Tests.TestHelpers
{
    /// <summary>
    /// Provides deterministic testing utilities to replace timing-based synchronization
    /// </summary>
    public static class DeterministicTestHelpers
    {
        /// <summary>
        /// Waits for a condition to become true with proper synchronization
        /// </summary>
        public static async Task WaitForConditionAsync(
            Func<bool> condition,
            TimeSpan timeout,
            string timeoutMessage = "Condition was not met within timeout")
        {
            var tcs = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource(timeout);

            cts.Token.Register(() =>
                tcs.TrySetException(new TimeoutException(timeoutMessage)));

            // Poll condition with increasing intervals
            _ = Task.Run(async () =>
            {
                var delay = 1;
                while (!cts.Token.IsCancellationRequested)
                {
                    if (condition())
                    {
                        tcs.TrySetResult(true);
                        return;
                    }
                    await Task.Delay(delay, cts.Token);
                    delay = Math.Min(delay * 2, 100); // Exponential backoff up to 100ms
                }
            });

            await tcs.Task;
        }

        /// <summary>
        /// Creates a synchronization point that waits for N signals
        /// </summary>
        public class SyncPoint
        {
            private readonly CountdownEvent _countdown;
            private readonly TaskCompletionSource<bool> _completion = new();

            public SyncPoint(int count)
            {
                _countdown = new CountdownEvent(count);
            }

            public void Signal()
            {
                if (_countdown.Signal())
                {
                    _completion.TrySetResult(true);
                }
            }

            public Task WaitAsync(TimeSpan timeout)
            {
                using var cts = new CancellationTokenSource(timeout);
                cts.Token.Register(() =>
                    _completion.TrySetException(new TimeoutException($"SyncPoint not reached within {timeout}")));
                return _completion.Task;
            }

            public Task WaitAsync() => WaitAsync(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Event collector that captures events for verification
        /// </summary>
        public class EventCollector<T>
        {
            private readonly List<T> _events = new();
            private readonly SemaphoreSlim _semaphore = new(0);
            private readonly object _lock = new();

            public void Add(T item)
            {
                lock (_lock)
                {
                    _events.Add(item);
                }
                _semaphore.Release();
            }

            public async Task<List<T>> WaitForCountAsync(int count, TimeSpan timeout)
            {
                using var cts = new CancellationTokenSource(timeout);

                // Wait for required count
                for (int i = 0; i < count; i++)
                {
                    await _semaphore.WaitAsync(cts.Token);
                }

                lock (_lock)
                {
                    return new List<T>(_events);
                }
            }

            public List<T> GetAll()
            {
                lock (_lock)
                {
                    return new List<T>(_events);
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _events.Clear();
                }
            }
        }

        /// <summary>
        /// Async operation coordinator for sequencing operations
        /// </summary>
        public class OperationCoordinator
        {
            private readonly Dictionary<string, TaskCompletionSource<bool>> _checkpoints = new();

            public async Task WaitForCheckpointAsync(string name, TimeSpan timeout)
            {
                TaskCompletionSource<bool> tcs;
                lock (_checkpoints)
                {
                    if (!_checkpoints.TryGetValue(name, out tcs))
                    {
                        tcs = new TaskCompletionSource<bool>();
                        _checkpoints[name] = tcs;
                    }
                }

                using var cts = new CancellationTokenSource(timeout);
                cts.Token.Register(() =>
                    tcs.TrySetException(new TimeoutException($"Checkpoint '{name}' not reached within {timeout}")));

                await tcs.Task;
            }

            public void SignalCheckpoint(string name)
            {
                lock (_checkpoints)
                {
                    if (_checkpoints.TryGetValue(name, out var tcs))
                    {
                        tcs.TrySetResult(true);
                    }
                    else
                    {
                        _checkpoints[name] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _checkpoints[name].TrySetResult(true);
                    }
                }
            }
        }

        /// <summary>
        /// State change monitor for tracking state transitions
        /// </summary>
        public class StateMonitor<TState> where TState : Enum
        {
            private readonly TaskCompletionSource<TState> _stateReached = new();
            private TState _targetState;
            private TState _currentState;

            public StateMonitor(TState initialState)
            {
                _currentState = initialState;
            }

            public void UpdateState(TState newState)
            {
                _currentState = newState;
                if (newState.Equals(_targetState))
                {
                    _stateReached.TrySetResult(newState);
                }
            }

            public async Task<TState> WaitForStateAsync(TState targetState, TimeSpan timeout)
            {
                _targetState = targetState;

                // Check if already in target state
                if (_currentState.Equals(targetState))
                {
                    return targetState;
                }

                using var cts = new CancellationTokenSource(timeout);
                cts.Token.Register(() =>
                    _stateReached.TrySetException(new TimeoutException($"State '{targetState}' not reached within {timeout}")));

                return await _stateReached.Task;
            }
        }

        /// <summary>
        /// Provides controlled time for testing time-dependent code
        /// </summary>
        public interface ITestTimeProvider
        {
            DateTime UtcNow { get; }
            Task Delay(TimeSpan delay, CancellationToken cancellationToken = default);
            void Advance(TimeSpan amount);
        }

        public class TestTimeProvider : ITestTimeProvider
        {
            private DateTime _currentTime = DateTime.UtcNow;
            private readonly List<(TaskCompletionSource<bool> tcs, DateTime targetTime)> _delays = new();

            public DateTime UtcNow => _currentTime;

            public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
            {
                var targetTime = _currentTime.Add(delay);
                var tcs = new TaskCompletionSource<bool>();

                cancellationToken.Register(() => tcs.TrySetCanceled());

                lock (_delays)
                {
                    _delays.Add((tcs, targetTime));
                }

                return tcs.Task;
            }

            public void Advance(TimeSpan amount)
            {
                _currentTime = _currentTime.Add(amount);

                lock (_delays)
                {
                    var completed = _delays.RemoveAll(d =>
                    {
                        if (d.targetTime <= _currentTime)
                        {
                            d.tcs.TrySetResult(true);
                            return true;
                        }
                        return false;
                    });
                }
            }
        }
    }
}