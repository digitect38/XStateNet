using System;

namespace XStateNet;

/// <summary>
/// Factory for creating thread-safe state machines
/// </summary>
public static class StateMachineFactory
{
    /// <summary>
    /// Create a thread-safe state machine from file
    /// </summary>
    public static StateMachine CreateThreadSafeFromFile(
        string jsonFilePath,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null)
    {
        var sm = StateMachine.CreateFromFile(jsonFilePath, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
        sm.EnableThreadSafety = true;
        return sm;
    }
    
    /// <summary>
    /// Create a thread-safe state machine from script
    /// </summary>
    public static StateMachine CreateThreadSafeFromScript(
        string? jsonScript,
        ActionMap? actionCallbacks = null,
        GuardMap? guardCallbacks = null,
        ServiceMap? serviceCallbacks = null,
        DelayMap? delayCallbacks = null)
    {
        var sm = StateMachine.CreateFromScript(jsonScript, actionCallbacks, guardCallbacks, serviceCallbacks, delayCallbacks);
        sm.EnableThreadSafety = true;
        return sm;
    }
}