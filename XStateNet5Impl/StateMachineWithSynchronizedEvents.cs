using System;
using System.Threading.Tasks;

namespace XStateNet;

/// <summary>
/// Extension of StateMachine with synchronized event handlers
/// </summary>
public partial class StateMachine
{
    private ThreadSafeEventHandler<StateChangedEventArgs>? _synchronizedStateChanged;
    private ThreadSafeEventHandler<ErrorOccurredEventArgs>? _synchronizedErrorOccurred;
    private ThreadSafeEventHandler<TransitionEventArgs>? _synchronizedTransition;

    /// <summary>
    /// Thread-safe, ordered state change event
    /// </summary>
    public ThreadSafeEventHandler<StateChangedEventArgs> SynchronizedStateChanged
    {
        get
        {
            if (_synchronizedStateChanged == null)
            {
                lock (this)
                {
                    _synchronizedStateChanged ??= new ThreadSafeEventHandler<StateChangedEventArgs>();
                }
            }
            return _synchronizedStateChanged;
        }
    }

    /// <summary>
    /// Thread-safe, ordered error event
    /// </summary>
    public ThreadSafeEventHandler<ErrorOccurredEventArgs> SynchronizedErrorOccurred
    {
        get
        {
            if (_synchronizedErrorOccurred == null)
            {
                lock (this)
                {
                    _synchronizedErrorOccurred ??= new ThreadSafeEventHandler<ErrorOccurredEventArgs>();
                }
            }
            return _synchronizedErrorOccurred;
        }
    }

    /// <summary>
    /// Thread-safe, ordered transition event
    /// </summary>
    public ThreadSafeEventHandler<TransitionEventArgs> SynchronizedTransition
    {
        get
        {
            if (_synchronizedTransition == null)
            {
                lock (this)
                {
                    _synchronizedTransition ??= new ThreadSafeEventHandler<TransitionEventArgs>();
                }
            }
            return _synchronizedTransition;
        }
    }

    /// <summary>
    /// Subscribe to state changes with priority ordering
    /// </summary>
    /// <param name="handler">Handler to execute on state change</param>
    /// <param name="priority">Lower priority executes first (default: Int32.MaxValue)</param>
    /// <param name="name">Optional name for debugging</param>
    /// <returns>Disposable subscription</returns>
    public IDisposable OnStateChanged(Action<StateChangedEventArgs> handler, int priority = int.MaxValue, string? name = null)
    {
        return SynchronizedStateChanged.SubscribeWithPriority(handler, priority, name);
    }

    /// <summary>
    /// Subscribe to errors with priority ordering
    /// </summary>
    /// <param name="handler">Handler to execute on error</param>
    /// <param name="priority">Lower priority executes first (default: Int32.MaxValue)</param>
    /// <param name="name">Optional name for debugging</param>
    /// <returns>Disposable subscription</returns>
    public IDisposable OnError(Action<ErrorOccurredEventArgs> handler, int priority = int.MaxValue, string? name = null)
    {
        return SynchronizedErrorOccurred.SubscribeWithPriority(handler, priority, name);
    }

    /// <summary>
    /// Subscribe to transitions with priority ordering
    /// </summary>
    /// <param name="handler">Handler to execute on transition</param>
    /// <param name="priority">Lower priority executes first (default: Int32.MaxValue)</param>
    /// <param name="name">Optional name for debugging</param>
    /// <returns>Disposable subscription</returns>
    public IDisposable OnTransitionEvent(Action<TransitionEventArgs> handler, int priority = int.MaxValue, string? name = null)
    {
        return SynchronizedTransition.SubscribeWithPriority(handler, priority, name);
    }

    /// <summary>
    /// Raise synchronized state changed event
    /// </summary>
    internal void RaiseSynchronizedStateChanged(string newState, string? previousState = null)
    {
        // Raise synchronized event
        if (_synchronizedStateChanged?.HasHandlers == true)
        {
            var args = new StateChangedEventArgs(machineId, newState, previousState, DateTime.UtcNow);
            _synchronizedStateChanged.Invoke(args);
        }

        // Also raise legacy event for backward compatibility
        StateChanged?.Invoke(newState);
    }

    /// <summary>
    /// Raise synchronized error event
    /// </summary>
    internal void RaiseSynchronizedError(Exception exception, string? state = null, string? eventName = null)
    {
        // Raise synchronized event
        if (_synchronizedErrorOccurred?.HasHandlers == true)
        {
            var args = new ErrorOccurredEventArgs(machineId, exception, state, eventName, DateTime.UtcNow);
            _synchronizedErrorOccurred.Invoke(args);
        }

        // Also raise legacy event for backward compatibility
        ErrorOccurred?.Invoke(exception);
    }

    /// <summary>
    /// Raise synchronized transition event
    /// </summary>
    internal void RaiseSynchronizedTransition(CompoundState? fromState, StateNode? toState, string eventName)
    {
        // Raise synchronized event
        if (_synchronizedTransition?.HasHandlers == true)
        {
            var args = new TransitionEventArgs(
                machineId,
                fromState?.Name,
                toState?.Name ?? toState?.ToString(),
                eventName,
                DateTime.UtcNow
            );
            _synchronizedTransition.Invoke(args);
        }

        // Also raise legacy event for backward compatibility
        OnTransition?.Invoke(fromState, toState, eventName);
    }

    /// <summary>
    /// Clean up synchronized event handlers
    /// </summary>
    protected void DisposeSynchronizedHandlers()
    {
        _synchronizedStateChanged?.Dispose();
        _synchronizedErrorOccurred?.Dispose();
        _synchronizedTransition?.Dispose();
    }
}

/// <summary>
/// Event arguments for state changes
/// </summary>
public class StateChangedEventArgs : EventArgs
{
    public string MachineId { get; }
    public string NewState { get; }
    public string? PreviousState { get; }
    public DateTime Timestamp { get; }

    public StateChangedEventArgs(string machineId, string newState, string? previousState, DateTime timestamp)
    {
        MachineId = machineId;
        NewState = newState;
        PreviousState = previousState;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Event arguments for errors
/// </summary>
public class ErrorOccurredEventArgs : EventArgs
{
    public string MachineId { get; }
    public Exception Exception { get; }
    public string? State { get; }
    public string? EventName { get; }
    public DateTime Timestamp { get; }

    public ErrorOccurredEventArgs(string machineId, Exception exception, string? state, string? eventName, DateTime timestamp)
    {
        MachineId = machineId;
        Exception = exception;
        State = state;
        EventName = eventName;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Event arguments for transitions
/// </summary>
public class TransitionEventArgs : EventArgs
{
    public string MachineId { get; }
    public string? FromState { get; }
    public string? ToState { get; }
    public string EventName { get; }
    public DateTime Timestamp { get; }

    public TransitionEventArgs(string machineId, string? fromState, string? toState, string eventName, DateTime timestamp)
    {
        MachineId = machineId;
        FromState = fromState;
        ToState = toState;
        EventName = eventName;
        Timestamp = timestamp;
    }
}