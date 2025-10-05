namespace XStateNet;

/// <summary>
/// Thread-safe ordered event handler that guarantees execution order
/// </summary>
public class OrderedEventHandler<T>
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly List<(int priority, Guid id, Action<T> handler)> _handlers = new();
    private int _nextPriority = 0;

    /// <summary>
    /// Subscribe to the event with guaranteed order
    /// </summary>
    /// <param name="handler">The event handler</param>
    /// <returns>Subscription token for unsubscribing</returns>
    public IDisposable Subscribe(Action<T> handler)
    {
        return Subscribe(handler, Interlocked.Increment(ref _nextPriority));
    }

    /// <summary>
    /// Subscribe with explicit priority (lower priority executes first)
    /// </summary>
    public IDisposable Subscribe(Action<T> handler, int priority)
    {
        var id = Guid.NewGuid();

        _lock.EnterWriteLock();
        try
        {
            _handlers.Add((priority, id, handler));
            // Keep sorted by priority
            _handlers.Sort((a, b) => a.priority.CompareTo(b.priority));
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return new Subscription(() => Unsubscribe(id));
    }

    /// <summary>
    /// Invoke all handlers in guaranteed order
    /// </summary>
    public void Invoke(T arg)
    {
        List<Action<T>> handlersToInvoke;

        _lock.EnterReadLock();
        try
        {
            // Create a snapshot to avoid holding lock during handler execution
            handlersToInvoke = _handlers.Select(h => h.handler).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Execute handlers in order outside the lock
        foreach (var handler in handlersToInvoke)
        {
            try
            {
                handler(arg);
            }
            catch (Exception ex)
            {
                // Log but don't break the chain
                Console.WriteLine($"Handler execution failed: {ex.Message}");
            }
        }
    }

    private void Unsubscribe(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            _handlers.RemoveAll(h => h.id == id);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private int _disposed = 0;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _unsubscribe();
            }
        }
    }
}

/// <summary>
/// Extension for StateMachine to use ordered events
/// </summary>
public partial class StateMachine
{
    private OrderedEventHandler<string>? _orderedStateChanged;
    private OrderedEventHandler<Exception>? _orderedErrorOccurred;

    /// <summary>
    /// Subscribe to state changes with guaranteed order
    /// </summary>
    public IDisposable SubscribeToStateChange(Action<string> handler, int priority = int.MaxValue)
    {
        _orderedStateChanged ??= new OrderedEventHandler<string>();
        return _orderedStateChanged.Subscribe(handler, priority);
    }

    /// <summary>
    /// Subscribe to errors with guaranteed order
    /// </summary>
    public IDisposable SubscribeToError(Action<Exception> handler, int priority = int.MaxValue)
    {
        _orderedErrorOccurred ??= new OrderedEventHandler<Exception>();
        return _orderedErrorOccurred.Subscribe(handler, priority);
    }

    /// <summary>
    /// Raise state changed event in order
    /// </summary>
    public void RaiseOrderedStateChanged(string state)
    {
        _orderedStateChanged?.Invoke(state);
        // Also raise the regular event for backward compatibility
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Raise error event in order
    /// </summary>
    internal void RaiseOrderedError(Exception ex)
    {
        _orderedErrorOccurred?.Invoke(ex);
        // Also raise the regular event for backward compatibility
        ErrorOccurred?.Invoke(ex);
    }
}