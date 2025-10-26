using Akka.Actor;
using XStateNet2.Core.Messages;

namespace XStateNet2.Core.Extensions;

/// <summary>
/// Extension methods for XState state machines to simplify common operations
/// in production code. Provides synchronous and asynchronous waiting helpers.
/// </summary>
public static class XStateMachineExtensions
{
    /// <summary>
    /// Wait for a state machine to reach a specific condition (blocking).
    /// Useful for synchronous code that needs to wait for state transitions.
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="condition">Condition to check on the state snapshot</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="pollInterval">How often to check the condition</param>
    /// <returns>True if condition was met, false if timeout occurred</returns>
    public static bool WaitForState(
        this IActorRef machine,
        Func<StateSnapshot, bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500)).Result;
                if (condition(snapshot))
                    return true;
            }
            catch
            {
                // Ignore ask timeout, continue waiting
            }

            Thread.Sleep(interval);
        }

        return false;
    }

    /// <summary>
    /// Wait for a state machine to reach a specific condition (async).
    /// Non-blocking version for async code.
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="condition">Condition to check on the state snapshot</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="pollInterval">How often to check the condition</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if condition was met, false if timeout occurred</returns>
    public static async Task<bool> WaitForStateAsync(
        this IActorRef machine,
        Func<StateSnapshot, bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromMilliseconds(500));
                if (condition(snapshot))
                    return true;
            }
            catch
            {
                // Ignore ask timeout, continue waiting
            }

            await Task.Delay(interval, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Wait for a state machine to reach a specific state by name (blocking).
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="stateName">Expected state name</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <returns>True if state was reached, false if timeout occurred</returns>
    public static bool WaitForStateName(
        this IActorRef machine,
        string stateName,
        TimeSpan timeout)
    {
        return WaitForState(machine, s => s.CurrentState == stateName, timeout);
    }

    /// <summary>
    /// Wait for a state machine to reach a specific state by name (async).
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="stateName">Expected state name</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if state was reached, false if timeout occurred</returns>
    public static async Task<bool> WaitForStateNameAsync(
        this IActorRef machine,
        string stateName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await WaitForStateAsync(machine, s => s.CurrentState == stateName, timeout, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Send an event and wait for a specific condition (blocking).
    /// Combines event sending with state waiting for cleaner code.
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="eventType">Event type to send</param>
    /// <param name="condition">Condition to check after event is processed</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="eventData">Optional event data payload</param>
    /// <returns>True if condition was met, false if timeout occurred</returns>
    public static bool SendEventAndWait(
        this IActorRef machine,
        string eventType,
        Func<StateSnapshot, bool> condition,
        TimeSpan timeout,
        object? eventData = null)
    {
        machine.Tell(new SendEvent(eventType, eventData));
        return WaitForState(machine, condition, timeout);
    }

    /// <summary>
    /// Send an event and wait for a specific condition (async).
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="eventType">Event type to send</param>
    /// <param name="condition">Condition to check after event is processed</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="eventData">Optional event data payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if condition was met, false if timeout occurred</returns>
    public static async Task<bool> SendEventAndWaitAsync(
        this IActorRef machine,
        string eventType,
        Func<StateSnapshot, bool> condition,
        TimeSpan timeout,
        object? eventData = null,
        CancellationToken cancellationToken = default)
    {
        machine.Tell(new SendEvent(eventType, eventData));
        return await WaitForStateAsync(machine, condition, timeout, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the current state snapshot from the state machine (blocking).
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="timeout">Maximum time to wait for response</param>
    /// <returns>Current state snapshot</returns>
    public static StateSnapshot GetStateSnapshot(
        this IActorRef machine,
        TimeSpan? timeout = null)
    {
        return machine.Ask<StateSnapshot>(new GetState(), timeout ?? TimeSpan.FromSeconds(1)).Result;
    }

    /// <summary>
    /// Get the current state snapshot from the state machine (async).
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="timeout">Maximum time to wait for response</param>
    /// <returns>Current state snapshot</returns>
    public static async Task<StateSnapshot> GetStateSnapshotAsync(
        this IActorRef machine,
        TimeSpan? timeout = null)
    {
        return await machine.Ask<StateSnapshot>(new GetState(), timeout ?? TimeSpan.FromSeconds(1));
    }
}
