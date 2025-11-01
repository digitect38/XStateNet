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
) : IStateMachineMessage
{
    /// <summary>
    /// XState V5: Metadata for all active state nodes
    /// Key: state node ID (full path), Value: meta data dictionary
    /// </summary>
    public Dictionary<string, Dictionary<string, object>>? Meta { get; init; }

    /// <summary>
    /// XState V5: All tags from active state nodes
    /// </summary>
    public List<string>? Tags { get; init; }

    /// <summary>
    /// XState V5: Output data if in a final state
    /// null if not in a final state
    /// </summary>
    public object? Output { get; init; }

    /// <summary>
    /// XState V5: Description of the current state node
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// XState V5: Status of the state machine
    /// "active" - running normally
    /// "done" - reached final state with output
    /// "error" - in error state
    /// "stopped" - machine is stopped
    /// </summary>
    public string Status => IsRunning
        ? (Output != null ? "done" : "active")
        : "stopped";
};

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

/// <summary>
/// Send event with region states (for evaluating in-state conditions in parallel states)
/// </summary>
public record SendEventWithRegionStates(
    SendEvent Event,
    Dictionary<string, string> RegionStates
) : IStateMachineMessage;

/// <summary>
/// Cross-region transition request (from region to parent)
/// Indicates that a region wants to transition to a state outside the parallel state
/// </summary>
public record CrossRegionTransition(
    string TargetState,
    SendEvent TriggeringEvent,
    List<object>? Actions
) : IStateMachineMessage;

/// <summary>
/// Direct transition command (from parent to region)
/// Forces a region to transition directly to a specific state
/// Used for multiple targets feature
/// </summary>
public record DirectTransition(
    string TargetState
) : IStateMachineMessage;
