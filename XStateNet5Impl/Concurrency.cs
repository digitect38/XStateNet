using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XStateNet;

/// <summary>
/// Thread-safe event queue for state machine
/// </summary>
public class EventQueue : IDisposable
{
    private readonly Channel<EventMessage> _channel;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _processingTask;
    private readonly StateMachine _stateMachine;
    private readonly SemaphoreSlim _processingSemaphore;
    private bool _disposed;

    public EventQueue(StateMachine stateMachine)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _cancellationTokenSource = new CancellationTokenSource();
        _processingSemaphore = new SemaphoreSlim(1, 1);

        // Unbounded channel for event queuing
        _channel = Channel.CreateUnbounded<EventMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _processingTask = ProcessEventsAsync(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Enqueue an event for processing and wait for completion
    /// </summary>
    public async Task<string> SendAsync(string eventName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EventQueue));

        var completionSource = new TaskCompletionSource<string>();
        var message = new EventMessage(eventName, DateTime.UtcNow, completionSource);
        await _channel.Writer.WriteAsync(message);

        // Wait for the event to be processed
        return await completionSource.Task;
    }

    /// <summary>
    /// Process events from the queue sequentially
    /// </summary>
    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                await _processingSemaphore.WaitAsync(cancellationToken);
                try
                {
                    await _stateMachine.ProcessEventAsync(message.EventName);

                    // Signal completion with the current state
                    var currentState = _stateMachine.GetActiveStateNames();
                    message.CompletionSource?.SetResult(currentState);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error processing event {message.EventName}: {ex.Message}");
                    message.CompletionSource?.SetException(ex);
                }
                finally
                {
                    _processingSemaphore.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when shutting down
        }
    }

    public void Clear()
    {
        // Drain the channel without processing
        while (_channel.Reader.TryRead(out _))
        {
            // Just discard the message
        }
        Logger.Debug("EventQueue cleared");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _channel.Writer.TryComplete();
        _cancellationTokenSource.Cancel();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Logger.Error($"Error disposing EventQueue: {ex.Message}");
        }

        _cancellationTokenSource.Dispose();
        _processingSemaphore.Dispose();
    }

    private record EventMessage(string EventName, DateTime Timestamp, TaskCompletionSource<string>? CompletionSource = null);
}

/// <summary>
/// Thread-safe state machine synchronization
/// </summary>
public class StateMachineSync
{
    private readonly ReaderWriterLockSlim _stateLock = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _transitionLocks = new();
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Acquire read lock for state access
    /// </summary>
    public IDisposable AcquireReadLock()
    {
        if (!_stateLock.TryEnterReadLock(_lockTimeout))
            throw new TimeoutException("Failed to acquire read lock");

        return new LockReleaser(() => _stateLock.ExitReadLock());
    }

    /// <summary>
    /// Acquire write lock for state modification
    /// </summary>
    public IDisposable AcquireWriteLock()
    {
        if (!_stateLock.TryEnterWriteLock(_lockTimeout))
            throw new TimeoutException("Failed to acquire write lock");

        return new LockReleaser(() => _stateLock.ExitWriteLock());
    }

    /// <summary>
    /// Acquire transition lock for specific state
    /// </summary>
    public async Task<IDisposable> AcquireTransitionLockAsync(string stateName)
    {
        var semaphore = _transitionLocks.GetOrAdd(stateName, _ => new SemaphoreSlim(1, 1));

        if (!await semaphore.WaitAsync(_lockTimeout))
            throw new TimeoutException($"Failed to acquire transition lock for state {stateName}");

        return new LockReleaser(() => semaphore.Release());
    }

    /// <summary>
    /// Check for potential deadlock in transition path
    /// </summary>
    public bool CheckDeadlockPotential(string fromState, string toState, HashSet<string> visitedStates)
    {
        if (visitedStates == null)
            visitedStates = new HashSet<string>();

        // Detect circular reference
        if (visitedStates.Contains(toState))
        {
            Logger.Warning($"Potential deadlock detected: circular reference from {fromState} to {toState}");
            return true;
        }

        visitedStates.Add(fromState);
        return false;
    }

    public void Dispose()
    {
        _stateLock?.Dispose();
        foreach (var semaphore in _transitionLocks.Values)
        {
            semaphore?.Dispose();
        }
        _transitionLocks.Clear();
    }

    private class LockReleaser : IDisposable
    {
        private readonly Action _releaseAction;
        private bool _disposed;

        public LockReleaser(Action releaseAction)
        {
            _releaseAction = releaseAction;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _releaseAction?.Invoke();
        }
    }
}

/// <summary>
/// Thread-safe transition executor
/// </summary>
public class SafeTransitionExecutor : TransitionExecutor
{
    private readonly StateMachineSync _sync;
    private readonly ConcurrentDictionary<string, bool> _activeTransitions = new();

    public SafeTransitionExecutor(string? machineId, StateMachineSync sync) : base(machineId)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
    }

    protected override async Task ExecuteCore(Transition? transition, string eventName)
    {
        await ExecuteAsync(transition, eventName);//.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Execute transition with proper async handling
    /// </summary>
    public override async Task ExecuteAsync(Transition? transition, string eventName)
    {
        if (transition == null) return;
        if (StateMachine == null)
            throw new InvalidOperationException("StateMachine is not initialized");

        var transitionKey = $"{transition.SourceName}->{transition.TargetName}";

        // Check for re-entrancy using lock-free ConcurrentDictionary
        if (!_activeTransitions.TryAdd(transitionKey, true))
        {
            Logger.Warning($"Skipping re-entrant transition: {transitionKey}");
            return;
        }

        try
        {
            // Check for deadlock potential
            if (transition.SourceName != null && transition.TargetName != null &&
                _sync.CheckDeadlockPotential(transition.SourceName, transition.TargetName, new HashSet<string>()))
            {
                Logger.Error($"Deadlock potential detected, aborting transition: {transitionKey}");
                return;
            }

            // Acquire transition lock
            if (transition.SourceName != null)
            {
                using (await _sync.AcquireTransitionLockAsync(transition.SourceName))
                {
                    await PerformTransitionAsync(transition, eventName);
                }
            }
            else
            {
                await PerformTransitionAsync(transition, eventName);
            }
        }
        finally
        {
            _activeTransitions.TryRemove(transitionKey, out _);
        }
    }

    private async Task PerformTransitionAsync(Transition transition, string eventName)
    {
        Logger.Debug($">> transition on event {eventName} in state {transition.SourceName}");

        bool guardPassed = transition.Guard == null || transition.Guard.PredicateFunc(StateMachine!);
        if (transition.Guard != null)
        {
            // Notify guard evaluation
            StateMachine?.RaiseGuardEvaluated(transition.Guard.Name, guardPassed);
        }

        if (guardPassed && (transition.InCondition == null || transition.InCondition()))
        {
            string? sourceName = transition?.SourceName;
            string? targetName = transition?.TargetName;

            if (string.IsNullOrWhiteSpace(sourceName))
                throw new InvalidOperationException("Source state name cannot be null or empty");

            if (targetName != null)
            {
                var (exitList, entryList) = StateMachine!.GetFullTransitionSinglePath(sourceName, targetName);

                string? firstExit = exitList.FirstOrDefault();
                string? firstEntry = entryList.FirstOrDefault();

                // Exit states
                if (firstExit != null)
                    await StateMachine!.TransitUp(firstExit.ToState(StateMachine!) as CompoundState);

                Logger.Info($"Transit: [ {sourceName} --> {targetName} ] by {eventName}");

                // Execute transition actions
                if (transition?.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            // Notify action execution
                            StateMachine?.RaiseActionExecuted(action.Name, targetName);
                            await action.Action(StateMachine!);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error executing action {action.Name}: {ex.Message}");
                        }
                    }
                }

                // Enter states
                if (firstEntry != null)
                    await StateMachine!.TransitDown(firstEntry.ToState(StateMachine!) as CompoundState, targetName);

                // Notify transition completed
                StateNode? sourceNode = null;
                StateNode? targetNode = null;

                // Safely get source node
                if (!string.IsNullOrWhiteSpace(sourceName))
                {
                    try
                    {
                        sourceNode = GetState(sourceName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to get source state '{sourceName}': {ex.Message}");
                    }
                }

                // Safely get target node
                if (!string.IsNullOrWhiteSpace(targetName))
                {
                    try
                    {
                        targetNode = GetState(targetName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to get target state '{targetName}': {ex.Message}");
                    }
                }

                // Only raise transition if both states are valid
                if (sourceNode != null && targetNode != null)
                {
                    StateMachine!.RaiseTransition(sourceNode as CompoundState, targetNode, eventName);
                }

                // Fire StateChanged event after transition is complete
                StateMachine!.RaiseStateChanged();
            }
            else
            {
                // Action-only transition
                if (transition?.Actions != null)
                {
                    foreach (var action in transition.Actions)
                    {
                        try
                        {
                            await action.Action(StateMachine!);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error executing action {action.Name}: {ex.Message}");
                        }
                    }
                }
            }
        }
        else
        {
            Logger.Debug($"Condition not met for transition on event {eventName}");
        }
    }
}