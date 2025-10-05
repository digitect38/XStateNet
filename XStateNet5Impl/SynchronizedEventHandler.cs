namespace XStateNet;

/// <summary>
/// Thread-safe event handler that guarantees execution order and prevents concurrent execution
/// </summary>
/// <typeparam name="T">The type of event argument</typeparam>
public class ThreadSafeEventHandler<T> : IDisposable
{
    private readonly ReaderWriterLockSlim _subscriptionLock = new(LockRecursionPolicy.NoRecursion);
    private readonly object _executionLock = new();
    private readonly List<HandlerEntry> _handlers = new();
    private readonly Dictionary<Guid, HandlerEntry> _handlerLookup = new();
    private int _isDisposed = 0;

    protected class HandlerEntry
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int Priority { get; }
        public Action<T> Handler { get; }
        public string? Name { get; }
        public DateTime SubscribedAt { get; }

        public HandlerEntry(Action<T> handler, int priority, string? name = null)
        {
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Priority = priority;
            Name = name;
            SubscribedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Subscribe a handler with automatic priority (FIFO order)
    /// </summary>
    /// <param name="handler">The event handler to subscribe</param>
    /// <param name="name">Optional name for debugging</param>
    /// <returns>Disposable subscription token</returns>
    public IDisposable Subscribe(Action<T> handler, string? name = null)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        ThrowIfDisposed();

        // Use timestamp-based priority for FIFO ordering
        var priority = Environment.TickCount;
        return SubscribeWithPriority(handler, priority, name);
    }

    /// <summary>
    /// Subscribe a handler with explicit priority (lower values execute first)
    /// </summary>
    /// <param name="handler">The event handler to subscribe</param>
    /// <param name="priority">Execution priority (lower = earlier)</param>
    /// <param name="name">Optional name for debugging</param>
    /// <returns>Disposable subscription token</returns>
    public IDisposable SubscribeWithPriority(Action<T> handler, int priority, string? name = null)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        ThrowIfDisposed();

        var entry = new HandlerEntry(handler, priority, name);

        _subscriptionLock.EnterWriteLock();
        try
        {
            _handlers.Add(entry);
            _handlerLookup[entry.Id] = entry;

            // Keep sorted by priority for consistent execution order
            _handlers.Sort((a, b) =>
            {
                var priorityCompare = a.Priority.CompareTo(b.Priority);
                // If same priority, maintain subscription order
                return priorityCompare != 0 ? priorityCompare :
                    a.SubscribedAt.CompareTo(b.SubscribedAt);
            });
        }
        finally
        {
            _subscriptionLock.ExitWriteLock();
        }

        return new Subscription(this, entry.Id);
    }

    /// <summary>
    /// Invoke all handlers synchronously in priority order
    /// </summary>
    /// <param name="arg">The event argument</param>
    public void Invoke(T arg)
    {
        ThrowIfDisposed();

        // Get snapshot of handlers under read lock
        HandlerEntry[] handlersSnapshot;
        _subscriptionLock.EnterReadLock();
        try
        {
            handlersSnapshot = _handlers.ToArray();
        }
        finally
        {
            _subscriptionLock.ExitReadLock();
        }

        // Execute all handlers under execution lock to prevent concurrent execution
        lock (_executionLock)
        {
            foreach (var entry in handlersSnapshot)
            {
                try
                {
                    entry.Handler(arg);
                }
                catch (Exception ex)
                {
                    // Log error but continue executing other handlers
                    OnHandlerError(entry, arg, ex);
                }
            }
        }
    }

    /// <summary>
    /// Invoke all handlers and wait for completion
    /// </summary>
    /// <param name="arg">The event argument</param>
    /// <param name="timeout">Maximum time to wait for execution</param>
    /// <returns>True if completed within timeout, false otherwise</returns>
    public bool InvokeWithTimeout(T arg, TimeSpan timeout)
    {
        ThrowIfDisposed();

        var completed = false;
        var thread = new Thread(() =>
        {
            Invoke(arg);
            completed = true;
        })
        {
            IsBackground = true,
            Name = $"ThreadSafeEventHandler<{typeof(T).Name}>"
        };

        thread.Start();

        if (thread.Join(timeout))
        {
            return completed;
        }

        // Timeout occurred - abort the thread (not recommended but sometimes necessary)
        thread.Interrupt();
        return false;
    }

    /// <summary>
    /// Try to invoke handlers - returns false if already executing
    /// </summary>
    public bool TryInvoke(T arg)
    {
        ThrowIfDisposed();

        if (!Monitor.TryEnter(_executionLock))
        {
            return false;
        }

        try
        {
            HandlerEntry[] handlersSnapshot;
            _subscriptionLock.EnterReadLock();
            try
            {
                handlersSnapshot = _handlers.ToArray();
            }
            finally
            {
                _subscriptionLock.ExitReadLock();
            }

            foreach (var entry in handlersSnapshot)
            {
                try
                {
                    entry.Handler(arg);
                }
                catch (Exception ex)
                {
                    OnHandlerError(entry, arg, ex);
                }
            }

            return true;
        }
        finally
        {
            Monitor.Exit(_executionLock);
        }
    }

    /// <summary>
    /// Get the current number of subscribed handlers
    /// </summary>
    public int HandlerCount
    {
        get
        {
            _subscriptionLock.EnterReadLock();
            try
            {
                return _handlers.Count;
            }
            finally
            {
                _subscriptionLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Check if there are any handlers subscribed
    /// </summary>
    public bool HasHandlers
    {
        get
        {
            _subscriptionLock.EnterReadLock();
            try
            {
                return _handlers.Count > 0;
            }
            finally
            {
                _subscriptionLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Clear all subscriptions
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();

        _subscriptionLock.EnterWriteLock();
        try
        {
            _handlers.Clear();
            _handlerLookup.Clear();
        }
        finally
        {
            _subscriptionLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Event raised when a handler throws an exception
    /// </summary>
    public event EventHandler<HandlerErrorEventArgs>? HandlerError;

    protected virtual void OnHandlerError(HandlerEntry entry, T arg, Exception exception)
    {
        var args = new HandlerErrorEventArgs(entry.Name ?? "Unknown", exception, arg);
        HandlerError?.Invoke(this, args);

        // Also log to console for debugging
        Console.WriteLine($"[ThreadSafeEventHandler] Handler '{entry.Name}' error: {exception.Message}");
    }

    private void Unsubscribe(Guid id)
    {
        _subscriptionLock.EnterWriteLock();
        try
        {
            if (_handlerLookup.TryGetValue(id, out var entry))
            {
                _handlers.Remove(entry);
                _handlerLookup.Remove(id);
            }
        }
        finally
        {
            _subscriptionLock.ExitWriteLock();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0)
        {
            throw new ObjectDisposedException(nameof(ThreadSafeEventHandler<T>));
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
        {
            // Clear handlers without checking if disposed since we're in Dispose
            _subscriptionLock.EnterWriteLock();
            try
            {
                _handlers.Clear();
                _handlerLookup.Clear();
            }
            finally
            {
                _subscriptionLock.ExitWriteLock();
            }

            _subscriptionLock.Dispose();
        }
    }

    /// <summary>
    /// Subscription token for unsubscribing
    /// </summary>
    private class Subscription : IDisposable
    {
        private readonly ThreadSafeEventHandler<T> _parent;
        private readonly Guid _id;
        private int _isDisposed = 0;

        public Subscription(ThreadSafeEventHandler<T> parent, Guid id)
        {
            _parent = parent;
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
            {
                _parent.Unsubscribe(_id);
            }
        }
    }

    /// <summary>
    /// Event arguments for handler errors
    /// </summary>
    public class HandlerErrorEventArgs : EventArgs
    {
        public string HandlerName { get; }
        public Exception Exception { get; }
        public T? EventArgument { get; }

        public HandlerErrorEventArgs(string handlerName, Exception exception, T? eventArgument)
        {
            HandlerName = handlerName;
            Exception = exception;
            EventArgument = eventArgument;
        }
    }
}

/// <summary>
/// Extension methods for easier integration
/// </summary>
public static class ThreadSafeEventHandlerExtensions
{
    /// <summary>
    /// Convert a regular event to synchronized event
    /// </summary>
    public static ThreadSafeEventHandler<T> ToSynchronized<T>(this Action<T>? multicastDelegate)
    {
        var synchronized = new ThreadSafeEventHandler<T>();

        if (multicastDelegate != null)
        {
            foreach (Action<T> handler in multicastDelegate.GetInvocationList())
            {
                synchronized.Subscribe(handler);
            }
        }

        return synchronized;
    }
}