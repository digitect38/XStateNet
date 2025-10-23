namespace XStateNet2.Core.Messages;

/// <summary>
/// Base message interface for all state machine messages
/// </summary>
public interface IStateMachineMessage { }

/// <summary>
/// Start the state machine
/// </summary>
public record StartMachine : IStateMachineMessage;

/// <summary>
/// Stop the state machine
/// </summary>
public record StopMachine : IStateMachineMessage;

/// <summary>
/// Send an event to the state machine
/// </summary>
public record SendEvent(string Type, object? Data = null) : IStateMachineMessage;

/// <summary>
/// Get current state snapshot
/// </summary>
public record GetState : IStateMachineMessage;

/// <summary>
/// State snapshot response
/// </summary>
public record StateSnapshot(
    string CurrentState,
    Dictionary<string, object> Context,
    bool IsRunning
) : IStateMachineMessage;

/// <summary>
/// State transition notification
/// </summary>
public record StateChanged(
    string PreviousState,
    string CurrentState,
    SendEvent? TriggeringEvent
) : IStateMachineMessage;

/// <summary>
/// Service invocation result (success)
/// </summary>
public record ServiceDone(string ServiceId, object? Data) : IStateMachineMessage;

/// <summary>
/// Service invocation result (error)
/// </summary>
public record ServiceError(string ServiceId, Exception Error) : IStateMachineMessage;

/// <summary>
/// Subscribe to state changes
/// </summary>
public record Subscribe : IStateMachineMessage;

/// <summary>
/// Unsubscribe from state changes
/// </summary>
public record Unsubscribe : IStateMachineMessage;

/// <summary>
/// Internal message for delayed transitions (after)
/// </summary>
public record DelayedTransition(int Delay, string? Target) : IStateMachineMessage;

/// <summary>
/// Region completed (for parallel states)
/// </summary>
public record RegionCompleted(string RegionId) : IStateMachineMessage;

/// <summary>
/// Broadcast event to all regions (for parallel states)
/// </summary>
public record BroadcastEvent(SendEvent Event) : IStateMachineMessage;

/// <summary>
/// Region state changed notification (for parallel states)
/// </summary>
public record RegionStateChanged(string RegionId, string State) : IStateMachineMessage;
