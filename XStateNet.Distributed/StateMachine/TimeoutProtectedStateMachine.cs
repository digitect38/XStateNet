using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Channels;
using XStateNet;

namespace XStateNet.Distributed.StateMachines
{
    /// <summary>
    /// StateMachines wrapper with comprehensive timeout protection
    /// </summary>
    public sealed class TimeoutProtectedStateMachine : IStateMachine
    {
        private readonly IStateMachine _innerMachine;
        private readonly ITimeoutProtection _timeoutProtection;
        private readonly IAdaptiveTimeoutManager _adaptiveTimeout;
        private readonly IDeadLetterQueue? _dlq;
        private readonly ILogger<TimeoutProtectedStateMachine>? _logger;
        private readonly TimeoutProtectedStateMachineOptions _options;

        // Timeout configuration per state/transition
        private readonly ConcurrentDictionary<string, TimeSpan> _stateTimeouts;
        private readonly ConcurrentDictionary<string, TimeSpan> _transitionTimeouts;
        private readonly ConcurrentDictionary<string, TimeSpan> _actionTimeouts;

        // Active timeout scopes
        private readonly ConcurrentDictionary<string, ITimeoutScope> _activeScopes;

        // Statistics
        private long _totalTransitions;
        private long _totalTimeouts;
        private long _totalRecoveries;

        // State tracking
        private string? _currentState;
        private DateTime _lastStateChangeTime;

        public string Id => _innerMachine.machineId;
        public string CurrentState => _innerMachine.GetActiveStateString();
        public bool IsRunning => _innerMachine.IsRunning;

        // IStateMachine implementation
        public string machineId => _innerMachine.machineId;
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

        public TimeoutProtectedStateMachine(
            IStateMachine innerMachine,
            ITimeoutProtection timeoutProtection,
            IDeadLetterQueue? dlq = null,
            TimeoutProtectedStateMachineOptions? options = null,
            ILogger<TimeoutProtectedStateMachine>? logger = null)
        {
            _innerMachine = innerMachine ?? throw new ArgumentNullException(nameof(innerMachine));
            _timeoutProtection = timeoutProtection ?? throw new ArgumentNullException(nameof(timeoutProtection));
            _options = options ?? new TimeoutProtectedStateMachineOptions();
            _dlq = dlq;
            _logger = logger;

            _adaptiveTimeout = new AdaptiveTimeoutManager(logger);

            _stateTimeouts = new ConcurrentDictionary<string, TimeSpan>();
            _transitionTimeouts = new ConcurrentDictionary<string, TimeSpan>();
            _actionTimeouts = new ConcurrentDictionary<string, TimeSpan>();
            _activeScopes = new ConcurrentDictionary<string, ITimeoutScope>();

            // Subscribe to state machine events
            SubscribeToStateMachineEvents();
        }

        public async Task<bool> SendAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default)
        {
            var transitionKey = $"{CurrentState}->{eventName}";
            var timeout = GetTransitionTimeout(transitionKey);

            var stopwatch = Stopwatch.StartNew();
            Interlocked.Increment(ref _totalTransitions);

            try
            {
                var result = await _timeoutProtection.ExecuteAsync(
                    async ct =>
                    {
                        await _innerMachine.SendAsync(eventName, payload);
                        return true;
                    },
                    timeout,
                    transitionKey,
                    cancellationToken);

                // Record successful execution for adaptive timeout
                _adaptiveTimeout.RecordExecution(transitionKey, stopwatch.Elapsed, true);

                return result;
            }
            catch (TimeoutException ex)
            {
                Interlocked.Increment(ref _totalTimeouts);
                _adaptiveTimeout.RecordExecution(transitionKey, stopwatch.Elapsed, false);

                _logger?.LogWarning(ex, "State transition '{Transition}' timed out after {Timeout:F1}s",
                    transitionKey, timeout.TotalSeconds);

                // Send to DLQ if configured
                if (_options.SendTimeoutEventsToDLQ && _dlq != null)
                {
                    await _dlq.EnqueueAsync(
                        new TimeoutEvent
                        {
                            MachineId = Id,
                            EventName = eventName,
                            Payload = payload,
                            FromState = CurrentState,
                            TimeoutDuration = timeout
                        },
                        source: Id,
                        reason: "Transition timeout",
                        exception: ex);
                }

                // Attempt recovery if configured
                if (_options.EnableTimeoutRecovery)
                {
                    return await AttemptRecoveryAsync(eventName, payload, cancellationToken);
                }

                throw;
            }
        }

        public async Task<T?> SendWithResponseAsync<T>(
            string eventName,
            object? payload = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            timeout ??= _options.DefaultResponseTimeout;

            using var scope = _timeoutProtection.CreateScope(timeout.Value, $"Response:{eventName}");

            try
            {
                // Send event
                await SendAsync(eventName, payload, scope.Token);

                // Wait for response
                return await WaitForResponseAsync<T>(eventName, scope);
            }
            catch (TimeoutException ex)
            {
                _logger?.LogWarning(ex, "No response received for event '{EventName}' within {Timeout:F1}s",
                    eventName, timeout.Value.TotalSeconds);
                return default;
            }
        }

        public void ConfigureStateTimeout(string state, TimeSpan timeout)
        {
            _stateTimeouts[state] = timeout;
            _logger?.LogDebug("Configured timeout for state '{State}': {Timeout:F1}s",
                state, timeout.TotalSeconds);
        }

        public void ConfigureTransitionTimeout(string fromState, string eventName, TimeSpan timeout)
        {
            var key = $"{fromState}->{eventName}";
            _transitionTimeouts[key] = timeout;
            _logger?.LogDebug("Configured timeout for transition '{Transition}': {Timeout:F1}s",
                key, timeout.TotalSeconds);
        }

        public void ConfigureActionTimeout(string actionName, TimeSpan timeout)
        {
            _actionTimeouts[actionName] = timeout;
            _logger?.LogDebug("Configured timeout for action '{Action}': {Timeout:F1}s",
                actionName, timeout.TotalSeconds);
        }

        public async Task<bool> ExecuteWithStateTimeoutAsync(
            Func<CancellationToken, Task<bool>> operation,
            CancellationToken cancellationToken = default)
        {
            var timeout = GetStateTimeout(CurrentState);

            using var scope = _timeoutProtection.CreateScope(timeout, $"State:{CurrentState}");
            _activeScopes[CurrentState] = scope;

            try
            {
                return await scope.ExecuteAsync(operation, $"StateExecution:{CurrentState}");
            }
            finally
            {
                _activeScopes.TryRemove(CurrentState, out _);
            }
        }

        public async Task WaitForStateAsync(
            string targetState,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            timeout ??= _options.DefaultStateWaitTimeout;

            var tcs = new TaskCompletionSource<bool>();
            Action<string>? handler = null;

            handler = (newState) =>
            {
                if (newState == targetState)
                {
                    tcs.TrySetResult(true);
                }
            };

            _innerMachine.StateChanged += handler;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout.Value);

                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"State '{targetState}' was not reached within {timeout.Value.TotalSeconds:F1} seconds");
            }
            finally
            {
                _innerMachine.StateChanged -= handler;
            }
        }

        public TimeoutProtectionStatistics GetStatistics()
        {
            var timeoutStats = _timeoutProtection.GetStatistics();

            return new TimeoutProtectionStatistics
            {
                MachineId = Id,
                CurrentState = CurrentState,
                TotalTransitions = _totalTransitions,
                TotalTimeouts = _totalTimeouts,
                TotalRecoveries = _totalRecoveries,
                TimeoutRate = _totalTransitions > 0
                    ? (double)_totalTimeouts / _totalTransitions
                    : 0,
                RecoveryRate = _totalTimeouts > 0
                    ? (double)_totalRecoveries / _totalTimeouts
                    : 0,
                ActiveTimeoutScopes = _activeScopes.Count,
                BaseStatistics = timeoutStats,
                AdaptiveTimeouts = GetAdaptiveTimeoutSummary()
            };
        }

        private void SubscribeToStateMachineEvents()
        {
            _innerMachine.StateChanged += OnStateChanged;
            // Note: IStateMachine doesn't have these events
            // _innerMachine.TransitionStarted += OnTransitionStarted;
            // _innerMachine.TransitionCompleted += OnTransitionCompleted;

            if (_innerMachine is IActionExecutor actionExecutor)
            {
                actionExecutor.ActionStarted += OnActionStarted;
                actionExecutor.ActionCompleted += OnActionCompleted;
            }
        }

        private void OnStateChanged(string newState)
        {
            // Cancel timeout scope for previous state
            if (_currentState != null && _activeScopes.TryRemove(_currentState, out var oldScope))
            {
                oldScope.Dispose();
            }

            var previousState = _currentState;
            _currentState = newState;
            _lastStateChangeTime = DateTime.UtcNow;

            // Start timeout monitoring for new state if configured
            if (_stateTimeouts.ContainsKey(newState))
            {
                StartStateTimeoutMonitoring(newState);
            }
        }

        private void OnTransitionStarted(object? sender, TransitionEventArgs e)
        {
            // Log transition start for timeout tracking
            _logger?.LogDebug("Transition started: {FromState} -> {ToState} via {Event}",
                e.FromState, e.ToState, e.EventName);
        }

        private void OnTransitionCompleted(object? sender, TransitionEventArgs e)
        {
            // Log transition completion
            _logger?.LogDebug("Transition completed: {FromState} -> {ToState} via {Event} in {Duration:F1}ms",
                e.FromState, e.ToState, e.EventName, e.Duration.TotalMilliseconds);
        }

        private void OnActionStarted(object? sender, ActionEventArgs e)
        {
            // Start timeout monitoring for action
            if (_actionTimeouts.TryGetValue(e.ActionName, out var timeout))
            {
                StartActionTimeoutMonitoring(e.ActionName, timeout);
            }
        }

        private void OnActionCompleted(object? sender, ActionEventArgs e)
        {
            // Stop timeout monitoring for action
            if (_activeScopes.TryRemove($"Action:{e.ActionName}", out var scope))
            {
                scope.Dispose();
            }
        }

        private void StartStateTimeoutMonitoring(string state)
        {
            var timeout = GetStateTimeout(state);
            var scope = _timeoutProtection.CreateScope(timeout, $"StateMonitor:{state}");

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(timeout, scope.Token);

                    // State timeout occurred
                    _logger?.LogWarning("State '{State}' exceeded timeout of {Timeout:F1}s",
                        state, timeout.TotalSeconds);

                    // Trigger timeout event if still in the same state
                    if (CurrentState == state)
                    {
                        await HandleStateTimeoutAsync(state);
                    }
                }
                catch (OperationCanceledException)
                {
                    // State changed before timeout
                }
            });
        }

        private void StartActionTimeoutMonitoring(string actionName, TimeSpan timeout)
        {
            var scope = _timeoutProtection.CreateScope(timeout, $"Action:{actionName}");
            _activeScopes[$"Action:{actionName}"] = scope;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(timeout, scope.Token);

                    // Action timeout occurred
                    _logger?.LogWarning("Action '{Action}' exceeded timeout of {Timeout:F1}s",
                        actionName, timeout.TotalSeconds);

                    await HandleActionTimeoutAsync(actionName);
                }
                catch (OperationCanceledException)
                {
                    // Action completed before timeout
                }
            });
        }

        private async Task HandleStateTimeoutAsync(string state)
        {
            if (_options.StateTimeoutEvent != null)
            {
                // Send timeout event to trigger transition
                await SendAsync(_options.StateTimeoutEvent, new { TimedOutState = state });
            }

            if (_dlq != null && _options.SendStateTimeoutsToDLQ)
            {
                await _dlq.EnqueueAsync(
                    new StateTimeoutEvent
                    {
                        MachineId = Id,
                        State = state,
                        TimeoutDuration = GetStateTimeout(state)
                    },
                    source: Id,
                    reason: "State timeout");
            }
        }

        private async Task HandleActionTimeoutAsync(string actionName)
        {
            if (_dlq != null)
            {
                await _dlq.EnqueueAsync(
                    new ActionTimeoutEvent
                    {
                        MachineId = Id,
                        ActionName = actionName,
                        State = CurrentState,
                        TimeoutDuration = _actionTimeouts.GetValueOrDefault(actionName)
                    },
                    source: Id,
                    reason: "Action timeout");
            }
        }

        private async Task<bool> AttemptRecoveryAsync(
            string eventName,
            object? payload,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _totalRecoveries);

            _logger?.LogInformation("Attempting recovery for event '{EventName}' from state '{State}'",
                eventName, CurrentState);

            // Try with extended timeout
            var extendedTimeout = TimeSpan.FromMilliseconds(
                _options.DefaultTransitionTimeout.TotalMilliseconds * _options.RecoveryTimeoutMultiplier);

            try
            {
                return await _timeoutProtection.ExecuteAsync(
                    async ct => {
                        await _innerMachine.SendAsync(eventName, payload);
                        return true;
                    },
                    extendedTimeout,
                    $"Recovery:{eventName}",
                    cancellationToken);
            }
            catch
            {
                _logger?.LogError("Recovery failed for event '{EventName}'", eventName);
                return false;
            }
        }

        private async Task<T?> WaitForResponseAsync<T>(string eventName, ITimeoutScope scope)
        {
            var responseChannel = new BoundedChannelManager<T>(
                $"Response:{eventName}",
                new CustomBoundedChannelOptions { Capacity = 1 });

            // Subscribe to response events
            EventHandler<ResponseReceivedEventArgs>? handler = null;
            handler = (sender, args) =>
            {
                if (args.RequestEvent == eventName && args.Response is T response)
                {
                    responseChannel.TryWrite(response);
                }
            };

            // _innerMachine.ResponseReceived += handler;

            try
            {
                var (success, response) = await responseChannel.ReadAsync(scope.Token);
                return success ? response : default;
            }
            finally
            {
                // _innerMachine.ResponseReceived -= handler;
                responseChannel.Dispose();
            }
        }

        private TimeSpan GetStateTimeout(string state)
        {
            if (_stateTimeouts.TryGetValue(state, out var timeout))
                return timeout;

            if (_options.UseAdaptiveTimeouts)
                return _adaptiveTimeout.GetTimeout($"State:{state}");

            return _options.DefaultStateTimeout;
        }

        private TimeSpan GetTransitionTimeout(string transition)
        {
            if (_transitionTimeouts.TryGetValue(transition, out var timeout))
                return timeout;

            if (_options.UseAdaptiveTimeouts)
                return _adaptiveTimeout.GetTimeout($"Transition:{transition}");

            return _options.DefaultTransitionTimeout;
        }

        private ConcurrentDictionary<string, AdaptiveTimeoutStatistics> GetAdaptiveTimeoutSummary()
        {
            var summary = new ConcurrentDictionary<string, AdaptiveTimeoutStatistics>();

            foreach (var state in _stateTimeouts.Keys)
            {
                summary[$"State:{state}"] = _adaptiveTimeout.GetStatistics($"State:{state}");
            }

            foreach (var transition in _transitionTimeouts.Keys)
            {
                summary[$"Transition:{transition}"] = _adaptiveTimeout.GetStatistics($"Transition:{transition}");
            }

            return summary;
        }

#pragma warning disable CS0618 // Type or member is obsolete
        public IStateMachine Start()
#pragma warning restore CS0618
        {
            _innerMachine.Start();
            return this;
        }

        public async Task<string> StartAsync()
        {
            return await _innerMachine.StartAsync();
        }

        public void Stop() => _innerMachine.Stop();

        // IStateMachine methods
#pragma warning disable CS0618 // Type or member is obsolete
        public void Send(string eventName, object? eventData = null)
#pragma warning restore CS0618
        {
            _innerMachine.Send(eventName, eventData);
        }

        public async Task SendAsync(string eventName, object? eventData = null)
        {
            await _innerMachine.SendAsync(eventName, eventData);
        }

        public async Task<string> SendAsyncWithState(string eventName, object? eventData = null)
        {
            return await _innerMachine.SendAsyncWithState(eventName, eventData);
        }

        public string GetActiveStateString()
        {
            return _innerMachine.GetActiveStateString();
        }

        public List<CompoundState> GetActiveStates()
        {
            return _innerMachine.GetActiveStates();
        }

        public bool IsInState(string stateName)
        {
            return _innerMachine.IsInState(stateName);
        }

        // ITimeoutProtectedStateMachine methods
        public async Task<bool> SendEventAsync(string eventName, object? payload = null, CancellationToken cancellationToken = default)
        {
            try
            {
                await _innerMachine.SendAsync(eventName, payload);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SendEvent(string eventName, object? payload = null)
        {
            try
            {
                _innerMachine.Send(eventName, payload);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SetStateTimeout(string state, TimeSpan timeout)
        {
            _stateTimeouts[state] = timeout;
        }

        public void SetTransitionTimeout(string fromState, string toState, TimeSpan timeout)
        {
            _transitionTimeouts[$"{fromState}->{toState}"] = timeout;
        }

        public void SetActionTimeout(string actionName, TimeSpan timeout)
        {
            _actionTimeouts[actionName] = timeout;
        }

        public void Dispose()
        {
            foreach (var scope in _activeScopes.Values)
            {
                scope?.Dispose();
            }

            _timeoutProtection?.Dispose();
            _innerMachine?.Dispose();
        }
    }

    // Interfaces and supporting types
    public interface ITimeoutProtectedStateMachine
    {
        Task<T?> SendWithResponseAsync<T>(string eventName, object? payload = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default);

        Task<bool> ExecuteWithStateTimeoutAsync(Func<CancellationToken, Task<bool>> operation,
            CancellationToken cancellationToken = default);

        Task WaitForStateAsync(string targetState, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default);

        void ConfigureStateTimeout(string state, TimeSpan timeout);
        void ConfigureTransitionTimeout(string fromState, string eventName, TimeSpan timeout);
        void ConfigureActionTimeout(string actionName, TimeSpan timeout);

        TimeoutProtectionStatistics GetStatistics();
    }

    public class TimeoutProtectedOptions
    {
        // Default timeouts
        public TimeSpan DefaultStateTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan DefaultTransitionTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan DefaultActionTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan DefaultResponseTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan DefaultStateWaitTimeout { get; set; } = TimeSpan.FromMinutes(1);

        // Adaptive timeout settings
        public bool UseAdaptiveTimeouts { get; set; } = true;
        public double AdaptivePercentile { get; set; } = 0.95;
        public TimeSpan MinTimeout { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(10);

        // Recovery settings
        public bool EnableTimeoutRecovery { get; set; } = true;
        public double RecoveryTimeoutMultiplier { get; set; } = 2.0;

        // DLQ integration
        public bool SendTimeoutEventsToDLQ { get; set; } = true;
        public bool SendStateTimeoutsToDLQ { get; set; } = true;

        // Monitoring
        public bool ForceTimeoutOnStuckTransitions { get; set; } = false;
        public string? StateTimeoutEvent { get; set; } = "STATE_TIMEOUT";
    }

    public class TimeoutProtectionStatistics
    {
        public string MachineId { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public long TotalTransitions { get; set; }
        public long TotalTimeouts { get; set; }
        public long TotalRecoveries { get; set; }
        public double TimeoutRate { get; set; }
        public double RecoveryRate { get; set; }
        public int ActiveTimeoutScopes { get; set; }
        public TimeoutStatistics BaseStatistics { get; set; } = new();
        public ConcurrentDictionary<string, AdaptiveTimeoutStatistics> AdaptiveTimeouts { get; set; } = new();
    }

    // Event types for DLQ
    public class TimeoutEvent
    {
        public string MachineId { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public object? Payload { get; set; }
        public string FromState { get; set; } = string.Empty;
        public TimeSpan TimeoutDuration { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    public class StateTimeoutEvent
    {
        public string MachineId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public TimeSpan TimeoutDuration { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    public class ActionTimeoutEvent
    {
        public string MachineId { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public TimeSpan TimeoutDuration { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }

    public class TimeoutProtectedStateMachineOptions
    {
        public TimeSpan DefaultStateTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan DefaultTransitionTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan DefaultActionTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan DefaultResponseTimeout { get; set; } = TimeSpan.FromSeconds(20);
        public TimeSpan DefaultStateWaitTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public bool EnableDlqOnTimeout { get; set; } = true;
        public bool SendTimeoutEventsToDLQ { get; set; } = true;
        public bool SendStateTimeoutsToDLQ { get; set; } = true;
        public bool EnableTimeoutRecovery { get; set; } = true;
        public bool UseAdaptiveTimeouts { get; set; } = false;
        public double RecoveryTimeoutMultiplier { get; set; } = 1.5;
        public string StateTimeoutEvent { get; set; } = "TIMEOUT";
        public ConcurrentDictionary<string, TimeSpan> StateTimeouts { get; set; } = new();
        public ConcurrentDictionary<string, TimeSpan> TransitionTimeouts { get; set; } = new();
        public ConcurrentDictionary<string, TimeSpan> ActionTimeouts { get; set; } = new();
    }

    public class AdaptiveTimeoutManager : IAdaptiveTimeoutManager
    {
        private readonly ConcurrentDictionary<string, List<TimeSpan>> _operationHistory = new();
        private readonly ConcurrentDictionary<string, TimeSpan> _timeouts = new();
        private readonly ILogger? _logger;

        public AdaptiveTimeoutManager(ILogger? logger = null)
        {
            _logger = logger;
        }

        public TimeSpan GetTimeout(string operationName)
        {
            if (_timeouts.TryGetValue(operationName, out var timeout))
            {
                return timeout;
            }

            if (_operationHistory.TryGetValue(operationName, out var history) && history.Count > 0)
            {
                var sorted = history.OrderBy(t => t).ToList();
                var p95Index = Math.Min((int)(sorted.Count * 0.95), sorted.Count - 1);
                return TimeSpan.FromMilliseconds(sorted[p95Index].TotalMilliseconds * 1.5);
            }
            return TimeSpan.FromSeconds(30);
        }

        public void RecordExecution(string operationName, TimeSpan duration, bool success)
        {
            if (success)
            {
                _operationHistory.AddOrUpdate(operationName,
                    new List<TimeSpan> { duration },
                    (key, list) =>
                    {
                        list.Add(duration);
                        if (list.Count > 100) list.RemoveAt(0);
                        return list;
                    });
            }
            else
            {
                _logger?.LogWarning("Operation {OperationName} failed after {Duration}ms", operationName, duration.TotalMilliseconds);
            }
        }

        public void AdjustTimeout(string operationName, double factor)
        {
            var currentTimeout = GetTimeout(operationName);
            var newTimeout = TimeSpan.FromMilliseconds(currentTimeout.TotalMilliseconds * factor);
            _timeouts[operationName] = newTimeout;
        }

        public AdaptiveTimeoutStatistics GetStatistics(string operationName)
        {
            var stats = new AdaptiveTimeoutStatistics
            {
                OperationName = operationName,
                CurrentTimeout = GetTimeout(operationName)
            };

            if (_operationHistory.TryGetValue(operationName, out var history) && history.Count > 0)
            {
                stats.SampleCount = history.Count;
                stats.AverageDuration = TimeSpan.FromMilliseconds(history.Average(t => t.TotalMilliseconds));

                var sorted = history.OrderBy(t => t).ToList();
                stats.P50Duration = sorted[sorted.Count / 2];
                stats.P95Duration = sorted[(int)(sorted.Count * 0.95)];
                stats.P99Duration = sorted[(int)(sorted.Count * 0.99)];
                stats.SuccessRate = 1.0; // Assuming all recorded durations are successful
            }

            return stats;
        }
    }


    public interface IActionExecutor
    {
        event EventHandler<ActionEventArgs> ActionStarted;
        event EventHandler<ActionEventArgs> ActionCompleted;
    }

    // Event args
    public class StateChangedEventArgs : EventArgs
    {
        public string OldState { get; set; } = string.Empty;
        public string NewState { get; set; } = string.Empty;
    }

    public class TransitionEventArgs : EventArgs
    {
        public string FromState { get; set; } = string.Empty;
        public string ToState { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    public class ResponseReceivedEventArgs : EventArgs
    {
        public string RequestEvent { get; set; } = string.Empty;
        public object? Response { get; set; }
    }

    public class ActionEventArgs : EventArgs
    {
        public string ActionName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }
}