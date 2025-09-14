using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XStateNet;

/// <summary>
/// Error transition for handling errors in state machines
/// </summary>
public class OnErrorTransition : Transition
{
    public string? ErrorType { get; set; }
    
    public OnErrorTransition(string? machineId) : base(machineId) { }
}

/// <summary>
/// Error context for state machine errors
/// </summary>
public class StateErrorContext
{
    public Exception Exception { get; set; }
    public string? SourceState { get; set; }
    public string? EventName { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; }
    
    public StateErrorContext(Exception exception, string? sourceState = null, string? eventName = null)
    {
        Exception = exception;
        SourceState = sourceState;
        EventName = eventName;
        Timestamp = DateTime.UtcNow;
        AdditionalData = new Dictionary<string, object>();
    }
}

/// <summary>
/// Error handler for state machines
/// </summary>
public class ErrorHandler : StateObject
{
    private readonly Stack<StateErrorContext> _errorStack = new();
    private readonly List<StateErrorContext> _errorHistory = new();
    
    public ErrorHandler(string? machineId) : base(machineId) { }
    
    /// <summary>
    /// Handles an error in the state machine
    /// </summary>
    public void HandleError(Exception exception, CompoundState? currentState, string? eventName = null)
    {
        if (StateMachine == null) return;
        
        var errorContext = new StateErrorContext(exception, currentState?.Name, eventName);
        _errorStack.Push(errorContext);
        _errorHistory.Add(errorContext);
        
        Logger.Error($"Error in state '{currentState?.Name}': {exception.Message}");
        
        // Store error context in machine context
        if (StateMachine.ContextMap != null)
        {
            StateMachine.ContextMap["_lastError"] = errorContext;
            StateMachine.ContextMap["_errorMessage"] = exception.Message;
            StateMachine.ContextMap["_errorType"] = exception.GetType().Name;
        }
        
        // Try to find and execute onError transition
        var handled = TryHandleErrorTransition(currentState, exception);
        
        if (!handled)
        {
            // If no specific error handler, try generic onError event
            StateMachine.Send("onError");
        }
    }
    
    /// <summary>
    /// Tries to handle error with onError transition
    /// </summary>
    private bool TryHandleErrorTransition(CompoundState? currentState, Exception exception)
    {
        if (currentState == null || StateMachine == null) return false;
        
        // Look for onError transitions in current state's OnTransitionMap
        List<OnErrorTransition>? errorTransitions = null;
        
        if (currentState.OnTransitionMap != null && currentState.OnTransitionMap.ContainsKey("onError"))
        {
            var transitions = currentState.OnTransitionMap["onError"];
            errorTransitions = transitions?
                .OfType<OnErrorTransition>()
                .Where(t => t.ErrorType == null || t.ErrorType == exception.GetType().Name)
                .ToList();
        }
        
        if (errorTransitions == null || errorTransitions.Count == 0)
        {
            // Try parent state
            if (currentState.Parent != null)
            {
                return TryHandleErrorTransition(currentState.Parent, exception);
            }
            return false;
        }
        
        // Execute first matching error transition
        var transition = errorTransitions.First();
        bool guardPassed = transition.Guard == null || transition.Guard.PredicateFunc(StateMachine);
        if (transition.Guard != null)
        {
            // Notify guard evaluation
            StateMachine.RaiseGuardEvaluated(transition.Guard.Name, guardPassed);
        }

        if (guardPassed)
        {
            var executor = new TransitionExecutor(StateMachine.machineId);
            executor.Execute(transition, $"error:{exception.GetType().Name}");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Gets the last error
    /// </summary>
    public StateErrorContext? GetLastError()
    {
        return _errorStack.Count > 0 ? _errorStack.Peek() : null;
    }
    
    /// <summary>
    /// Clears the error stack
    /// </summary>
    public void ClearErrors()
    {
        _errorStack.Clear();
    }
    
    /// <summary>
    /// Gets error history
    /// </summary>
    public IReadOnlyList<StateErrorContext> GetErrorHistory()
    {
        return _errorHistory.AsReadOnly();
    }
    
    /// <summary>
    /// Wraps an action with error handling
    /// </summary>
    public async Task ExecuteWithErrorHandling(Func<Task> action, CompoundState? currentState, string? eventName = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            HandleError(ex, currentState, eventName);
        }
    }
    
    /// <summary>
    /// Wraps an action with error handling (synchronous)
    /// </summary>
    public void ExecuteWithErrorHandling(Action action, CompoundState? currentState, string? eventName = null)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            HandleError(ex, currentState, eventName);
        }
    }
}