using NUnit.Framework;
using System;
using System.Collections.Generic;
using XStateNet;

namespace XSateV5_Test.AdvancedFeatures;

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
        _stateMachine!.Start();
        
        Assert.That(_stateMachine.GetActiveStateString().Contains("idle"), Is.True);
        
        _stateMachine!.Send("START");
        
        // The error should be caught and handled
        Assert.That(_stateMachine.GetActiveStateString().Contains("error"), Is.True);
        Assert.That(_errorHandled, Is.True);
        Assert.That(_errorMessage, Is.EqualTo("Test error"));
        Assert.That(_errorType, Is.EqualTo("InvalidOperationException"));
        Assert.That(_actionLog, Does.Contain("throwError"));
        Assert.That(_actionLog, Does.Contain("handleError"));
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
        _stateMachine!.Start();
        
        // Test InvalidOperationException handling
        _stateMachine!.Send("THROW_INVALID");
        Assert.That(_stateMachine.GetActiveStateString().Contains("handledInvalid"), Is.True);
        Assert.That(_errorHandled, Is.True);
        Assert.That(_errorType, Is.EqualTo("InvalidOperationException"));
        
        // Reset and test ArgumentException handling
        Setup();
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine!.Start();
        
        _stateMachine!.Send("THROW_ARGUMENT");
        Assert.That(_stateMachine.GetActiveStateString().Contains("handledArgument"), Is.True);
        Assert.That(_errorHandled, Is.True);
        Assert.That(_errorType, Is.EqualTo("ArgumentException"));
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
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        
        // Should recover because InvalidOperationException is recoverable
        Assert.That(_stateMachine.GetActiveStateString().Contains("recovered"), Is.True);
        Assert.That(_actionLog, Does.Contain("recover"));
        Assert.That(_stateMachine.ContextMap!["recovered"], Is.Not.Null);
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
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        
        // Error should be caught at level2 and transition to localError
        Assert.That(_stateMachine.GetActiveStateString().Contains("localError"), Is.True);
        Assert.That(_errorHandled, Is.True);
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
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        
        // Error context should be preserved
        Assert.That(_stateMachine.GetActiveStateString().Contains("retry"), Is.True);
        Assert.That(_stateMachine.ContextMap["_lastError"], Is.Not.Null);
        Assert.That(_errorMessage, Is.EqualTo("Test error"));
        
        // Update attempts
        _stateMachine.ContextMap!["attempts"] = (int)(_stateMachine.ContextMap!["attempts"] ?? 0) + 1;
        
        _stateMachine!.Send("GIVE_UP");
        Assert.That(_stateMachine.GetActiveStateString().Contains("failed"), Is.True);
        Assert.That(_stateMachine.ContextMap["attempts"], Is.EqualTo(1));
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
        _stateMachine!.Start();
        
        var initialState = _stateMachine!.GetActiveStateString();
        Assert.That(initialState.Contains("regionA.idle"), Is.True);
        Assert.That(initialState.Contains("regionB.working"), Is.True);
        
        _stateMachine!.Send("ERROR_A");
        
        // Region A should handle its error locally
        var afterError = _stateMachine!.GetActiveStateString();
        Assert.That(afterError.Contains("regionA.failed"), Is.True);
        Assert.That(afterError.Contains("regionB.working"), Is.True); // Region B continues
        Assert.That(_errorHandled, Is.True);
    }
}