using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Channels;

namespace XStateNet.Distributed.StateMachines
{
    /// <summary>
    /// XStateNet-based StateMachine wrapper with comprehensive timeout protection using hierarchical states
    /// </summary>
    public sealed class XStateNetTimeoutProtectedStateMachine : StateMachine, IDisposable
    {
        private readonly IStateMachine _innerMachine;
        private readonly StateMachine _protectionMachine;
        private readonly IDeadLetterQueue? _dlq;
        private readonly ILogger<XStateNetTimeoutProtectedStateMachine>? _logger;
        private readonly TimeoutProtectedStateMachineOptions _options;

        // Timeout configuration per state/transition
        private readonly ConcurrentDictionary<string, TimeSpan> _stateTimeouts;
        private readonly ConcurrentDictionary<string, TimeSpan> _transitionTimeouts;
        private readonly ConcurrentDictionary<string, TimeSpan> _actionTimeouts;

        // Active timeout tracking
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTimeouts;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingOperations;

        // Statistics
        private long _totalTransitions;
        private long _totalTimeouts;
        private long _totalRecoveries;

        public string Id => _innerMachine.machineId;
        public string machineId => _innerMachine.machineId;
        public bool IsRunning => _innerMachine.IsRunning;
        public ConcurrentDictionary<string, object?>? ContextMap
        {
            get => _innerMachine.ContextMap;
            set => _innerMachine.ContextMap = value;
        }
        public CompoundState? RootState => _innerMachine.RootState;
        public ServiceInvoker? ServiceInvoker
        {
            get => _innerMachine.ServiceInvoker;
            set => _innerMachine.ServiceInvoker = value;
        }

        public event Action<string>? StateChanged;
        public event Action<Exception>? ErrorOccurred;
        
        public XStateNetTimeoutProtectedStateMachine(
            IStateMachine innerMachine,
            IDeadLetterQueue? dlq = null,
            TimeoutProtectedStateMachineOptions? options = null,
            ILogger<XStateNetTimeoutProtectedStateMachine>? logger = null)
        {
            _innerMachine = innerMachine ?? throw new ArgumentNullException(nameof(innerMachine));
            _options = options ?? new TimeoutProtectedStateMachineOptions();
            _dlq = dlq;
            _logger = logger;

            _stateTimeouts = new ConcurrentDictionary<string, TimeSpan>();
            _transitionTimeouts = new ConcurrentDictionary<string, TimeSpan>();
            _actionTimeouts = new ConcurrentDictionary<string, TimeSpan>();
            _activeTimeouts = new ConcurrentDictionary<string, CancellationTokenSource>();
            _pendingOperations = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();

            // Create protection state machine
            _protectionMachine = CreateProtectionStateMachine();
            _protectionMachine.Start();

            // Subscribe to inner machine events
            _innerMachine.StateChanged += OnInnerStateChanged;
        }

        private StateMachine CreateProtectionStateMachine()
        {
            var config = @"{
                'id': 'TimeoutProtection_" + _innerMachine.machineId + @"',
                'type': 'parallel',
                'states': {
                    'monitoring': {
                        'initial': 'idle',
                        'states': {
                            'idle': {
                                'on': {
                                    'START_MONITORING': 'active'
                                }
                            },
                            'active': {
                                'type': 'parallel',
                                'states': {
                                    'stateTimeout': {
                                        'initial': 'waiting',
                                        'states': {
                                            'waiting': {
                                                'on': {
                                                    'STATE_CHANGED': {
                                                        'target': 'timing',
                                                        'actions': 'startStateTimeout'
                                                    }
                                                }
                                            },
                                            'timing': {
                                                'on': {
                                                    'STATE_TIMEOUT': {
                                                        'target': 'timedOut',
                                                        'actions': 'handleStateTimeout'
                                                    },
                                                    'STATE_CHANGED': {
                                                        'target': 'timing',
                                                        'actions': ['cancelStateTimeout', 'startStateTimeout']
                                                    },
                                                    'CANCEL_TIMEOUT': 'waiting'
                                                }
                                            },
                                            'timedOut': {
                                                'entry': 'notifyStateTimeout',
                                                'on': {
                                                    'RECOVER': 'waiting',
                                                    'STATE_CHANGED': 'waiting'
                                                }
                                            }
                                        }
                                    },
                                    'transitionTimeout': {
                                        'initial': 'ready',
                                        'states': {
                                            'ready': {
                                                'on': {
                                                    'TRANSITION_START': {
                                                        'target': 'inProgress',
                                                        'actions': 'startTransitionTimeout'
                                                    }
                                                }
                                            },
                                            'inProgress': {
                                                'on': {
                                                    'TRANSITION_COMPLETE': {
                                                        'target': 'ready',
                                                        'actions': 'cancelTransitionTimeout'
                                                    },
                                                    'TRANSITION_TIMEOUT': {
                                                        'target': 'timedOut',
                                                        'actions': 'handleTransitionTimeout'
                                                    }
                                                }
                                            },
                                            'timedOut': {
                                                'entry': 'notifyTransitionTimeout',
                                                'on': {
                                                    'RECOVER': {
                                                        'target': 'recovering',
                                                        'actions': 'attemptRecovery'
                                                    },
                                                    'ABANDON': 'ready'
                                                }
                                            },
                                            'recovering': {
                                                'on': {
                                                    'RECOVERY_SUCCESS': 'ready',
                                                    'RECOVERY_FAILED': 'failed'
                                                }
                                            },
                                            'failed': {
                                                'type': 'final',
                                                'entry': 'logFailure'
                                            }
                                        }
                                    },
                                    'actionTimeout': {
                                        'initial': 'idle',
                                        'states': {
                                            'idle': {
                                                'on': {
                                                    'ACTION_START': {
                                                        'target': 'executing',
                                                        'actions': 'startActionTimeout'
                                                    }
                                                }
                                            },
                                            'executing': {
                                                'on': {
                                                    'ACTION_COMPLETE': {
                                                        'target': 'idle',
                                                        'actions': 'cancelActionTimeout'
                                                    },
                                                    'ACTION_TIMEOUT': {
                                                        'target': 'timedOut',
                                                        'actions': 'handleActionTimeout'
                                                    }
                                                }
                                            },
                                            'timedOut': {
                                                'entry': 'notifyActionTimeout',
                                                'after': {
                                                    '1000': 'idle'
                                                }
                                            }
                                        }
                                    }
                                },
                                'on': {
                                    'STOP_MONITORING': 'idle'
                                }
                            }
                        }
                    },
                    'execution': {
                        'initial': 'ready',
                        'states': {
                            'ready': {
                                'on': {
                                    'EXECUTE': 'processing'
                                }
                            },
                            'processing': {
                                'entry': 'executeOperation',
                                'on': {
                                    'SUCCESS': 'ready',
                                    'FAILURE': 'error',
                                    'TIMEOUT': 'timeout'
                                }
                            },
                            'error': {
                                'entry': 'handleError',
                                'on': {
                                    'RETRY': 'processing',
                                    'RESET': 'ready'
                                }
                            },
                            'timeout': {
                                'entry': 'handleTimeout',
                                'on': {
                                    'RETRY': 'processing',
                                    'RESET': 'ready'
                                }
                            }
                        }
                    }
                }
            }";

            var actionMap = new ActionMap();

            // State timeout actions
            actionMap["startStateTimeout"] = new List<NamedAction>
            {
                new NamedAction("startStateTimeout",  (sm) =>
                {
                    var state = sm.ContextMap["currentState"]?.ToString() ?? "";
                    if (_stateTimeouts.TryGetValue(state, out var timeout))
                    {
                        StartTimeout($"state_{state}", timeout, () =>
                        {
                            sm.Send("STATE_TIMEOUT", new { State = state });
                        });
                    }
                })
            };

            actionMap["cancelStateTimeout"] = new List<NamedAction>
            {
                new NamedAction("cancelStateTimeout",  (sm) =>
                {
                    var state = sm.ContextMap["currentState"]?.ToString() ?? "";
                    CancelTimeout($"state_{state}");
                })
            };

            actionMap["handleStateTimeout"] = new List<NamedAction>
            {
                new NamedAction("handleStateTimeout", async (sm) =>
                {
                    var state = sm.ContextMap["currentState"]?.ToString() ?? "";
                    Interlocked.Increment(ref _totalTimeouts);
                    
                    _logger?.LogWarning("State '{State}' timed out", state);
                    
                    if (_dlq != null && _options.SendStateTimeoutsToDLQ)
                    {
                        await _dlq.EnqueueAsync(
                            new StateTimeoutEvent
                            {
                                MachineId = Id,
                                State = state,
                                TimeoutDuration = _stateTimeouts.GetValueOrDefault(state)
                            },
                            source: Id,
                            reason: "State timeout");
                    }
                })
            };

            // Transition timeout actions
            actionMap["startTransitionTimeout"] = new List<NamedAction>
            {
                new NamedAction("startTransitionTimeout",  (sm) =>
                {
                    var transition = sm.ContextMap["currentTransition"]?.ToString() ?? "";
                    if (_transitionTimeouts.TryGetValue(transition, out var timeout))
                    {
                        StartTimeout($"transition_{transition}", timeout, () =>
                        {
                            sm.Send("TRANSITION_TIMEOUT", new { Transition = transition });
                        });
                    }
                })
            };

            actionMap["cancelTransitionTimeout"] = new List<NamedAction>
            {
                new NamedAction("cancelTransitionTimeout", (sm) =>
                {
                    var transition = sm.ContextMap["currentTransition"]?.ToString() ?? "";
                    CancelTimeout($"transition_{transition}");
                })
            };

            actionMap["handleTransitionTimeout"] = new List<NamedAction>
            {
                new NamedAction("handleTransitionTimeout", async (sm) =>
                {
                    var transition = sm.ContextMap["currentTransition"]?.ToString() ?? "";
                    Interlocked.Increment(ref _totalTimeouts);

                    _logger?.LogWarning("Transition '{Transition}' timed out", transition);

                    if (_dlq != null && _options.SendTimeoutEventsToDLQ)
                    {
                        var parts = transition.Split("->" );
                        await _dlq.EnqueueAsync(
                            new TimeoutEvent
                            {
                                MachineId = Id,
                                EventName = parts.Length > 1 ? parts[1] : transition,
                                FromState = parts.Length > 0 ? parts[0] : "",
                                TimeoutDuration = _transitionTimeouts.GetValueOrDefault(transition)
                            },
                            source: Id,
                            reason: "Transition timeout");
                    }
                })
            };

            // Action timeout actions
            actionMap["startActionTimeout"] = new List<NamedAction>
            {
                new NamedAction("startActionTimeout",  (sm) =>
                {
                    var action = sm.ContextMap["currentAction"]?.ToString() ?? "";
                    if (_actionTimeouts.TryGetValue(action, out var timeout))
                    {
                        StartTimeout($"action_{action}", timeout, () =>
                        {
                            sm.Send("ACTION_TIMEOUT", new { Action = action });
                        });
                    }
                })
            };

            actionMap["cancelActionTimeout"] = new List<NamedAction>
            {
                new NamedAction("cancelActionTimeout",  (sm) =>
                {
                    var action = sm.ContextMap["currentAction"]?.ToString() ?? "";
                    CancelTimeout($"action_{action}");
                })
            };

            actionMap["handleActionTimeout"] = new List<NamedAction>
            {
                new NamedAction("handleActionTimeout", async (sm) =>
                {
                    var action = sm.ContextMap["currentAction"]?.ToString() ?? "";
                    Interlocked.Increment(ref _totalTimeouts);

                    _logger?.LogWarning("Action '{Action}' timed out", action);

                    if (_dlq != null)
                    {
                        await _dlq.EnqueueAsync(
                            new ActionTimeoutEvent
                            {
                                MachineId = Id,
                                ActionName = action,
                                State = GetActiveStateString(),
                                TimeoutDuration = _actionTimeouts.GetValueOrDefault(action)
                            },
                            source: Id,
                            reason: "Action timeout");
                    }
                })
            };

            // Recovery actions
            actionMap["attemptRecovery"] = new List<NamedAction>
            {
                new NamedAction("attemptRecovery", async (sm) =>
                {
                    Interlocked.Increment(ref _totalRecoveries);

                    var eventName = sm.ContextMap["pendingEvent"]?.ToString() ?? "";
                    var payload = sm.ContextMap["pendingPayload"];

                    _logger?.LogInformation("Attempting recovery for event '{EventName}'", eventName);

                    try
                    {
                        await _innerMachine.SendAsync(eventName, payload);
                        sm.Send("RECOVERY_SUCCESS");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Recovery failed for event '{EventName}'", eventName);
                        sm.Send("RECOVERY_FAILED");
                    }
                })
            };

            // Notification actions
            actionMap["notifyStateTimeout"] = new List<NamedAction>
            {
                new NamedAction("notifyStateTimeout", (sm) =>
                {
                    var state = sm.ContextMap["currentState"]?.ToString() ?? "";
                    _logger?.LogError("State '{State}' has timed out and entered timeout state", state);
                })
            };

            actionMap["notifyTransitionTimeout"] = new List<NamedAction>
            {
                new NamedAction("notifyTransitionTimeout", (sm) =>
                {
                    var transition = sm.ContextMap["currentTransition"]?.ToString() ?? "";
                    _logger?.LogError("Transition '{Transition}' has timed out", transition);
                })
            };

            actionMap["notifyActionTimeout"] = new List<NamedAction>
            {
                new NamedAction("notifyActionTimeout", (sm) =>
                {
                    var action = sm.ContextMap["currentAction"]?.ToString() ?? "";
                    _logger?.LogError("Action '{Action}' has timed out", action);
                })
            };

            // Execution actions
            actionMap["executeOperation"] = new List<NamedAction>
            {
                new NamedAction("executeOperation", (sm) =>
                {
                    var operationId = sm.ContextMap["operationId"]?.ToString() ?? "";
                    if (_pendingOperations.TryGetValue(operationId, out var tcs))
                    {
                        try
                        {
                            // Execute the actual operation
                            tcs.TrySetResult(true);
                            sm.Send("SUCCESS");
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                            sm.Send("FAILURE", new { Exception = ex });
                        }
                    }
                })
            };

            actionMap["handleError"] = new List<NamedAction>
            {
                new NamedAction("handleError", (sm) =>
                {
                    var exception = sm.ContextMap["lastException"] as Exception;
                    _logger?.LogError(exception, "Operation failed");
                })
            };

            actionMap["handleTimeout"] = new List<NamedAction>
            {
                new NamedAction("handleTimeout", (sm) =>
                {
                    _logger?.LogError("Operation timed out");
                })
            };

            actionMap["logFailure"] = new List<NamedAction>
            {
                new NamedAction("logFailure", (sm) =>
                {
                    _logger?.LogError("Recovery failed - entering failed state");
                })
            };

            return StateMachineFactory.CreateFromScript(config, threadSafe:false, true, actionMap);
        }

        private void StartTimeout(string timeoutId, TimeSpan timeout, Action onTimeout)
        {
            CancelTimeout(timeoutId);

            var cts = new CancellationTokenSource();
            _activeTimeouts[timeoutId] = cts;

            Task.Delay(timeout, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    onTimeout();
                    _activeTimeouts.TryRemove(timeoutId, out _);
                }
            });
        }

        private void CancelTimeout(string timeoutId)
        {
            if (_activeTimeouts.TryRemove(timeoutId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        private void OnInnerStateChanged(string newState)
        {
            _protectionMachine.ContextMap["currentState"] = newState;
            _protectionMachine.Send("STATE_CHANGED", new { State = newState });
            StateChanged?.Invoke(newState);
        }

        public void ConfigureStateTimeout(string state, TimeSpan timeout)
        {
            _stateTimeouts[state] = timeout;
            _logger?.LogDebug("Configured timeout for state '{State}': {Timeout}s", state, timeout.TotalSeconds);
        }

        public void ConfigureTransitionTimeout(string fromState, string eventName, TimeSpan timeout)
        {
            var key = $"{fromState}->{eventName}";
            _transitionTimeouts[key] = timeout;
            _logger?.LogDebug("Configured timeout for transition '{Transition}': {Timeout}s", key, timeout.TotalSeconds);
        }

        public void ConfigureActionTimeout(string actionName, TimeSpan timeout)
        {
            _actionTimeouts[actionName] = timeout;
            _logger?.LogDebug("Configured timeout for action '{Action}': {Timeout}s", actionName, timeout.TotalSeconds);
        }

        public async Task<bool> SendAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default)
        {
            var transitionKey = $"{GetActiveStateString()}->{eventName}";
            _protectionMachine.ContextMap["currentTransition"] = transitionKey;
            _protectionMachine.ContextMap["pendingEvent"] = eventName;
            _protectionMachine.ContextMap["pendingPayload"] = payload;

            _protectionMachine.Send("TRANSITION_START");

            try
            {
                var timeout = _transitionTimeouts.GetValueOrDefault(transitionKey, _options.DefaultTransitionTimeout);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                var stopwatch = Stopwatch.StartNew();
                Interlocked.Increment(ref _totalTransitions);

                await _innerMachine.SendAsync(eventName, payload);

                _protectionMachine.Send("TRANSITION_COMPLETE");

                _logger?.LogDebug("Transition '{Transition}' completed in {Duration}ms",
                    transitionKey, stopwatch.ElapsedMilliseconds);

                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _protectionMachine.Send("TRANSITION_TIMEOUT");

                if (_options.EnableTimeoutRecovery)
                {
                    _protectionMachine.Send("RECOVER");
                    // Wait for recovery
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                throw new TimeoutException($"Transition '{transitionKey}' timed out");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Transition '{Transition}' failed", transitionKey);
                throw;
            }
        }

        public TimeoutProtectionStatistics GetStatistics()
        {
            return new TimeoutProtectionStatistics
            {
                MachineId = Id,
                CurrentState = GetActiveStateString(),
                TotalTransitions = _totalTransitions,
                TotalTimeouts = _totalTimeouts,
                TotalRecoveries = _totalRecoveries,
                TimeoutRate = _totalTransitions > 0 ? (double)_totalTimeouts / _totalTransitions : 0,
                RecoveryRate = _totalTimeouts > 0 ? (double)_totalRecoveries / _totalTimeouts : 0,
                ActiveTimeoutScopes = _activeTimeouts.Count
            };
        }

        // IStateMachine implementation
        public new async Task<IStateMachine> Start()
        {
            _protectionMachine.Send("START_MONITORING");
            await _innerMachine.StartAsync();
            return this;
        }

        public new async Task<string> StartAsync()
        {
            await _protectionMachine.SendAsync("START_MONITORING");
            return await _innerMachine.StartAsync();
        }

        public new void Stop()
        {
            _protectionMachine.Send("STOP_MONITORING");
            _innerMachine.Stop();
            _protectionMachine.Stop();
        }

        public new async Task<string> SendAsync(string eventName, object? eventData = null)
        {
            return await _innerMachine.SendAsync(eventName, eventData);
        }

        public string GetActiveStateString()
        {
            return _innerMachine.GetActiveStateNames();
        }

        public List<CompoundState> GetActiveStates()
        {
            return _innerMachine.GetActiveStates();
        }

        public bool IsInState(string stateName)
        {
            return _innerMachine.IsInState(stateName);
        }

        public void Dispose()
        {
            foreach (var cts in _activeTimeouts.Values)
            {
                cts?.Cancel();
                cts?.Dispose();
            }

            _activeTimeouts.Clear();
            _protectionMachine?.Stop();
            _innerMachine?.Dispose();
        }
    }
}
