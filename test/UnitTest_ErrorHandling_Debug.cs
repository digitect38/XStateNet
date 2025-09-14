using Xunit;

using System;
using System.Collections.Generic;
using XStateNet;
namespace XStateV5Features;
public class UnitTest_ErrorHandling_Debug : IDisposable
{
    private StateMachine? _stateMachine;
    private ActionMap _actions;
    private GuardMap _guards;
    private bool _errorHandled;
    private string? _errorMessage;
    private string? _errorType;
    private List<string> _actionLog;
    
    public UnitTest_ErrorHandling_Debug()
    {
        _errorHandled = false;
        _errorMessage = null;
        _errorType = null;
        _actionLog = new List<string>();
        
        _actions = new ActionMap
        {
            ["throwError"] = new () { new ("throwError", (sm) => {
                _actionLog.Add("throwError");
                Console.WriteLine("About to throw error");
                throw new InvalidOperationException("Test error");
            }) },
            ["handleError"] = new () { new ("handleError", (sm) => {
                _actionLog.Add("handleError");
                _errorHandled = true;
                _errorMessage = sm.ContextMap?["_errorMessage"]?.ToString();
                _errorType = sm.ContextMap?["_errorType"]?.ToString();
                Console.WriteLine($"Error handled: {_errorMessage}");
            }) }
        };
        
        _guards = new GuardMap();
    }
    
    [Fact]
    public void TestErrorHandlingDebug()
    {
        const string script = @"
        {
            'id': 'errorTest',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'entry': 'throwError',
                    'on': {
                        'onError': {
                            'target': 'error',
                            'actions': 'handleError'
                        }
                    }
                },
                'error': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine!.Start();
        
        Console.WriteLine($"Initial state: {_stateMachine.GetActiveStateString()}");
        Assert.Contains("idle", _stateMachine.GetActiveStateString());
        
        try
        {
            _stateMachine!.Send("START");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception caught: {ex.Message}");
        }
        
        Console.WriteLine($"State after START: {_stateMachine.GetActiveStateString()}");
        
        // Check if error was stored in context
        if (_stateMachine.ContextMap != null)
        {
            Console.WriteLine($"Context has _error: {_stateMachine.ContextMap.ContainsKey("_error")}");
            Console.WriteLine($"Context has _errorType: {_stateMachine.ContextMap.ContainsKey("_errorType")}");
            Console.WriteLine($"Context has _errorMessage: {_stateMachine.ContextMap.ContainsKey("_errorMessage")}");
            
            if (_stateMachine.ContextMap.ContainsKey("_errorMessage"))
            {
                Console.WriteLine($"Error message in context: {_stateMachine.ContextMap["_errorMessage"]}");
            }
        }
        
        // Try manually sending onError
        Console.WriteLine("Manually sending onError event");
        _stateMachine!.Send("onError");
        
        Console.WriteLine($"State after onError: {_stateMachine.GetActiveStateString()}");
        Console.WriteLine($"Error handled: {_errorHandled}");
        Console.WriteLine($"Action log: {string.Join(", ", _actionLog)}");
        
        // The error should be caught and handled
        Assert.Contains("error", _stateMachine.GetActiveStateString());
        Assert.True(_errorHandled);
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

