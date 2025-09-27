using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet;

/// <summary>
/// Interface for state machine functionality
/// </summary>
public interface IStateMachine : IDisposable
{
    /// <summary>
    /// Gets the machine ID
    /// </summary>
    string machineId { get; }

    /// <summary>
    /// Gets or sets the context map for storing data
    /// </summary>
    ConcurrentDictionary<string, object?>? ContextMap { get; set; }

    /// <summary>
    /// Gets the root state of the state machine
    /// </summary>
    CompoundState? RootState { get; }

    /// <summary>
    /// Gets whether the state machine is running
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Starts the state machine (DEPRECATED - Use StartAsync instead)
    /// </summary>
    [Obsolete("Use StartAsync() instead. This synchronous method is deprecated and will be removed in the next major version.", error: false)]
    IStateMachine Start();
    
    /// <summary>
    /// Starts the state machine asynchronously and returns the initial state
    /// </summary>
    /// <returns>The initial state string after starting</returns>
    Task<string> StartAsync();

    /// <summary>
    /// Stops the state machine
    /// </summary>
    void Stop();

    /// <summary>
    /// Sends an event asynchronously
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="eventData">Optional event data</param>
    Task<string> SendAsync(string eventName, object? eventData = null);

    /// <summary>
    /// Gets the active state as a string
    /// </summary>
    string GetActiveStateNames(bool leafOnly = true, string separator = ";");

    /// <summary>
    /// Gets all active states
    /// </summary>
    List<CompoundState> GetActiveStates();

    /// <summary>
    /// Checks if a state is active
    /// </summary>
    bool IsInState(string stateName);

    /// <summary>
    /// Waits for the state machine to reach a specific state
    /// </summary>
    /// <param name="stateName">The state name to wait for (can be partial match)</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 5000ms)</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Task that completes when the state is reached</returns>
    /// <exception cref="TimeoutException">Thrown when the state is not reached within the timeout</exception>
    Task<string> WaitForStateAsync(string stateName, int timeoutMs = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for the state machine to reach a specific state and all associated actions to complete
    /// </summary>
    /// <param name="stateName">The state name to wait for (can be partial match)</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 5000ms)</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Task that completes when the state is reached and all actions have executed</returns>
    /// <exception cref="TimeoutException">Thrown when the state is not reached within the timeout</exception>
    Task<string> WaitForStateWithActionsAsync(string stateName, int timeoutMs = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets the service invoker
    /// </summary>
    ServiceInvoker? ServiceInvoker { get; set; }

    /// <summary>
    /// Event raised when state changes
    /// </summary>
    event Action<string>? StateChanged;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    event Action<Exception>? ErrorOccurred;
}

/// <summary>
/// Interface for state machine factory
/// </summary>
public interface IStateMachineFactory
{
    /// <summary>
    /// Creates a state machine from script
    /// </summary>
    IStateMachine CreateFromScript(
        string script,
        ActionMap? actions = null,
        GuardMap? guards = null,
        ServiceMap? services = null,
        DelayMap? delayCallbacks = null,
        ActivityMap? activityCallbacks = null
   );

    /// <summary>
    /// Creates a state machine from builder
    /// </summary>
    IStateMachine CreateFromBuilder(Action<StateMachineBuilder> builderAction);
}