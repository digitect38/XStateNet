using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for invoke/service functionality
/// Services are async operations that can be invoked by states
/// </summary>
public class InvokeServiceTests : TestKit
{
    private int _serviceCallCount = 0;
    private bool _serviceCompleted = false;
    private bool _errorOccurred = false;
    private string? _lastErrorMessage = null;

    [Fact]
    public async Task BasicInvokeService_ShouldCompleteSuccessfully()
    {
        // Arrange
        var json = """
        {
            "id": "testMachine",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "START": "fetching"
                    }
                },
                "fetching": {
                    "invoke": {
                        "src": "fetchData",
                        "onDone": {
                            "target": "success",
                            "actions": ["handleSuccess"]
                        },
                        "onError": {
                            "target": "failure",
                            "actions": ["handleError"]
                        }
                    }
                },
                "success": {
                    "type": "final"
                },
                "failure": {
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithService("fetchData", async (ctx) =>
            {
                Interlocked.Increment(ref _serviceCallCount);
                await Task.Delay(100);
                ctx.Set("data", "fetched data");
                return "fetched data";
            })
            .WithAction("handleSuccess", (ctx, _) => _serviceCompleted = true)
            .WithAction("handleError", (ctx, _) => _errorOccurred = true)
            .BuildAndStart();

        await Task.Delay(200);

        // Act
        machine.Tell(new SendEvent("START"));
        await Task.Delay(500);

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("success", snapshot.CurrentState);
        Assert.True(_serviceCompleted);
        Assert.Equal(1, _serviceCallCount);
        Assert.Equal("fetched data", snapshot.Context["data"]);
    }

    [Fact]
    public async Task InvokeService_WithError_ShouldHandleGracefully()
    {
        // Arrange
        var json = """
        {
            "id": "testMachine",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "START": "processing"
                    }
                },
                "processing": {
                    "invoke": {
                        "src": "failingService",
                        "onDone": {
                            "target": "success"
                        },
                        "onError": {
                            "target": "error",
                            "actions": ["handleError"]
                        }
                    }
                },
                "success": {
                    "type": "final"
                },
                "error": {
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithService("failingService", async (ctx) =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Service failed intentionally");
            })
            .WithAction("handleError", (ctx, _) =>
            {
                _errorOccurred = true;
                _lastErrorMessage = "Service failed intentionally";
            })
            .BuildAndStart();

        await Task.Delay(200);

        // Act
        machine.Tell(new SendEvent("START"));
        await Task.Delay(500);

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("error", snapshot.CurrentState);
        Assert.True(_errorOccurred);
        Assert.Equal("Service failed intentionally", _lastErrorMessage);
    }

    [Fact]
    public async Task MultipleServices_InParallelStates_ShouldExecuteConcurrently()
    {
        // Arrange
        var serviceACompleted = false;
        var serviceBCompleted = false;

        var json = """
        {
            "id": "testMachine",
            "type": "parallel",
            "states": {
                "serviceA": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": {
                                "START": "running"
                            }
                        },
                        "running": {
                            "invoke": {
                                "src": "serviceA",
                                "onDone": {
                                    "target": "complete",
                                    "actions": ["onServiceADone"]
                                }
                            }
                        },
                        "complete": {
                            "type": "final"
                        }
                    }
                },
                "serviceB": {
                    "initial": "idle",
                    "states": {
                        "idle": {
                            "on": {
                                "START": "running"
                            }
                        },
                        "running": {
                            "invoke": {
                                "src": "serviceB",
                                "onDone": {
                                    "target": "complete",
                                    "actions": ["onServiceBDone"]
                                }
                            }
                        },
                        "complete": {
                            "type": "final"
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithService("serviceA", async (ctx) =>
            {
                await Task.Delay(100);
                return "A complete";
            })
            .WithService("serviceB", async (ctx) =>
            {
                await Task.Delay(150);
                return "B complete";
            })
            .WithAction("onServiceADone", (ctx, _) => serviceACompleted = true)
            .WithAction("onServiceBDone", (ctx, _) => serviceBCompleted = true)
            .BuildAndStart();

        await Task.Delay(200);

        // Act
        machine.Tell(new SendEvent("START"));
        await Task.Delay(500);

        // Assert
        Assert.True(serviceACompleted);
        Assert.True(serviceBCompleted);
    }

    [Fact]
    public async Task ServiceInvocation_WithDataPassing_ShouldUpdateContext()
    {
        // Arrange
        var json = """
        {
            "id": "testMachine",
            "initial": "idle",
            "context": {
                "userId": "123",
                "userData": null
            },
            "states": {
                "idle": {
                    "on": {
                        "FETCH": "loading"
                    }
                },
                "loading": {
                    "invoke": {
                        "src": "loadUserData",
                        "onDone": {
                            "target": "loaded",
                            "actions": ["saveUserData"]
                        }
                    }
                },
                "loaded": {
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json)
            .WithContext("userId", "123")
            .WithService("loadUserData", async (ctx) =>
            {
                var userId = ctx.Get<string>("userId");
                await Task.Delay(50);
                return new { UserId = userId, Name = "John Doe", Email = "john@example.com" };
            })
            .WithAction("saveUserData", (ctx, data) =>
            {
                ctx.Set("userData", data);
            })
            .BuildAndStart();

        await Task.Delay(200);

        // Act
        machine.Tell(new SendEvent("FETCH"));
        await Task.Delay(400);

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("loaded", snapshot.CurrentState);
        Assert.NotNull(snapshot.Context["userData"]);
    }

    [Fact]
    public async Task ServiceCancellation_OnStateExit_ShouldCancelService()
    {
        // Arrange
        var serviceCancelled = false;

        var json = """
        {
            "id": "testMachine",
            "initial": "idle",
            "states": {
                "idle": {
                    "on": {
                        "START": "processing"
                    }
                },
                "processing": {
                    "invoke": {
                        "src": "longRunningService"
                    },
                    "on": {
                        "CANCEL": "cancelled"
                    }
                },
                "cancelled": {
                    "type": "final"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var cts = new CancellationTokenSource();

        var machine = factory.FromJson(json)
            .WithService("longRunningService", async (ctx) =>
            {
                try
                {
                    await Task.Delay(5000, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    serviceCancelled = true;
                }
                return null;
            })
            .BuildAndStart();

        await Task.Delay(200);

        // Act
        machine.Tell(new SendEvent("START"));
        await Task.Delay(200);

        // Cancel the service by transitioning out of the state
        cts.Cancel();
        machine.Tell(new SendEvent("CANCEL"));
        await Task.Delay(300);

        var snapshot = await machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(2));

        // Assert
        Assert.Contains("cancelled", snapshot.CurrentState);
        Assert.True(serviceCancelled);
    }
}
