using System;
using System.Collections.Generic;
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
    Dictionary<string, object?>? ContextMap { get; set; }

    /// <summary>
    /// Gets the root state of the state machine
    /// </summary>
    CompoundState? RootState { get; }

    /// <summary>
    /// Gets whether the state machine is running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the state machine
    /// </summary>
    IStateMachine Start();

    /// <summary>
    /// Stops the state machine
    /// </summary>
    void Stop();

    /// <summary>
    /// Sends an event to the state machine
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="eventData">Optional event data</param>
    void Send(string eventName, object? eventData = null);

    /// <summary>
    /// Sends an event asynchronously
    /// </summary>
    /// <param name="eventName">The event name</param>
    /// <param name="eventData">Optional event data</param>
    Task SendAsync(string eventName, object? eventData = null);

    /// <summary>
    /// Gets the active state as a string
    /// </summary>
    string GetActiveStateString();

    /// <summary>
    /// Gets all active states
    /// </summary>
    List<CompoundState> GetActiveStates();

    /// <summary>
    /// Checks if a state is active
    /// </summary>
    bool IsInState(string stateName);

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