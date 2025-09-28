using Xunit;

using System;
using System.Collections.Generic;
using XStateNet;

namespace XSateV5_Test.AdvancedFeatures;

public class UnitTest_ErrorHandling : IDisposable
{
    private StateMachine? _stateMachine;
    private ActionMap _actions;
    private GuardMap _guards;
    private bool _errorHandled;
    private string? _errorMessage;
    private string? _errorType;
    private List<string> _actionLog;
    
    public UnitTest_ErrorHandling()
    {
        _errorHandled = false;
        _errorMessage = null;
        _errorType = null;
        _actionLog = new List<string>();
        
        _actions = new ActionMap
        {
            ["throwError"] = new List<NamedAction> { new NamedAction("throwError", (sm) => {
                _actionLog.Add("throwError");
                throw new InvalidOperationException("Test error");
            }) },
            ["throwCustomError"] = new List<NamedAction> { new NamedAction("throwCustomError", (sm) => {
                _actionLog.Add("throwCustomError");
                throw new ArgumentException("Custom test error");
            }) },
            ["handleError"] = new List<NamedAction> { new NamedAction("handleError", (sm) => {
                _actionLog.Add("handleError");
                _errorHandled = true;
                _errorMessage = sm.ContextMap?["_errorMessage"]?.ToString();
                _errorType = sm.ContextMap?["_errorType"]?.ToString();
            }) },
            ["logAction"] = new List<NamedAction> { new NamedAction("logAction", (sm) => {
                _actionLog.Add("logAction");
            }) },
            ["recover"] = new List<NamedAction> { new NamedAction("recover", (sm) => {
                _actionLog.Add("recover");
                sm.ContextMap!["recovered"] = true;
            }) }
        };
        
        _guards = new GuardMap
        {
            ["isRecoverable"] = new NamedGuard("isRecoverable", (sm) => {
                var errorType = sm.ContextMap?["_errorType"]?.ToString();
                return errorType == "InvalidOperationException";
            })
        };
    }
    
    [Fact]
    public void TestBasicErrorHandling()
    {
        string uniqueId = $"'errorTest{Guid.NewGuid():N}'";
        string script = @"{
            'id': " + uniqueId + @",
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'entry': 'throwError',
                    'onError': {
                        'target': 'error',
                        'actions': 'handleError'
                    }
                },
                'error': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards);
        _stateMachine!.Start();
        
        Assert.Contains($"{_stateMachine.machineId}.idle", _stateMachine.GetActiveStateNames());
        
        _stateMachine!.Send("START");

        // The error should be caught and handled
        Assert.Contains($"{_stateMachine.machineId}.error", _stateMachine.GetActiveStateNames());
        Assert.True(_errorHandled);
        Assert.Equal("Test error", _errorMessage);
        Assert.Equal("InvalidOperationException", _errorType);
        Assert.Contains("throwError", _actionLog);
        Assert.Contains("handleError", _actionLog);
    }
    
    [Fact]
    public void TestSpecificErrorTypeHandling()
    {
        string script = @"
        {
            'id': " + $"'specificErrorTest{Guid.NewGuid():N}'" + @",
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'THROW_INVALID': 'invalidOp',
                        'THROW_ARGUMENT': 'argumentErr'
                    }
                },
                'invalidOp': {
                    'entry': 'throwError',
                    'onError': [
                        {
                            'errorType': 'InvalidOperationException',
                            'target': 'handledInvalid',
                            'actions': 'handleError'
                        },
                        {
                            'target': 'genericError'
                        }
                    ]
                },
                'argumentErr': {
                    'entry': 'throwCustomError',
                    'onError': [
                        {
                            'errorType': 'ArgumentException',
                            'target': 'handledArgument',
                            'actions': 'handleError'
                        },
                        {
                            'target': 'genericError'
                        }
                    ]
                },
                'handledInvalid': {
                    'type': 'final'
                },
                'handledArgument': {
                    'type': 'final'
                },
                'genericError': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards);
        _stateMachine!.Start();
        
        // Test InvalidOperationException handling
        _stateMachine!.Send("THROW_INVALID");
        Assert.Contains($"{_stateMachine.machineId}.handledInvalid", _stateMachine.GetActiveStateNames());
        Assert.True(_errorHandled);
        Assert.Equal("InvalidOperationException", _errorType);
        
        // Reset and test ArgumentException handling
        _errorHandled = false;
        _errorMessage = null;
        _errorType = null;
        _actionLog.Clear();
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards);
        _stateMachine!.Start();
        
        _stateMachine!.Send("THROW_ARGUMENT");
        Assert.Contains($"{_stateMachine.machineId}.handledArgument", _stateMachine.GetActiveStateNames());
        Assert.True(_errorHandled);
        Assert.Equal("ArgumentException", _errorType);
    }
    
    [Fact]
    public async void TestErrorHandlingWithGuards()
    {
        string script = @"
        {
            'id': 'guardedErrorTest',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'entry': 'throwError',
                    'onError': [
                        {
                            'target': 'recovered',
                            'cond': 'isRecoverable',
                            'actions': 'recover'
                        },
                        {
                            'target': 'failed'
                        }
                    ]
                },
                'recovered': {
                    'type': 'final'
                },
                'failed': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards);
        await _stateMachine!.StartAsync();        
        var stateString = await _stateMachine!.SendAsync("START");
        
        // Should recover because InvalidOperationException is recoverable
        Assert.Contains($"{_stateMachine.machineId}.recovered", stateString);
        Assert.Contains("recover", _actionLog);
        Assert.NotNull(_stateMachine.ContextMap!["recovered"]);
    }
    
    [Fact]
    public void TestNestedErrorHandling()
    {        
        string script = @"
        {
            'id': 'nestedErrorTest_1234',
            'initial': 'level1',
            'states': {
                'level1': {
                    'initial': 'idle',
                    'states': {
                        'idle': {
                            'on': {
                                'START': 'level2'
                            }
                        },
                        'level2': {
                            'initial': 'processing',
                            'states': {
                                'processing': {
                                    'entry': 'throwError'
                                }
                            },
                            'onError': {
                                'target': '#nestedErrorTest_1234.level1.localError',
                                'actions': 'handleError'
                            }
                        },
                        'localError': {
                            'type': 'final'
                        }
                    },
                    'onError': {
                        'target': 'globalError'
                    }
                },
                'globalError': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, false, _actions, _guards);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        
        // Error should be caught at level2 and transition to localError
        Assert.Contains($"{_stateMachine.machineId}.level1.localError", _stateMachine.GetActiveStateNames());
        Assert.True(_errorHandled);
    }
    
    [Fact]
    public void TestErrorContextPreservation()
    {
        string uniqueId = $"'contextTest{Guid.NewGuid():N}'";
        string script = @"
        {
            'id':" + uniqueId + @",
            'initial': 'idle',
            'context': {
                'attempts': 0
            },
            'states': {
                'idle': {
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'entry': 'throwError',
                    'onError': {
                        'target': 'retry',
                        'actions': 'handleError'
                    }
                },
                'retry': {
                    'entry': 'logAction',
                    'on': {
                        'RETRY': 'processing',
                        'GIVE_UP': 'failed'
                    }
                },
                'failed': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards);
        _stateMachine.ContextMap!["attempts"] = 0;
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        
        // Error context should be preserved
        Assert.Contains($"{_stateMachine.machineId}.retry", _stateMachine.GetActiveStateNames());
        Assert.NotNull(_stateMachine.ContextMap["_lastError"]);
        Assert.Equal("Test error", _errorMessage);
        
        // Update attempts
        _stateMachine.ContextMap!["attempts"] = (int)(_stateMachine.ContextMap!["attempts"] ?? 0) + 1;
        
        _stateMachine!.Send("GIVE_UP");
        Assert.Contains($"{_stateMachine.machineId}.failed", _stateMachine.GetActiveStateNames());
        Assert.Equal(1, _stateMachine.ContextMap["attempts"]);
    }
    
    [Fact]
    public void TestParallelStateErrorHandling()
    {
        const string script = @"
        {
            'id': 'parallelErrorTest',
            'type': 'parallel',
            'states': {
                'regionA': {
                    'initial': 'idle',
                    'states': {
                        'idle': {
                            'on': {
                                'ERROR_A': 'error'
                            }
                        },
                        'error': {
                            'entry': 'throwError'
                        },
                        'failed': {
                            'type': 'final'
                        }
                    },
                    'onError': {
                        'target': '.failed'
                    }
                },
                'regionB': {
                    'initial': 'working',
                    'states': {
                        'working': {
                            'on': {
                                'COMPLETE': 'done'
                            }
                        },
                        'done': {
                            'type': 'final'
                        }
                    }
                }
            },
            'onError': {
                'actions': 'handleError'
            }
        }";
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards);
        _stateMachine!.Start();
        
        var initialState = _stateMachine!.GetActiveStateNames();
        Assert.Contains("regionA.idle", initialState);
        Assert.Contains("regionB.working", initialState);
        
        _stateMachine!.Send("ERROR_A");
        
        // Region A should handle its error locally
        var afterError = _stateMachine!.GetActiveStateNames();
        Assert.Contains("regionA.failed", afterError);
        Assert.Contains("regionB.working", afterError); // Region B continues
        Assert.True(_errorHandled);
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}


