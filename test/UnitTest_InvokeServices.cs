using Xunit;
using System;
using System.Threading.Tasks;
using System.Threading;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using System.Collections.Generic;

namespace ActorModelTests;

public class UnitTest_InvokeServices : OrchestratorTestBase
{
    private IPureStateMachine? _currentMachine;
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
    }

    private StateMachine? GetUnderlying() =>
        (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

    private Dictionary<string, Action<OrchestratedContext>> CreateActions()
    {
        return new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEntry"] = (ctx) => Console.WriteLine("Entering state"),
            ["handleSuccess"] = (ctx) => _serviceCompleted = true,
            ["handleError"] = (ctx) =>
            {
                _errorOccurred = true;
                var underlying = GetUnderlying();
                _lastErrorMessage = underlying?.ContextMap?["_errorMessage"]?.ToString();
            }
        };
    }

    private Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>> CreateServices()
    {
        return new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["fetchData"] = async (sm, ct) =>
            {
                _serviceCallCount++;
                await Task.Delay(100, ct);
                sm.ContextMap!["data"] = "fetched data";
                return "fetched data";
            },
            ["failingService"] = async (sm, ct) =>
            {
                await Task.Delay(10, ct); // Small delay to ensure async execution
                throw new InvalidOperationException("Service failed intentionally");
            },
            ["longRunningService"] = async (sm, ct) =>
            {
                await Task.Delay(500, ct);
                sm.ContextMap!["result"] = "completed";
                return "completed";
            }
        };
    }

    [Fact]
    public async Task TestBasicInvokeService()
    {
        string script = $$"""
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
        }
        """;

        _currentMachine = CreateMachine("machineId", script, CreateActions(), null, CreateServices());
        await _currentMachine.StartAsync();

        var initialState = _currentMachine.CurrentState;
        Assert.Contains("idle", initialState);

        await SendEventAsync("TEST", _currentMachine.Id, "START");

        // Wait deterministically for service to complete
        await WaitForStateAsync(_currentMachine, "success", timeoutMs: 2000);

        var finalState = _currentMachine.CurrentState;
        Assert.Contains("success", finalState);

        Assert.True(_serviceCompleted);
        Assert.Equal(1, _serviceCallCount);
        Assert.Equal("fetched data", GetUnderlying()?.ContextMap?["data"]);
    }

    [Fact]
    public async Task TestInvokeServiceWithError()
    {
        string script = @"{
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

        _currentMachine = CreateMachine("machineId", script, CreateActions(), null, CreateServices());
        await _currentMachine.StartAsync();

        await SendEventAsync("TEST", _currentMachine.Id, "START");

        // Wait deterministically for error state
        await WaitForStateAsync(_currentMachine, "error", timeoutMs: 2000);

        var finalState = _currentMachine.CurrentState;
        Assert.Contains("error", finalState);

        Assert.True(_errorOccurred);
        Assert.Equal("Service failed intentionally", _lastErrorMessage);
    }

    [Fact]
    public async Task TestMultipleInvokedServices()
    {
        string script = @"{
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

        _currentMachine = CreateMachine("machineId", script, CreateActions(), null, CreateServices());
        await _currentMachine.StartAsync();

        await SendEventAsync("TEST", _currentMachine.Id, "START");

        // Wait deterministically for serviceA (shorter) to complete
        await WaitForStateAsync(_currentMachine, "serviceA.complete", timeoutMs: 2000);

        var stateAfterA = _currentMachine.CurrentState;
        Assert.Contains("serviceA.complete", stateAfterA);

        // Wait deterministically for serviceB (longer) to complete
        await WaitForStateAsync(_currentMachine, "serviceB.complete", timeoutMs: 2000);

        var finalState = _currentMachine.CurrentState;
        Assert.Contains("serviceA.complete", finalState);
        Assert.Contains("serviceB.complete", finalState);
    }

    [Fact]
    public async Task TestServiceCancellationOnStateExit()
    {
        var uniqueId = $"machineId_{Guid.NewGuid():N}";
        var cancellableServices = new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["cancellable"] = async (sm, ct) =>
            {
                try
                {
                    await Task.Delay(5000, ct); // Long delay
                }
                catch (TaskCanceledException)
                {
                    // This is expected
                }
                return null!;
            }
        };

        string script = $$"""
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
        }
        """;

        _currentMachine = CreateMachine(uniqueId, script, CreateActions(), null, cancellableServices);
        await _currentMachine.StartAsync();

        await SendEventAsync("TEST", _currentMachine.Id, "START");

        // Wait for processing state
        await WaitForStateAsync(_currentMachine, "processing", timeoutMs: 1000);
        Assert.Contains("processing", _currentMachine.CurrentState);

        // Cancel should exit the state and cancel the service
        await SendEventAsync("TEST", _currentMachine.Id, "CANCEL");

        // Wait for cancelled state
        await WaitForStateAsync(_currentMachine, "cancelled", timeoutMs: 1000);
        Assert.Contains("cancelled", _currentMachine.CurrentState);
    }
}
