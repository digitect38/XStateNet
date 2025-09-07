using Xunit;
using FluentAssertions;
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
        
        _stateMachine.GetActiveStateString().Contains("idle").Should().BeTrue();
        
        _stateMachine!.Send("START");
        
        // The error should be caught and handled
        _stateMachine.GetActiveStateString().Contains("error").Should().BeTrue();
        _errorHandled.Should().BeTrue();
        _errorMessage.Should().Be("Test error");
        _errorType.Should().Be("InvalidOperationException");
        _actionLog.Should().Contain("throwError");
        _actionLog.Should().Contain("handleError");
    }
    
    [Fact]
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
        _stateMachine.GetActiveStateString().Contains("handledInvalid").Should().BeTrue();
        _errorHandled.Should().BeTrue();
        _errorType.Should().Be("InvalidOperationException");
        
        // Reset and test ArgumentException handling
        _errorHandled = false;
        _errorMessage = null;
        _errorType = null;
        _actionLog.Clear();
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards);
        _stateMachine!.Start();
        
        _stateMachine!.Send("THROW_ARGUMENT");
        _stateMachine.GetActiveStateString().Contains("handledArgument").Should().BeTrue();
        _errorHandled.Should().BeTrue();
        _errorType.Should().Be("ArgumentException");
    }
    
    [Fact]
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
        _stateMachine.GetActiveStateString().Contains("recovered").Should().BeTrue();
        _actionLog.Should().Contain("recover");
        _stateMachine.ContextMap!["recovered"].Should().NotBeNull();
    }
    
    [Fact]
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
        _stateMachine.GetActiveStateString().Contains("localError").Should().BeTrue();
        _errorHandled.Should().BeTrue();
    }
    
    [Fact]
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
        _stateMachine.GetActiveStateString().Contains("retry").Should().BeTrue();
        _stateMachine.ContextMap["_lastError"].Should().NotBeNull();
        _errorMessage.Should().Be("Test error");
        
        // Update attempts
        _stateMachine.ContextMap!["attempts"] = (int)(_stateMachine.ContextMap!["attempts"] ?? 0) + 1;
        
        _stateMachine!.Send("GIVE_UP");
        _stateMachine.GetActiveStateString().Contains("failed").Should().BeTrue();
        _stateMachine.ContextMap["attempts"].Should().Be(1);
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
        initialState.Contains("regionA.idle").Should().BeTrue();
        initialState.Contains("regionB.working").Should().BeTrue();
        
        _stateMachine!.Send("ERROR_A");
        
        // Region A should handle its error locally
        var afterError = _stateMachine!.GetActiveStateString();
        afterError.Contains("regionA.failed").Should().BeTrue();
        afterError.Contains("regionB.working").Should().BeTrue(); // Region B continues
        _errorHandled.Should().BeTrue();
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}


