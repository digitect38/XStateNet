using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Base test class for XStateNet2 tests that provides common helper methods
/// for waiting on state transitions and sending events.
/// </summary>
public abstract class XStateTestKit : TestKit
{
    /// <summary>
    /// Unique test ID for this test instance (automatically generated)
    /// Provides isolation between parallel test runs and prevents actor name conflicts
    /// </summary>
    protected string TestId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Generate a unique actor name for this test instance
    /// </summary>
    /// <param name="baseName">Base name for the actor</param>
    /// <returns>Unique actor name</returns>
    protected string UniqueActorName(string baseName) => $"{baseName}-{TestId}";
    /// <summary>
    /// Wait for a state machine to reach a specific condition with timeout and retry logic.
    /// Uses AwaitAssert for deterministic waiting without arbitrary delays.
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="condition">Condition to check on the state snapshot</param>
    /// <param name="description">Description of the condition for error messages</param>
    /// <param name="timeout">Maximum time to wait (default: 3 seconds)</param>
    /// <param name="interval">Check interval (default: 50ms)</param>
    protected void WaitForState(
        IActorRef machine,
        Func<StateSnapshot, bool> condition,
        string description = "state condition",
        TimeSpan? timeout = null,
        TimeSpan? interval = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(3);
        var askTimeout = effectiveTimeout; // Use full timeout for Ask to handle slow systems

        AwaitAssert(() =>
        {
            var snapshot = machine.Ask<StateSnapshot>(new GetState(), askTimeout).Result;
            Assert.True(condition(snapshot), $"Expected {description}, but got state: {snapshot.CurrentState}");
        },
        effectiveTimeout,
        interval ?? TimeSpan.FromMilliseconds(50));
    }

    /// <summary>
    /// Send an event to a state machine and wait for a specific state condition.
    /// Combines event sending with state waiting for cleaner test code.
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="eventType">Event type to send</param>
    /// <param name="stateCondition">Condition to check after event is processed</param>
    /// <param name="description">Description for error messages</param>
    /// <param name="eventData">Optional event data payload</param>
    /// <param name="timeout">Maximum time to wait (default: 3 seconds)</param>
    protected void SendEventAndWait(
        IActorRef machine,
        string eventType,
        Func<StateSnapshot, bool> stateCondition,
        string description = "state condition",
        object? eventData = null,
        TimeSpan? timeout = null)
    {
        machine.Tell(new SendEvent(eventType, eventData));
        WaitForState(machine, stateCondition, description, timeout);
    }

    /// <summary>
    /// Wait for a state machine to reach a specific state by name.
    /// Convenience method for the common case of waiting for a specific state.
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="stateName">Expected state name</param>
    /// <param name="timeout">Maximum time to wait (default: 3 seconds)</param>
    protected void WaitForStateName(
        IActorRef machine,
        string stateName,
        TimeSpan? timeout = null)
    {
        WaitForState(machine, s => s.CurrentState == stateName, $"state '{stateName}'", timeout);
    }

    /// <summary>
    /// Wait for a context value to reach a specific value.
    /// Useful for testing context updates and side effects.
    /// </summary>
    /// <param name="machine">The state machine actor reference</param>
    /// <param name="contextKey">Context key to check</param>
    /// <param name="expectedValue">Expected value</param>
    /// <param name="timeout">Maximum time to wait (default: 3 seconds)</param>
    protected void WaitForContextValue<T>(
        IActorRef machine,
        string contextKey,
        T expectedValue,
        TimeSpan? timeout = null)
    {
        WaitForState(machine, s =>
        {
            if (!s.Context.ContainsKey(contextKey))
                return false;

            var value = s.Context[contextKey];

            // Handle JsonElement conversion
            if (value is System.Text.Json.JsonElement element)
            {
                var converted = System.Text.Json.JsonSerializer.Deserialize<T>(element.GetRawText());
                return EqualityComparer<T>.Default.Equals(converted, expectedValue);
            }

            return EqualityComparer<T>.Default.Equals((T)value, expectedValue);
        },
        $"context[{contextKey}] == {expectedValue}",
        timeout);
    }
}
