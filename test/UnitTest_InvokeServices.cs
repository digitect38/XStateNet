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
        var uniqueId = "TestBasicInvokeService_" + Guid.NewGuid().ToString("N");

        string script = @"
        {
            ""id"": """ + uniqueId + @""",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""START"": ""fetching""
                    }
                },
                ""fetching"": {
                    ""invoke"": {
                        ""src"": ""fetchData"",
                        ""onDone"": {
                            ""target"": ""success"",
                            ""actions"": ""handleSuccess""
                        },
                        ""onError"": {
                            ""target"": ""failure"",
                            ""actions"": ""handleError""
                        }
                    }
                },
                ""success"": {
                    ""type"": ""final""
                },
                ""failure"": {
                    ""type"": ""final""
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards, _services);
        _stateMachine!.Start();
        
        Assert.Contains(uniqueId + ".idle", _stateMachine.GetActiveStateString());

        _stateMachine!.Send("START");
        Assert.Contains(uniqueId + ".fetching", _stateMachine.GetActiveStateString());

        // Wait for service to complete
        await Task.Delay(200);

        Assert.Contains(uniqueId + ".success", _stateMachine.GetActiveStateString());
        Assert.True(_serviceCompleted);
        Assert.Equal(1, _serviceCallCount);
        Assert.Equal("fetched data", _stateMachine.ContextMap!["data"]);
    }
    
    [Fact]
    public async Task TestInvokeServiceWithError()
    {
        var uniqueId = "TestInvokeServiceWithError_" + Guid.NewGuid().ToString("N");

        string script = @"
        {
            ""id"": """ + uniqueId + @""",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""START"": ""processing""
                    }
                },
                ""processing"": {
                    ""invoke"": {
                        ""src"": ""failingService"",
                        ""onDone"": {
                            ""target"": ""success""
                        },
                        ""onError"": {
                            ""target"": ""error"",
                            ""actions"": ""handleError""
                        }
                    }
                },
                ""success"": {
                    ""type"": ""final""
                },
                ""error"": {
                    ""type"": ""final""
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards, _services);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        Assert.Contains(uniqueId + ".processing", _stateMachine.GetActiveStateString());

        // Wait for service to fail
        await Task.Delay(150);

        Assert.Contains(uniqueId + ".error", _stateMachine.GetActiveStateString());
        Assert.True(_errorOccurred);
        Assert.Equal("Service failed intentionally", _lastErrorMessage);
    }
    
    [Fact]
    public async Task TestMultipleInvokedServices()
    {
        var uniqueId = "TestMultipleInvokedServices_" + Guid.NewGuid().ToString("N");

        string script = @"
        {
            ""id"": """ + uniqueId + @""",
            ""initial"": ""idle"",
            ""type"": ""parallel"",
            ""states"": {
                ""serviceA"": {
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""START"": ""running""
                            }
                        },
                        ""running"": {
                            ""invoke"": {
                                ""src"": ""fetchData"",
                                ""onDone"": { ""target"": ""complete"" }
                            }
                        },
                        ""complete"": {
                            ""type"": ""final""
                        }
                    }
                },
                ""serviceB"": {
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""START"": ""running""
                            }
                        },
                        ""running"": {
                            ""invoke"": {
                                ""src"": ""longRunningService"",
                                ""onDone"": { ""target"": ""complete"" }
                            }
                        },
                        ""complete"": {
                            ""type"": ""final""
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
        Assert.Contains(uniqueId + ".serviceA.running", activeStates);
        Assert.Contains(uniqueId + ".serviceB.running", activeStates);

        // Wait for shorter service
        await Task.Delay(200);

        // Service A should be complete
        activeStates = _stateMachine!.GetActiveStateString();
        Assert.Contains(uniqueId + ".serviceA.complete", activeStates);

        // Wait for longer service
        await Task.Delay(400);

        // Both services should be complete
        activeStates = _stateMachine!.GetActiveStateString();
        Assert.Contains(uniqueId + ".serviceA.complete", activeStates);
        Assert.Contains(uniqueId + ".serviceB.complete", activeStates);
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
        
        var uniqueId = "TestServiceCancellationOnStateExit_" + Guid.NewGuid().ToString("N");

        string script = @"
        {
            ""id"": """ + uniqueId + @""",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""START"": ""processing""
                    }
                },
                ""processing"": {
                    ""invoke"": {
                        ""src"": ""cancellable""
                    },
                    ""on"": {
                        ""CANCEL"": ""cancelled""
                    }
                },
                ""cancelled"": {
                    ""type"": ""final""
                }
            }
        }";
        
        _stateMachine = StateMachine.CreateFromScript(script, _actions, _guards, cancellableService);
        _stateMachine!.Start();
        
        _stateMachine!.Send("START");
        Assert.Contains(uniqueId + ".processing", _stateMachine.GetActiveStateString());

        // Cancel should exit the state and cancel the service
        _stateMachine!.Send("CANCEL");
        Assert.Contains(uniqueId + ".cancelled", _stateMachine.GetActiveStateString());
        
        // Note: In a real implementation, we'd need to verify the service was actually cancelled
        // This would require more sophisticated service management in the StateMachines
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
}