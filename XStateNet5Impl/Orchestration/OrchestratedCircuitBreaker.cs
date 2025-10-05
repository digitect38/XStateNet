using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Orchestration
{
    /// <summary>
    /// Circuit breaker implementation using PureStateMachine with EventBusOrchestrator
    /// Provides thread-safe state transitions without manual locking via orchestrator pattern
    /// </summary>
    public class OrchestratedCircuitBreaker : IDisposable
    {
        private readonly string _name;
        private readonly EventBusOrchestrator _orchestrator;
        private readonly IPureStateMachine _machine;
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;
        private readonly SemaphoreSlim _halfOpenSemaphore = new(1, 1);

        private long _failureCount = 0;
        private long _successCount = 0;
        private bool _disposed;

        public string MachineId => _machine.Id;
        public string CurrentState => _machine.CurrentState;
        public long FailureCount => Interlocked.Read(ref _failureCount);
        public long SuccessCount => Interlocked.Read(ref _successCount);

        public event EventHandler<(string oldState, string newState, string reason)>? StateTransitioned;

        public OrchestratedCircuitBreaker(
            string name,
            EventBusOrchestrator orchestrator,
            int failureThreshold = 5,
            TimeSpan? openDuration = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Circuit breaker name is required", nameof(name));
            if (failureThreshold <= 0)
                throw new ArgumentException("Failure threshold must be positive", nameof(failureThreshold));

            _name = name;
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _failureThreshold = failureThreshold;
            _openDuration = openDuration ?? TimeSpan.FromSeconds(60);

            _machine = CreateCircuitBreakerMachine();
        }

        private IPureStateMachine CreateCircuitBreakerMachine()
        {
            var baseMachineId = $"CircuitBreaker_{_name}";
            var definition = $$"""
            {
                'id': '{{baseMachineId}}',
                'initial': 'closed',
                'context': {
                    'failureCount': 0,
                    'successCount': 0,
                    'failureThreshold': {{_failureThreshold}},
                    'lastOpenTime': null
                },
                'states': {
                    'closed': {
                        'on': {
                            'RECORD_SUCCESS': {
                                'actions': ['incrementSuccess']
                            },
                            'RECORD_FAILURE': [
                                {
                                    'target': 'open',
                                    'cond': 'failureThresholdReached',
                                    'actions': ['incrementFailure', 'notifyOpened', 'recordOpenTime']
                                },
                                {
                                    'actions': ['incrementFailure']
                                }
                            ]
                        }
                    },
                    'open': {
                        'on': {
                            'TIMEOUT_EXPIRED': {
                                'target': 'halfOpen',
                                'actions': ['notifyHalfOpen']
                            }
                        }
                    },
                    'halfOpen': {
                        'on': {
                            'TEST_SUCCESS': {
                                'target': 'closed',
                                'actions': ['resetCounters', 'notifyClosed']
                            },
                            'TEST_FAILURE': {
                                'target': 'open',
                                'actions': ['incrementFailure', 'notifyReOpened', 'recordOpenTime']
                            }
                        }
                    }
                }
            }
            """;

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["incrementFailure"] = (ctx) =>
                {
                    Interlocked.Increment(ref _failureCount);
                },
                ["incrementSuccess"] = (ctx) =>
                {
                    Interlocked.Increment(ref _successCount);
                },
                ["resetCounters"] = (ctx) =>
                {
                    Interlocked.Exchange(ref _failureCount, 0);
                },
                ["notifyOpened"] = (ctx) =>
                {
                    OnStateTransitioned("closed", "open", "Failure threshold reached");
                },
                ["notifyHalfOpen"] = (ctx) =>
                {
                    OnStateTransitioned("open", "halfOpen", "Timeout expired");
                },
                ["notifyClosed"] = (ctx) =>
                {
                    OnStateTransitioned("halfOpen", "closed", "Test success");
                },
                ["notifyReOpened"] = (ctx) =>
                {
                    OnStateTransitioned("halfOpen", "open", "Test failure");
                },
                ["recordOpenTime"] = (ctx) =>
                {
                    // Schedule timeout expiration event
                    _ = Task.Delay(_openDuration).ContinueWith(async _ =>
                    {
                        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "TIMEOUT_EXPIRED", null);
                    });
                }
            };

            var guards = new Dictionary<string, Func<StateMachine, bool>>
            {
                ["failureThresholdReached"] = (sm) =>
                {
                    var count = Interlocked.Read(ref _failureCount) + 1; // +1 because guard runs before action
                    return count >= _failureThreshold;
                }
            };

            return ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: baseMachineId,
                json: definition,
                orchestrator: _orchestrator,
                orchestratedActions: actions,
                guards: guards
            );
        }

        public async Task<string> StartAsync()
        {
            return await _machine.StartAsync();
        }

        /// <summary>
        /// Execute an operation through the circuit breaker
        /// </summary>
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            // Check if circuit is open (read current state, don't cache)
            if (CurrentState.Contains("open") && !CurrentState.Contains("halfOpen"))
            {
                throw new CircuitBreakerOpenException($"Circuit breaker '{_name}' is open");
            }

            // Half-open: Only allow one test at a time (re-read state)
            if (CurrentState.Contains("halfOpen"))
            {
                var acquired = await _halfOpenSemaphore.WaitAsync(0, cancellationToken);
                if (!acquired)
                {
                    throw new CircuitBreakerOpenException($"Circuit breaker '{_name}' is half-open and test in progress");
                }

                try
                {
                    var result = await operation(cancellationToken);
                    await _orchestrator.SendEventAsync("SYSTEM", MachineId, "TEST_SUCCESS", null);
                    return result;
                }
                catch (Exception)
                {
                    await _orchestrator.SendEventAsync("SYSTEM", MachineId, "TEST_FAILURE", null);
                    throw;
                }
                finally
                {
                    _halfOpenSemaphore.Release();
                }
            }

            // Normal execution in closed state
            try
            {
                var result = await operation(cancellationToken);
                await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RECORD_SUCCESS", null);
                return result;
            }
            catch (Exception)
            {
                await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RECORD_FAILURE", null);
                throw;
            }
        }

        /// <summary>
        /// Manually record a success (for when not using ExecuteAsync)
        /// </summary>
        public async Task RecordSuccessAsync()
        {
            await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RECORD_SUCCESS", null);
        }

        /// <summary>
        /// Manually record a failure (for when not using ExecuteAsync)
        /// </summary>
        public async Task RecordFailureAsync()
        {
            await _orchestrator.SendEventAsync("SYSTEM", MachineId, "RECORD_FAILURE", null);
        }

        /// <summary>
        /// Get circuit breaker statistics
        /// </summary>
        public CircuitBreakerStats GetStats()
        {
            return new CircuitBreakerStats
            {
                State = CurrentState,
                FailureCount = FailureCount,
                SuccessCount = SuccessCount,
                FailureThreshold = _failureThreshold,
                OpenDuration = _openDuration
            };
        }

        private void OnStateTransitioned(string oldState, string newState, string reason)
        {
            StateTransitioned?.Invoke(this, (oldState, newState, reason));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _halfOpenSemaphore?.Dispose();
            _disposed = true;
        }
    }

    public class CircuitBreakerStats
    {
        public string State { get; set; } = "unknown";
        public long FailureCount { get; set; }
        public long SuccessCount { get; set; }
        public int FailureThreshold { get; set; }
        public TimeSpan OpenDuration { get; set; }
    }

    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }
}
