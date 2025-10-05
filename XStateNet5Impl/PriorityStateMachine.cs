using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XStateNet
{
    /// <summary>
    /// Priority levels for state machine events
    /// </summary>
    public enum EventPriority
    {
        Critical = 0,    // State transitions, error events
        High = 1,        // User-initiated events
        Normal = 2,      // Regular events
        Low = 3,         // Background events
        Deferred = 4     // Can be delayed
    }

    /// <summary>
    /// Configuration for timing-sensitive state transitions
    /// </summary>
    public class TimingSensitiveConfig
    {
        /// <summary>
        /// States that are timing-sensitive and should be prioritized
        /// </summary>
        public HashSet<string> CriticalStates { get; set; } = new();

        /// <summary>
        /// Events that trigger timing-sensitive transitions
        /// </summary>
        public HashSet<string> CriticalEvents { get; set; } = new();

        /// <summary>
        /// State transitions that are timing-sensitive (from -> to)
        /// </summary>
        public HashSet<(string from, string to)> CriticalTransitions { get; set; } = new();

        /// <summary>
        /// Maximum delay for critical state changes (ms)
        /// </summary>
        public int MaxCriticalDelay { get; set; } = 10;

        /// <summary>
        /// Maximum delay for high priority events (ms)
        /// </summary>
        public int MaxHighPriorityDelay { get; set; } = 50;
    }

    /// <summary>
    /// Priority-aware state machine wrapper
    /// </summary>
    public class PriorityStateMachine : IStateMachine, IDisposable
    {
        protected readonly IStateMachine _innerMachine;
        protected readonly TimingSensitiveConfig _config;
        private readonly Channel<PriorityEvent>[] _priorityChannels;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;
        private readonly SemaphoreSlim _criticalSemaphore;
        private string _currentState;
        private readonly object _stateLock = new();

        public event Action<string, string> StateChangedDetailed; // oldState, newState
        public event EventHandler<PriorityEventProcessedArgs> EventProcessed;

        // IStateMachine events
        public event Action<string>? StateChanged;
        public event Action<Exception>? ErrorOccurred;

        public PriorityStateMachine(
            IStateMachine innerMachine,
            TimingSensitiveConfig config = null)
        {
            _innerMachine = innerMachine ?? throw new ArgumentNullException(nameof(innerMachine));
            _config = config ?? new TimingSensitiveConfig();
            _cancellationTokenSource = new CancellationTokenSource();
            _criticalSemaphore = new SemaphoreSlim(1, 1);

            // Create priority channels
            var priorityCount = Enum.GetValues<EventPriority>().Length;
            _priorityChannels = new Channel<PriorityEvent>[priorityCount];

            for (int i = 0; i < priorityCount; i++)
            {
                _priorityChannels[i] = Channel.CreateUnbounded<PriorityEvent>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false
                    });
            }

            // Subscribe to inner machine state changes if it's a StateMachine
            if (_innerMachine is StateMachine sm)
            {
                sm.OnTransition += OnInnerTransition;
                // Initialize current state
                _currentState = sm.GetActiveStateNames();
            }

            // Subscribe to IStateMachine events
            _innerMachine.StateChanged += (newState) => StateChanged?.Invoke(newState);
            _innerMachine.ErrorOccurred += (ex) => ErrorOccurred?.Invoke(ex);

            // Start processing
            _processingTask = ProcessEventsAsync(_cancellationTokenSource.Token);
        }

        public string CurrentState
        {
            get
            {
                lock (_stateLock)
                {
                    if (_currentState != null)
                    {
                        return _currentState;
                    }

                    if (_innerMachine is StateMachine sm)
                    {
                        return sm.GetActiveStateNames();
                    }

                    return "unknown";
                }
            }
        }

        // IStateMachine implementation
        public string machineId => _innerMachine.machineId;
        public ConcurrentDictionary<string, object?>? ContextMap
        {
            get => _innerMachine.ContextMap;
            set => _innerMachine.ContextMap = value;
        }
        public CompoundState? RootState => _innerMachine.RootState;
        public bool IsRunning => _innerMachine.IsRunning;
        public ServiceInvoker? ServiceInvoker
        {
            get => _innerMachine.ServiceInvoker;
            set => _innerMachine.ServiceInvoker = value;
        }

        public Task<string> StartAsync() => _innerMachine.StartAsync();
        public void Stop() => _innerMachine.Stop();
        /*        
        public Task<string> SendAsync(string eventName, object? eventData = null)
        {
            return _innerMachine.SendAsync(eventName, eventData);
        }
        */
        public string GetActiveStateNames(bool leafOnly = true, string separator = ";")
            => _innerMachine.GetActiveStateNames(leafOnly, separator);


        public List<CompoundState> GetActiveStates() => _innerMachine.GetActiveStates();
        public bool IsInState(string stateName) => _innerMachine.IsInState(stateName);
        public Task<string> WaitForStateAsync(string stateName, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            return _innerMachine.WaitForStateAsync(stateName, timeoutMs, cancellationToken);
        }

        public Task<string> WaitForStateWithActionsAsync(string stateName, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            return _innerMachine.WaitForStateWithActionsAsync(stateName, timeoutMs, cancellationToken);
        }

        /// <summary>
        /// Send event with automatic priority detection
        /// </summary>
        public async Task<string> SendAsync(string eventName, object? data = null)
        {
            var priority = DeterminePriority(eventName, CurrentState);
            return await SendWithPriorityAsync(eventName, data, priority);
        }

        /// <summary>
        /// Send event in a fire-and-forget manner
        /// </summary>
        public void SendAndForget(string eventName, object? eventData = null)
        {
            try
            {
                var priority = DeterminePriority(eventName, CurrentState);
                var priorityEvent = new PriorityEvent(eventName, eventData, priority, DateTime.UtcNow);
                var channel = _priorityChannels[(int)priority];

                if (!channel.Writer.TryWrite(priorityEvent))
                {
                    // If queue is full, log error but don't throw
                    ErrorOccurred?.Invoke(new InvalidOperationException($"Failed to queue event {eventName} in fire-and-forget mode"));
                }

                // For critical events, signal immediate processing
                if (priority == EventPriority.Critical)
                {
                    _ = Task.Run(async () =>
                    {
                        await _criticalSemaphore.WaitAsync(0).ConfigureAwait(false);
                        _criticalSemaphore.Release();
                    });
                }
            }
            catch (Exception ex)
            {
                // Log the exception but don't throw since this is fire-and-forget
                ErrorOccurred?.Invoke(ex);
            }
        }

        /// <summary>
        /// Send event with explicit priority
        /// </summary>
        public async Task<string> SendWithPriorityAsync(
            string eventName,
            object data = null,
            EventPriority priority = EventPriority.Normal)
        {
            var priorityEvent = new PriorityEvent(eventName, data, priority, DateTime.UtcNow);
            var channel = _priorityChannels[(int)priority];

            if (!channel.Writer.TryWrite(priorityEvent))
            {
                throw new InvalidOperationException($"Failed to queue event {eventName}");
            }

            // For critical events, signal immediate processing
            if (priority == EventPriority.Critical)
            {
                await _criticalSemaphore.WaitAsync(0);
                _criticalSemaphore.Release();
            }

            return GetActiveStateNames();
        }

        /// <summary>
        /// Send multiple events as a batch with same priority
        /// </summary>
        public async Task SendBatchAsync(
            IEnumerable<(string eventName, object data)> events,
            EventPriority priority = EventPriority.Normal)
        {
            var tasks = events.Select(e =>
                SendWithPriorityAsync(e.eventName, e.data, priority));
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Execute a critical state transition immediately
        /// </summary>
        public async Task<bool> ExecuteCriticalTransitionAsync(
            string eventName,
            object data = null,
            TimeSpan? timeout = null)
        {
            var actualTimeout = timeout ?? TimeSpan.FromMilliseconds(_config.MaxCriticalDelay);
            var tcs = new TaskCompletionSource<bool>();

            Action<string, string> handler = (oldState, newState) => tcs.TrySetResult(true);
            StateChangedDetailed += handler;

            try
            {
                await SendWithPriorityAsync(eventName, data, EventPriority.Critical);

                using var cts = new CancellationTokenSource(actualTimeout);
                using (cts.Token.Register(() => tcs.TrySetResult(false)))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                StateChangedDetailed -= handler;
            }
        }

        private EventPriority DeterminePriority(string eventName, string currentState)
        {
            // Check if event is critical
            if (_config.CriticalEvents.Contains(eventName))
            {
                return EventPriority.Critical;
            }

            // Check if current state is critical
            if (_config.CriticalStates.Contains(currentState))
            {
                return EventPriority.Critical;
            }

            // Check for error/timeout events
            if (IsTimingSensitiveEvent(eventName))
            {
                return EventPriority.Critical;
            }

            // Default priority based on event pattern
            if (eventName.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                eventName.StartsWith("TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
                eventName.StartsWith("RESET", StringComparison.OrdinalIgnoreCase))
            {
                return EventPriority.High;
            }

            return EventPriority.Normal;
        }

        private bool IsTimingSensitiveEvent(string eventName)
        {
            return eventName.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
                   eventName.Contains("EXPIRE", StringComparison.OrdinalIgnoreCase) ||
                   eventName.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase) ||
                   eventName.Contains("EMERGENCY", StringComparison.OrdinalIgnoreCase) ||
                   eventName.Contains("HALFOPEN", StringComparison.OrdinalIgnoreCase) ||
                   eventName.Contains("CIRCUITBREAK", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ProcessEventsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    PriorityEvent priorityEvent = null;
                    var delay = _config.MaxHighPriorityDelay;

                    // Check channels in priority order
                    for (int priority = 0; priority < _priorityChannels.Length; priority++)
                    {
                        if (_priorityChannels[priority].Reader.TryRead(out priorityEvent))
                        {
                            if (priority == (int)EventPriority.Critical)
                            {
                                delay = 0; // No delay for critical
                            }
                            else if (priority == (int)EventPriority.High)
                            {
                                delay = Math.Min(delay, _config.MaxHighPriorityDelay / 2);
                            }
                            break;
                        }
                    }

                    if (priorityEvent == null)
                    {
                        // Wait for any channel to have data using WaitToReadAsync
                        var readTasks = _priorityChannels.Select(c => c.Reader.WaitToReadAsync(cancellationToken).AsTask()).ToArray();

                        try
                        {
                            await Task.WhenAny(readTasks);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        continue;
                    }

                    // Apply delay if needed (skip delay for Critical priority)
                    if (delay > 0 && priorityEvent.Priority != EventPriority.Critical)
                    {
                        var age = (DateTime.UtcNow - priorityEvent.QueuedTime).TotalMilliseconds;
                        if (age < delay)
                        {
                            await Task.Delay(Math.Max(1, delay - (int)age), cancellationToken);
                        }
                    }

                    // Process the event
                    await ProcessEventAsync(priorityEvent, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                // Log error or handle appropriately
                ErrorOccurred?.Invoke(ex);
            }
        }

        private async Task ProcessEventAsync(PriorityEvent priorityEvent, CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // Send to inner machine
                await _innerMachine.SendAsync(priorityEvent.EventName, priorityEvent.Data);

                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                EventProcessed?.Invoke(this, new PriorityEventProcessedArgs
                {
                    EventName = priorityEvent.EventName,
                    Priority = priorityEvent.Priority,
                    QueueTime = (startTime - priorityEvent.QueuedTime).TotalMilliseconds,
                    ProcessTime = duration
                });
            }
            catch
            {
                throw;
            }
        }

        private void OnInnerTransition(CompoundState? fromState, StateNode? toState, string eventName)
        {
            string oldStateString = fromState?.Name ?? "";
            string newStateString = toState?.Name ?? "";


            lock (_stateLock)
            {
                var oldState = _currentState;
                _currentState = newStateString;

                // Check if this is a critical transition
                if (_config.CriticalTransitions.Contains((oldState, newStateString)))
                {
                    // Critical state transition detected
                }
            }

            StateChangedDetailed?.Invoke(oldStateString, newStateString);
            StateChanged?.Invoke(newStateString);
        }

        public int GetQueueDepth(EventPriority priority)
        {
            return _priorityChannels[(int)priority].Reader.Count;
        }

        public Dictionary<EventPriority, int> GetAllQueueDepths()
        {
            return Enum.GetValues<EventPriority>()
                .ToDictionary(p => p, p => GetQueueDepth(p));
        }

        public void Dispose()
        {
            // Signal cancellation
            _cancellationTokenSource?.Cancel();

            // Complete all channels to unblock any waiting readers
            foreach (var channel in _priorityChannels ?? Array.Empty<Channel<PriorityEvent>>())
            {
                channel.Writer.TryComplete();
            }

            // Wait for processing task to complete with longer timeout
            try
            {
                _processingTask?.Wait(TimeSpan.FromMilliseconds(1000));
            }
            catch { }

            // Cleanup event handlers before disposing
            if (_innerMachine is StateMachine sm)
            {
                sm.OnTransition -= OnInnerTransition;
            }

            // Stop the inner machine first to prevent new operations
            try
            {
                _innerMachine?.Stop();
            }
            catch { }

            // Give time for any ongoing operations to complete
            System.Threading.Thread.Sleep(50);

            // Dispose resources
            _criticalSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();

            // Dispose inner machine if it's disposable
            if (_innerMachine is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch { }
            }
        }

        private class PriorityEvent
        {
            public string EventName { get; }
            public object Data { get; }
            public EventPriority Priority { get; }
            public DateTime QueuedTime { get; }

            public PriorityEvent(string eventName, object data, EventPriority priority, DateTime queuedTime)
            {
                EventName = eventName;
                Data = data;
                Priority = priority;
                QueuedTime = queuedTime;
            }
        }
    }

    public class PriorityEventProcessedArgs : EventArgs
    {
        public string EventName { get; set; }
        public EventPriority Priority { get; set; }
        public double QueueTime { get; set; }
        public double ProcessTime { get; set; }
    }
}
