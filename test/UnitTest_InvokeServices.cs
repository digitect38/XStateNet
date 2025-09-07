using Xunit;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using System.Threading;
using XStateNet;
namespace ActorModelTests;
public class UnitTest_InvokeServices : IDisposable
{
    private StateMachine? _stateMachine;
    private ActionMap _actions;
    private GuardMap _guards;
    private ServiceMap _services;
    private int _serviceCallCount;
    private bool _serviceCompleted;
    private bool _errorOccurred;
    private string? _lastErrorMessage;
    
    public UnitTest_InvokeServices()
    {
        _serviceCallCount = 0;
        _serviceCompleted = false;
        _errorOccurred = false;
        _lastErrorMessage = null;
        
        _actions = new ActionMap
        {
            ["logEntry"] = new List<NamedAction> { new NamedAction("logEntry", (sm) => Console.WriteLine("Entering state")) },
            ["handleSuccess"] = new List<NamedAction> { new NamedAction("handleSuccess", (sm) => _serviceCompleted = true) },
            ["handleError"] = new List<NamedAction> { new NamedAction("handleError", (sm) => {
                _errorOccurred = true;
                _lastErrorMessage = sm.ContextMap?["_errorMessage"]?.ToString();
            }) }
        };
        
        _guards = new GuardMap();
        
        _services = new ServiceMap
        {
            ["fetchData"] = new NamedService("fetchData", async (sm) => {
                _serviceCallCount++;
                await Task.Delay(100);
                sm.ContextMap!["data"] = "fetched data";
            }),
            ["failingService"] = new NamedService("failingService", async (sm) => {
                await Task.Delay(50);
                throw new InvalidOperationException("Service failed intentionally");
            }),
            ["longRunningService"] = new NamedService("longRunningService", async (sm) => {
                await Task.Delay(500);
                sm.ContextMap!["result"] = "completed";
            })
        };
    }
    
    [Fact]
    public async Task TestBasicInvokeService()
    {
        const string script = @"
        {
            'id': 'invokeTest',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'fetching'
                    }
                },
                'fetching': {
                    'invoke': {
                        'src': 'fetchData',
                        'onDone': {
                            'target': 'success',
                            'actions': ['handleSuccess']
                        },
                        'onError': {
                            'target': 'failure',
                            'actions': ['handleError']
                        }
                    }
                },
                'success': {
                    'type': 'final'
                },
                'failure': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards, _services);
        _stateMachine!.Start();
        
        _stateMachine.GetActiveStateString().Contains("idle").Should().BeTrue();
        
        _stateMachine!.Send("START");
        _stateMachine.GetActiveStateString().Contains("fetching").Should().BeTrue();
        
        // Wait for service to complete
        await Task.Delay(200);
        _stateMachine!.Send("onDone");
        
        _stateMachine.GetActiveStateString().Contains("success").Should().BeTrue();
        _serviceCompleted.Should().BeTrue();
        _serviceCallCount.Should().Be(1);
        _stateMachine.ContextMap!["data"].Should().Be("fetched data");
    }
    
    [Fact]
    public async Task TestInvokeServiceWithError()
    {
        const string script = @"
        {
            'id': 'invokeErrorTest',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'invoke': {
                        'src': 'failingService',
                        'onDone': {
                            'target': 'success'
                        },
                        'onError': {
                            'target': 'error',
                            'actions': ['handleError']
                        }
                    }
                },
                'success': {
                    'type': 'final'
                },
                'error': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards, _services);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        _stateMachine.GetActiveStateString().Contains("processing").Should().BeTrue();
        
        // Wait for service to fail
        await Task.Delay(150);
        _stateMachine!.Send("onError");
        
        _stateMachine.GetActiveStateString().Contains("error").Should().BeTrue();
        _errorOccurred.Should().BeTrue();
        _lastErrorMessage.Should().Be("Service failed intentionally");
    }
    
    [Fact]
    public async Task TestMultipleInvokedServices()
    {
        const string script = @"
        {
            'id': 'multiInvokeTest',
            'initial': 'idle',
            'type': 'parallel',
            'states': {
                'serviceA': {
                    'initial': 'idle',
                    'states': {
                        'idle': {
                            'on': {
                                'START': 'running'
                            }
                        },
                        'running': {
                            'invoke': {
                                'src': 'fetchData'
                            },
                            'on': {
                                'onDone': 'complete'
                            }
                        },
                        'complete': {
                            'type': 'final'
                        }
                    }
                },
                'serviceB': {
                    'initial': 'idle',
                    'states': {
                        'idle': {
                            'on': {
                                'START': 'running'
                            }
                        },
                        'running': {
                            'invoke': {
                                'src': 'longRunningService'
                            },
                            'on': {
                                'onDone': 'complete'
                            }
                        },
                        'complete': {
                            'type': 'final'
                        }
                    }
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards, _services);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        
        // Both services should be running
        var activeStates = _stateMachine!.GetActiveStateString();
        activeStates.Contains("serviceA.running").Should().BeTrue();
        activeStates.Contains("serviceB.running").Should().BeTrue();
        
        // Wait for shorter service
        await Task.Delay(200);
        _stateMachine!.Send("onDone");
        
        // Service A should be complete
        activeStates = _stateMachine!.GetActiveStateString();
        activeStates.Contains("serviceA.complete").Should().BeTrue();
        
        // Wait for longer service
        await Task.Delay(400);
        _stateMachine!.Send("onDone");
        
        // Both services should be complete
        activeStates = _stateMachine!.GetActiveStateString();
        activeStates.Contains("serviceA.complete").Should().BeTrue();
        activeStates.Contains("serviceB.complete").Should().BeTrue();
    }
    
    [Fact]
    public void TestServiceCancellationOnStateExit()
    {
        var cancellableService = new ServiceMap
        {
            ["cancellable"] = new NamedService("cancellable", async (sm) => {
                try
                {
                    await Task.Delay(5000); // Long delay
                }
                catch (TaskCanceledException)
                {
                    throw;
                }
            })
        };
        
        const string script = @"
        {
            'id': 'cancelTest',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': {
                        'START': 'processing'
                    }
                },
                'processing': {
                    'invoke': {
                        'src': 'cancellable'
                    },
                    'on': {
                        'CANCEL': 'cancelled'
                    }
                },
                'cancelled': {
                    'type': 'final'
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards, cancellableService);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        _stateMachine.GetActiveStateString().Contains("processing").Should().BeTrue();
        
        // Cancel should exit the state and cancel the service
        _stateMachine!.Send("CANCEL");
        _stateMachine.GetActiveStateString().Contains("cancelled").Should().BeTrue();
        
        // Note: In a real implementation, we'd need to verify the service was actually cancelled
        // This would require more sophisticated service management in the StateMachine
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}

