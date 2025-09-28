using Xunit;

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
            ["fetchData"] = new NamedService("fetchData", async (sm, ct) => {
                _serviceCallCount++;
                await Task.Delay(100, ct);
                sm.ContextMap!["data"] = "fetched data";
                return "fetched data";
            }),
            ["failingService"] = new NamedService("failingService", (sm, ct) => {
                return Task.FromException<object>(new InvalidOperationException("Service failed intentionally"));
            }),
            ["longRunningService"] = new NamedService("longRunningService", async (sm, ct) => {
                await Task.Delay(500, ct);
                sm.ContextMap!["result"] = "completed";
                return "completed";
            })
        };
    }
    
    [Fact]
    public async Task TestBasicInvokeService()
    {
        string script = @"
        {
            'id': 'machineId',
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
                            'actions': 'handleSuccess'
                        },
                        'onError': {
                            'target': 'failure',
                            'actions': 'handleError'
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
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true, _actions, _guards, _services);
        var stateString = await _stateMachine!.StartAsync();
        
        Assert.Contains(".idle", stateString);

        stateString = await _stateMachine!.SendAsync("START");
        Assert.Contains(".fetching", stateString);

        // Wait for service to complete - NOW TRULY EVENT-DRIVEN!
        stateString = await _stateMachine!.WaitForStateAsync("success", 1000);

        Assert.Contains(".success", stateString);

        // Give a tiny moment for actions to complete after state transition
        // This is the minimal delay needed for action execution, not for state waiting
        await Task.Delay(1);

        Assert.True(_serviceCompleted);
        Assert.Equal(1, _serviceCallCount);
        Assert.Equal("fetched data", _stateMachine.ContextMap!["data"]);
    }
    
    [Fact]
    public async Task TestInvokeServiceWithError()
    {
        string script = @"
        {
            'id': 'machineId',
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
                            'actions': 'handleError'
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
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards, _services);
        await _stateMachine!.StartAsync();
        
        var stateString = await _stateMachine!.SendAsync("START");
        Assert.Contains(".processing", stateString);

        // Wait for service to fail
        // Wait for service to fail and transition to error state
        stateString = await _stateMachine!.WaitForStateAsync("error", 1000);
        Assert.Contains(".error", stateString);

        // Give a tiny moment for actions to complete after state transition
        await Task.Delay(1);

        Assert.True(_errorOccurred);
        Assert.Equal("Service failed intentionally", _lastErrorMessage);
    }
    
    [Fact]
    public async Task TestMultipleInvokedServices()
    {
        string script = @"
        {
            'id': 'machineId',
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
                                'src': 'fetchData',
                                'onDone': { 'target': 'complete' }
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
                                'src': 'longRunningService',
                                'onDone': { 'target': 'complete' }
                            }
                        },
                        'complete': {
                            'type': 'final'
                        }
                    }
                }
            }
        }";
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards, _services);
        await _stateMachine!.StartAsync();
        
        var stateString = await _stateMachine!.SendAsync("START");
        
        // Both services should be running
        var activeStates = _stateMachine!.GetActiveStateNames();
        Assert.Contains(".serviceA.running", activeStates);
        Assert.Contains(".serviceB.running", activeStates);

        // Wait for shorter service
        await Task.Delay(200);

        // Service A should be complete
        activeStates = _stateMachine!.GetActiveStateNames();
        Assert.Contains(".serviceA.complete", activeStates);

        // Wait for longer service
        await Task.Delay(400);

        // Both services should be complete
        activeStates = _stateMachine!.GetActiveStateNames();
        Assert.Contains(".serviceA.complete", activeStates);
        Assert.Contains(".serviceB.complete", activeStates);
    }
    
    [Fact]
    public async Task TestServiceCancellationOnStateExit()
    {
        var cancellableService = new ServiceMap
        {
            ["cancellable"] = new NamedService("cancellable", async (sm, ct) => {
                try
                {
                    await Task.Delay(5000, ct); // Long delay
                }
                catch (TaskCanceledException)
                {
                    // This is expected
                }
                return null;
            })
        };
        
        string script = @"
        {
            'id': 'machineId',
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
        
        _stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,_actions, _guards, cancellableService);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        Assert.Contains(".processing", _stateMachine.GetActiveStateNames());

        // Cancel should exit the state and cancel the service
        _stateMachine!.Send("CANCEL");
        Assert.Contains(".cancelled", _stateMachine.GetActiveStateNames());
        
        // Note: In a real implementation, we'd need to verify the service was actually cancelled
        // This would require more sophisticated service management in the StateMachines
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}