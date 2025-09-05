using NUnit.Framework;
using System;
using System.Collections.Generic;
using XStateNet;

[TestFixture]
public class UnitTest_ErrorHandling
{
    private StateMachine? _stateMachine;
    private ActionMap _actions;
    private GuardMap _guards;
    private bool _errorHandled;
    private string? _errorMessage;
    private string? _errorType;
    private List<string> _actionLog;
    
    [SetUp]
    public void Setup()
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
    
    [Test]
    public void TestBasicErrorHandling()
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
                    'entry': ['throwError'],
                    'onError': {
                        'target': 'error',
                        'actions': ['handleError']
                    }
                },
                'error': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();
        
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("idle"));
        
        _stateMachine.Send("START");
        
        // The error should be caught and handled
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("error"));
        Assert.IsTrue(_errorHandled);
        Assert.AreEqual("Test error", _errorMessage);
        Assert.AreEqual("InvalidOperationException", _errorType);
        Assert.Contains("throwError", _actionLog);
        Assert.Contains("handleError", _actionLog);
    }
    
    [Test]
    public void TestSpecificErrorTypeHandling()
    {
        const string script = @"
        {
            'id': 'specificErrorTest',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'THROW_INVALID': 'invalidOp',
                        'THROW_ARGUMENT': 'argumentErr'
                    }
                },
                'invalidOp': {
                    'entry': ['throwError'],
                    'onError': [
                        {
                            'errorType': 'InvalidOperationException',
                            'target': 'handledInvalid',
                            'actions': ['handleError']
                        },
                        {
                            'target': 'genericError'
                        }
                    ]
                },
                'argumentErr': {
                    'entry': ['throwCustomError'],
                    'onError': [
                        {
                            'errorType': 'ArgumentException',
                            'target': 'handledArgument',
                            'actions': ['handleError']
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
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();
        
        // Test InvalidOperationException handling
        _stateMachine.Send("THROW_INVALID");
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("handledInvalid"));
        Assert.IsTrue(_errorHandled);
        Assert.AreEqual("InvalidOperationException", _errorType);
        
        // Reset and test ArgumentException handling
        Setup();
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();
        
        _stateMachine.Send("THROW_ARGUMENT");
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("handledArgument"));
        Assert.IsTrue(_errorHandled);
        Assert.AreEqual("ArgumentException", _errorType);
    }
    
    [Test]
    public void TestErrorHandlingWithGuards()
    {
        const string script = @"
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
                    'entry': ['throwError'],
                    'onError': [
                        {
                            'target': 'recovered',
                            'cond': 'isRecoverable',
                            'actions': ['recover']
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
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();
        
        _stateMachine.Send("START");
        
        // Should recover because InvalidOperationException is recoverable
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("recovered"));
        Assert.Contains("recover", _actionLog);
        Assert.IsTrue(_stateMachine.ContextMap!["recovered"] != null);
    }
    
    [Test]
    public void TestNestedErrorHandling()
    {
        const string script = @"
        {
            'id': 'nestedErrorTest',
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
                                    'entry': ['throwError']
                                }
                            },
                            'onError': {
                                'target': '#nestedErrorTest.level1.localError',
                                'actions': ['handleError']
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
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();
        
        _stateMachine.Send("START");
        
        // Error should be caught at level2 and transition to localError
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("localError"));
        Assert.IsTrue(_errorHandled);
    }
    
    [Test]
    public void TestErrorContextPreservation()
    {
        const string script = @"
        {
            'id': 'contextTest',
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
                    'entry': ['throwError'],
                    'onError': {
                        'target': 'retry',
                        'actions': ['handleError']
                    }
                },
                'retry': {
                    'entry': ['logAction'],
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
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.ContextMap!["attempts"] = 0;
        _stateMachine.Start();
        
        _stateMachine.Send("START");
        
        // Error context should be preserved
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("retry"));
        Assert.IsNotNull(_stateMachine.ContextMap["_lastError"]);
        Assert.AreEqual("Test error", _errorMessage);
        
        // Update attempts
        _stateMachine.ContextMap["attempts"] = (int)_stateMachine.ContextMap["attempts"] + 1;
        
        _stateMachine.Send("GIVE_UP");
        Assert.IsTrue(_stateMachine.GetActiveStateString().Contains("failed"));
        Assert.AreEqual(1, _stateMachine.ContextMap["attempts"]);
    }
    
    [Test]
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
                            'entry': ['throwError']
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
                'actions': ['handleError']
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine.Start();
        
        var initialState = _stateMachine.GetActiveStateString();
        Assert.IsTrue(initialState.Contains("regionA.idle"));
        Assert.IsTrue(initialState.Contains("regionB.working"));
        
        _stateMachine.Send("ERROR_A");
        
        // Region A should handle its error locally
        var afterError = _stateMachine.GetActiveStateString();
        Assert.IsTrue(afterError.Contains("regionA.failed"));
        Assert.IsTrue(afterError.Contains("regionB.working")); // Region B continues
        Assert.IsTrue(_errorHandled);
    }
}