using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using XStateNet;
using Newtonsoft.Json;

namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// Circuit Breaker implementation using XStateNet state machine
    /// This demonstrates how Circuit Breaker pattern can be elegantly implemented using state machines
    /// </summary>
    public class XStateNetCircuitBreaker : ICircuitBreaker, IDisposable
    {
        private readonly string _name;
        private readonly CircuitBreakerOptions _options;
        private readonly ICircuitBreakerMetrics _metrics;
        private readonly StateMachine _stateMachine;
        private readonly SlidingWindow _failureWindow;

        // State tracking with lock-free atomics
        private long _consecutiveFailures;
        private long _successCountInHalfOpen;
        private Exception? _lastException;

        // Use Interlocked for atomic state management
        private int _currentStateInt = (int)CircuitState.Closed;
        private int _previousStateInt = (int)CircuitState.Closed;

        public string Name => _name;
        public CircuitState State => (CircuitState)Interlocked.CompareExchange(ref _currentStateInt, 0, 0);
        public event EventHandler<CircuitStateChangedEventArgs>? StateChanged;

        public XStateNetCircuitBreaker(string name, CircuitBreakerOptions options, ICircuitBreakerMetrics? metrics = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics ?? new NullMetrics();
            _failureWindow = new SlidingWindow(options.SamplingDuration);

            Console.WriteLine($"[INIT] Creating circuit breaker '{_name}' with threshold={_options.FailureThreshold}");

            // Create state machine configuration
            _stateMachine = CreateStateMachine();
            _stateMachine.Start();
        }

        private StateMachine CreateStateMachine()
        {
            // Create action map
            var actionMap = new ActionMap();

            actionMap["incrementFailures"] = new List<NamedAction>
            {
                new NamedAction("incrementFailures", (sm) =>
                {
                    Interlocked.Increment(ref _consecutiveFailures);
                    Console.WriteLine($"[ACTION] incrementFailures: count now = {_consecutiveFailures}");
                })
            };

            actionMap["resetFailures"] = new List<NamedAction>
            {
                new NamedAction("resetFailures", (sm) =>
                {
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    Debug.WriteLine($"Circuit Breaker '{_name}' resetFailures: count now = 0");
                })
            };

            actionMap["incrementSuccess"] = new List<NamedAction>
            {
                new NamedAction("incrementSuccess", (sm) =>
                {
                    Interlocked.Increment(ref _successCountInHalfOpen);
                    Debug.WriteLine($"Circuit Breaker '{_name}' incrementSuccess: count now = {_successCountInHalfOpen}");
                })
            };

            actionMap["resetSuccessCount"] = new List<NamedAction>
            {
                new NamedAction("resetSuccessCount", (sm) =>
                {
                    Interlocked.Exchange(ref _successCountInHalfOpen, 0);
                })
            };

            actionMap["resetCounters"] = new List<NamedAction>
            {
                new NamedAction("resetCounters", (sm) =>
                {
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    Interlocked.Exchange(ref _successCountInHalfOpen, 0);
                    _failureWindow.Reset();
                })
            };

            actionMap["notifyOpen"] = new List<NamedAction>
            {
                new NamedAction("notifyOpen", (sm) =>
                {
                    Console.WriteLine($"[ACTION] notifyOpen called");
                    SetCurrentState(CircuitState.Open);
                    StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                    {
                        CircuitBreakerName = _name,
                        FromState = (CircuitState)_previousStateInt,
                        ToState = CircuitState.Open,
                        LastException = _lastException
                    });
                    _metrics.RecordStateChange(_name, CircuitState.Open);
                })
            };

            actionMap["notifyClosed"] = new List<NamedAction>
            {
                new NamedAction("notifyClosed", (sm) =>
                {
                    SetCurrentState(CircuitState.Closed);
                    StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                    {
                        CircuitBreakerName = _name,
                        FromState = (CircuitState)_previousStateInt,
                        ToState = CircuitState.Closed
                    });
                    _metrics.RecordStateChange(_name, CircuitState.Closed);
                })
            };

            actionMap["notifyOpenState"] = new List<NamedAction>
            {
                new NamedAction("notifyOpenState", (sm) =>
                {
                    Debug.WriteLine($"Circuit Breaker '{_name}' entered OPEN state, will transition to HalfOpen after {_options.BreakDuration}");
                })
            };

            actionMap["notifyHalfOpenTransition"] = new List<NamedAction>
            {
                new NamedAction("notifyHalfOpenTransition", (sm) =>
                {
                    SetCurrentState(CircuitState.HalfOpen);
                    StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                    {
                        CircuitBreakerName = _name,
                        FromState = (CircuitState)_previousStateInt,
                        ToState = CircuitState.HalfOpen
                    });
                    _metrics.RecordStateChange(_name, CircuitState.HalfOpen);
                })
            };

            actionMap["notifyHalfOpenState"] = new List<NamedAction>
            {
                new NamedAction("notifyHalfOpenState", (sm) =>
                {
                    Debug.WriteLine($"Circuit Breaker '{_name}' entered HALF-OPEN state");
                })
            };

            actionMap["rejectExecution"] = new List<NamedAction>
            {
                new NamedAction("rejectExecution", (sm) =>
                {
                    _metrics.RecordRejection(_name);
                })
            };

            // Create guard map
            var guardMap = new GuardMap();

            guardMap["thresholdExceeded"] = new NamedGuard("thresholdExceeded", (sm) =>
            {
                // Check if threshold will be exceeded AFTER this failure
                // Since incrementFailures will run as part of the action, we add 1
                var afterIncrement = _consecutiveFailures + 1;
                Console.WriteLine($"[GUARD] thresholdExceeded: current={_consecutiveFailures}, afterIncrement={afterIncrement}, threshold={_options.FailureThreshold}, result={afterIncrement >= _options.FailureThreshold}");
                if (afterIncrement >= _options.FailureThreshold)
                    return true;

                // Check failure rate threshold
                var failureRate = _failureWindow.GetFailureRate();
                if (failureRate > _options.FailureRateThreshold &&
                    _failureWindow.TotalCount >= _options.MinimumThroughput)
                {
                    return true;
                }

                return false;
            });

            guardMap["successThresholdMet"] = new NamedGuard("successThresholdMet", (sm) =>
            {
                // Check if we have enough successes to close the circuit
                var nextCount = _successCountInHalfOpen + 1;
                Debug.WriteLine($"Circuit Breaker '{_name}' checking success threshold: next={nextCount} >= {_options.SuccessCountInHalfOpen}");
                return nextCount >= _options.SuccessCountInHalfOpen;
            });

            // Use the CircuitBreakerStateMachineBuilder to create the state machine
            return CircuitBreakerStateMachineBuilder.Build(_name, _options, actionMap, guardMap);
        }

        private CircuitState GetCurrentState()
        {
            return (CircuitState)Interlocked.CompareExchange(ref _currentStateInt, 0, 0);
        }


        private void SetCurrentState(CircuitState newState)
        {
            int newStateInt = (int)newState;
            int currentStateInt;
            int previousStateInt;

            do
            {
                currentStateInt = _currentStateInt;
                if (currentStateInt == newStateInt)
                    return; // Already in the desired state

                previousStateInt = currentStateInt;
            }
            while (Interlocked.CompareExchange(ref _currentStateInt, newStateInt, currentStateInt) != currentStateInt);

            // Update previous state after successful transition
            Interlocked.Exchange(ref _previousStateInt, previousStateInt);
        }


        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(_ => operation(), cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            // Check if circuit is open
            var currentState = GetCurrentState();
            if (currentState == CircuitState.Open)
            {
                _stateMachine.Send("EXECUTE"); // For metrics
                throw new CircuitBreakerOpenException($"Circuit breaker '{_name}' is open");
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

        public Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(() => Task.FromResult(operation()), cancellationToken);
        }

        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async ct =>
            {
                await operation().ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        private void OnSuccess()
        {
            _failureWindow.RecordSuccess();

            var currentState = GetCurrentState();
            Debug.WriteLine($"Circuit Breaker '{_name}' OnSuccess called in state: {currentState}");

            // Let the state machine handle resetting via the 'resetFailures' action

            Debug.WriteLine($"Circuit Breaker '{_name}' sending SUCCESS event, current success count: {_successCountInHalfOpen}");
            _stateMachine.Send("SUCCESS");
            Debug.WriteLine($"Circuit Breaker '{_name}' after SUCCESS event, state: {GetCurrentState()}, success count: {_successCountInHalfOpen}");
        }

        private void OnFailure(Exception exception)
        {
            _lastException = exception;
            _failureWindow.RecordFailure();
            // Don't increment here - let the state machine handle it via actions

            Console.WriteLine($"[FAIL] Sending FAIL event, consecutiveFailures={_consecutiveFailures}");
            _stateMachine.Send("FAIL");
            Console.WriteLine($"[FAIL] After FAIL event, consecutiveFailures={_consecutiveFailures}, state={GetCurrentState()}");
        }

        public void Dispose()
        {
            _stateMachine?.Stop();
        }
    }
}