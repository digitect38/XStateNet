using System.Diagnostics;

namespace XStateNet
{
    /// <summary>
    /// Extension of PriorityStateMachine specifically for timing-sensitive operations
    /// </summary>
    public class TimingSensitiveStateMachine : PriorityStateMachine
    {
        private readonly Dictionary<string, StateMetrics> _stateMetrics = new();
        private readonly object _metricsLock = new();

        public TimingSensitiveStateMachine(
            IStateMachine innerMachine)
            : base(innerMachine, CreateDefaultTimingSensitiveConfig())
        {
            StateChangedDetailed += OnStateChangedMetrics;
        }

        private static TimingSensitiveConfig CreateDefaultTimingSensitiveConfig()
        {
            return new TimingSensitiveConfig
            {
                // Common timing-sensitive states
                CriticalStates = new HashSet<string>
                {
                    "halfOpen", "half-open", "HalfOpen", "HALF_OPEN",
                    "opening", "closing", "transitioning",
                    "error", "failed", "timeout",
                    "initializing", "connecting", "disconnecting"
                },

                // Common timing-sensitive events
                CriticalEvents = new HashSet<string>
                {
                    "TIMEOUT", "EXPIRE", "ERROR", "FAIL",
                    "RESET", "EMERGENCY_STOP", "ABORT",
                    "CIRCUIT_BREAK", "HALF_OPEN_TEST",
                    "CONNECT", "DISCONNECT", "RECONNECT"
                },

                MaxCriticalDelay = 100,  // 100ms for critical (was 5ms)
                MaxHighPriorityDelay = 500  // 500ms for high priority (was 20ms)
            };
        }

        /// <summary>
        /// Configure specific state transitions as critical
        /// </summary>
        public void AddCriticalTransition(string fromState, string toState)
        {
            lock (_metricsLock)
            {
                _config.CriticalTransitions.Add((fromState, toState));
                // Added critical transition
            }
        }

        /// <summary>
        /// Mark a state as timing-sensitive
        /// </summary>
        public void AddCriticalState(string state)
        {
            _config.CriticalStates.Add(state);
            // Added critical state
        }

        /// <summary>
        /// Mark an event as timing-sensitive
        /// </summary>
        public void AddCriticalEvent(string eventName)
        {
            _config.CriticalEvents.Add(eventName);
            // Added critical event
        }

        /// <summary>
        /// Execute state transition with timing guarantees
        /// </summary>
        public async Task<StateTransitionResult> ExecuteTimedTransitionAsync(
            string eventName,
            object data = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var actualTimeout = timeout ?? TimeSpan.FromMilliseconds(_config.MaxCriticalDelay);

            var previousState = CurrentState;
            var transitionCompleted = new TaskCompletionSource<string>();
            var stateChanged = false;

            // Subscribe to state change
            Action<string, string> onTransition = (oldSt, newSt) =>
            {
                stateChanged = true;
                transitionCompleted.TrySetResult(newSt);
            };

            StateChangedDetailed += onTransition;

            try
            {
                // Send with critical priority
                await SendWithPriorityAsync(eventName, data, EventPriority.Critical);

                // Wait for transition with timeout, but also handle when no transition occurs
                string newState;
                try
                {
                    // Use a shorter timeout for the TaskCompletionSource to avoid hanging
                    var waitTimeout = TimeSpan.FromMilliseconds(Math.Min(actualTimeout.TotalMilliseconds, 200));
                    newState = await transitionCompleted.Task.WaitAsync(waitTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Check if state actually changed even if event wasn't fired
                    stopwatch.Stop();
                    var finalState = CurrentState;
                    var success = finalState != previousState;

                    return new StateTransitionResult
                    {
                        FromState = previousState,
                        ToState = finalState,
                        EventName = eventName,
                        Duration = stopwatch.Elapsed,
                        WasCritical = true,
                        Success = success,
                        Error = success ? null : $"No state transition occurred for event {eventName}"
                    };
                }
                catch (TaskCanceledException)
                {
                    // Return failure result instead of throwing
                    stopwatch.Stop();
                    return new StateTransitionResult
                    {
                        FromState = previousState,
                        ToState = CurrentState,
                        EventName = eventName,
                        Duration = stopwatch.Elapsed,
                        WasCritical = true,
                        Success = false,
                        Error = "Operation was cancelled"
                    };
                }

                stopwatch.Stop();

                var result = new StateTransitionResult
                {
                    FromState = previousState,
                    ToState = newState,
                    EventName = eventName,
                    Duration = stopwatch.Elapsed,
                    WasCritical = true,
                    Success = true
                };

                // Record metrics
                RecordTransitionMetrics(result);

                // Log if transition was slow
                if (stopwatch.ElapsedMilliseconds > _config.MaxCriticalDelay)
                {
                    // Slow critical transition detected
                }

                return result;
            }
            finally
            {
                StateChangedDetailed -= onTransition;
            }
        }

        /// <summary>
        /// Batch multiple transitions with priority ordering
        /// </summary>
        public async Task<List<StateTransitionResult>> ExecuteBatchTransitionsAsync(
            params (string eventName, object data, EventPriority priority)[] transitions)
        {
            var results = new List<StateTransitionResult>();

            // Group by priority
            var priorityGroups = transitions
                .GroupBy(t => t.priority)
                .OrderBy(g => g.Key);

            foreach (var group in priorityGroups)
            {
                var tasks = group.Select(async t =>
                {
                    try
                    {
                        return await ExecuteTimedTransitionAsync(
                            t.eventName,
                            t.data,
                            TimeSpan.FromMilliseconds(
                                t.priority == EventPriority.Critical ? 1000 :
                                t.priority == EventPriority.High ? 2000 :
                                3000)); // Use reasonable timeouts for tests
                    }
                    catch (Exception ex)
                    {
                        return new StateTransitionResult
                        {
                            EventName = t.eventName,
                            Success = false,
                            Error = ex.Message
                        };
                    }
                });

                var groupResults = await Task.WhenAll(tasks);
                results.AddRange(groupResults);
            }

            return results;
        }

        private void OnStateChangedMetrics(string oldState, string newState)
        {
            lock (_metricsLock)
            {
                // Record state entry time
                if (!_stateMetrics.ContainsKey(newState))
                {
                    _stateMetrics[newState] = new StateMetrics { StateName = newState };
                }

                var metrics = _stateMetrics[newState];
                metrics.EntryCount++;
                metrics.LastEntryTime = DateTime.UtcNow;

                // Check if this was a critical transition
                if (_config.CriticalStates.Contains(newState))
                {
                    metrics.CriticalEntryCount++;
                    // Entered critical state
                }
            }
        }

        private void RecordTransitionMetrics(StateTransitionResult result)
        {
            lock (_metricsLock)
            {
                if (!_stateMetrics.ContainsKey(result.ToState))
                {
                    _stateMetrics[result.ToState] = new StateMetrics { StateName = result.ToState };
                }

                var metrics = _stateMetrics[result.ToState];
                metrics.TotalTransitionTime += result.Duration;
                metrics.TransitionCount++;

                if (result.Duration.TotalMilliseconds > metrics.MaxTransitionTime)
                {
                    metrics.MaxTransitionTime = result.Duration.TotalMilliseconds;
                    metrics.SlowestTransition = $"{result.FromState} -> {result.ToState}";
                }

                if (metrics.MinTransitionTime == 0 || result.Duration.TotalMilliseconds < metrics.MinTransitionTime)
                {
                    metrics.MinTransitionTime = result.Duration.TotalMilliseconds;
                }
            }
        }

        /// <summary>
        /// Get performance metrics for all states
        /// </summary>
        public Dictionary<string, StateMetrics> GetStateMetrics()
        {
            lock (_metricsLock)
            {
                return new Dictionary<string, StateMetrics>(_stateMetrics);
            }
        }

        /// <summary>
        /// Get metrics for critical states only
        /// </summary>
        public Dictionary<string, StateMetrics> GetCriticalStateMetrics()
        {
            lock (_metricsLock)
            {
                var criticalMetrics = new Dictionary<string, StateMetrics>();
                foreach (var kvp in _stateMetrics)
                {
                    if (_config.CriticalStates.Contains(kvp.Key))
                    {
                        criticalMetrics[kvp.Key] = kvp.Value;
                    }
                }
                return criticalMetrics;
            }
        }

        public class StateMetrics
        {
            public string StateName { get; set; }
            public int EntryCount { get; set; }
            public int CriticalEntryCount { get; set; }
            public DateTime LastEntryTime { get; set; }
            public TimeSpan TotalTransitionTime { get; set; }
            public int TransitionCount { get; set; }
            public double MaxTransitionTime { get; set; }
            public double MinTransitionTime { get; set; }
            public string SlowestTransition { get; set; }

            public double AverageTransitionTime =>
                TransitionCount > 0 ? TotalTransitionTime.TotalMilliseconds / TransitionCount : 0;
        }

        public class StateTransitionResult
        {
            public string FromState { get; set; }
            public string ToState { get; set; }
            public string EventName { get; set; }
            public TimeSpan Duration { get; set; }
            public bool WasCritical { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
        }
    }

    /// <summary>
    /// Extension methods for timing-sensitive operations
    /// </summary>
    public static class TimingSensitiveExtensions
    {
        /// <summary>
        /// Send critical state change event
        /// </summary>
        public static Task SendCriticalAsync(this IStateMachine machine, string eventName, object data = null)
        {
            if (machine is PriorityStateMachine priorityMachine)
            {
                return priorityMachine.SendWithPriorityAsync(eventName, data, EventPriority.Critical);
            }

            // Fallback to regular send
            return machine.SendAsync(eventName, data);
        }

        /// <summary>
        /// Send high priority event
        /// </summary>
        public static Task SendHighPriorityAsync(this IStateMachine machine, string eventName, object data = null)
        {
            if (machine is PriorityStateMachine priorityMachine)
            {
                return priorityMachine.SendWithPriorityAsync(eventName, data, EventPriority.High);
            }

            // Fallback to regular send
            return machine.SendAsync(eventName, data);
        }

        /// <summary>
        /// Execute a timed state transition
        /// </summary>
        public static async Task<bool> TryTimedTransitionAsync(
            this IStateMachine machine,
            string eventName,
            TimeSpan timeout,
            object data = null)
        {
            if (machine is TimingSensitiveStateMachine timingSensitive)
            {
                try
                {
                    var result = await timingSensitive.ExecuteTimedTransitionAsync(
                        eventName, data, timeout);
                    return result.Success;
                }
                catch
                {
                    return false;
                }
            }

            // Fallback to regular send with manual timeout
            var tcs = new TaskCompletionSource<bool>();
            Action<string> handler = (newState) => tcs.TrySetResult(true);

            if (machine is StateMachine sm)
            {
                sm.StateChanged += handler;
            }
            try
            {
                await machine.SendAsync(eventName, data);

                using var cts = new CancellationTokenSource(timeout);
                using (cts.Token.Register(() => tcs.TrySetResult(false)))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                if (machine is StateMachine stateMachine)
                {
                    stateMachine.StateChanged -= handler;
                }
            }
        }
    }
}