using Akka.TestKit.Xunit2;
using FluentAssertions;
using XStateNet2.Core;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for XState v5 relative path notation in transitions
/// Verifies that transitions like ".error_timeout" correctly resolve to "orchestrator.error_timeout"
/// </summary>
public class RelativePathTransitionsTests : XStateTestKit
{
    private readonly ITestOutputHelper _output;

    public RelativePathTransitionsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RelativePath_ShouldResolveToChildState()
    {
        // Arrange - Test that ".error" resolves to "orchestrator.error"
        var json = @"{
            ""id"": ""test"",
            ""initial"": ""orchestrator"",
            ""states"": {
                ""orchestrator"": {
                    ""initial"": ""idle"",
                    ""on"": {
                        ""ERROR"": "".error""
                    },
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""START"": ""working""
                            }
                        },
                        ""working"": {},
                        ""error"": {
                            ""type"": ""final""
                        }
                    }
                }
            }
        }";

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Send ERROR event from orchestrator level
        // Assert - Should transition to orchestrator.error
        SendEventAndWait(machine, "ERROR",
            s => s.CurrentState.Contains("error") && s.CurrentState.Contains("orchestrator"),
            "state to contain both 'error' and 'orchestrator'");
    }

    [Fact]
    public void GlobalOnHandler_WithRelativePath_ShouldWork()
    {
        // Arrange - Test global "on" handler with relative path (like CMP orchestrator)
        var json = @"{
            ""id"": ""cmp"",
            ""type"": ""parallel"",
            ""states"": {
                ""orchestrator"": {
                    ""initial"": ""idle"",
                    ""on"": {
                        ""TIMEOUT"": "".error_timeout"",
                        ""PROCESS_ERROR"": "".error_handling""
                    },
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""START"": ""working""
                            }
                        },
                        ""working"": {},
                        ""error_timeout"": {
                            ""entry"": [""logTimeout""]
                        },
                        ""error_handling"": {
                            ""entry"": [""logError""]
                        }
                    }
                },
                ""equipment"": {
                    ""initial"": ""ready"",
                    ""states"": {
                        ""ready"": {}
                    }
                }
            }
        }";

        var factory = new XStateMachineFactory(Sys);
        var logMessages = new List<string>();

        var machine = factory.FromJson(json)
            .WithAction("logTimeout", (ctx, data) => logMessages.Add("TIMEOUT"))
            .WithAction("logError", (ctx, data) => logMessages.Add("ERROR"))
            .BuildAndStart();

        // Act - Send TIMEOUT event (should go to orchestrator.error_timeout)
        SendEventAndWait(machine, "TIMEOUT",
            s => s.CurrentState.Contains("error_timeout") && s.CurrentState.Contains("orchestrator"),
            "state to contain both 'error_timeout' and 'orchestrator'");

        // Assert
        AwaitCondition(() => logMessages.Contains("TIMEOUT"), TimeSpan.FromSeconds(2));
        logMessages.Should().Contain("TIMEOUT");
    }

    [Fact]
    public void AlwaysTransition_WithRelativePath_ShouldWork()
    {
        // Arrange - Test "always" transitions with relative paths
        var json = @"{
            ""id"": ""test"",
            ""initial"": ""parent"",
            ""states"": {
                ""parent"": {
                    ""initial"": ""check"",
                    ""states"": {
                        ""check"": {
                            ""always"": [
                                {
                                    ""target"": "".error"",
                                    ""guard"": ""hasError""
                                },
                                {
                                    ""target"": "".success""
                                }
                            ]
                        },
                        ""error"": {
                            ""type"": ""final""
                        },
                        ""success"": {
                            ""type"": ""final""
                        }
                    }
                }
            }
        }";

        var factory = new XStateMachineFactory(Sys);

        // Test with error condition
        var machineWithError = factory.FromJson(json)
            .WithGuard("hasError", (ctx, data) => true)
            .BuildAndStart();

        WaitForState(machineWithError,
            s => s.CurrentState.Contains("error") && s.CurrentState.Contains("parent"),
            "state to contain both 'error' and 'parent'");

        // Test without error condition
        var machineNoError = factory.FromJson(json)
            .WithGuard("hasError", (ctx, data) => false)
            .BuildAndStart();

        WaitForState(machineNoError,
            s => s.CurrentState.Contains("success") && s.CurrentState.Contains("parent"),
            "state to contain both 'success' and 'parent'");
    }

    [Fact]
    public void RelativePath_InNestedState_ShouldResolveCorrectly()
    {
        // Arrange - Test relative paths in deeply nested states
        var json = @"{
            ""id"": ""test"",
            ""initial"": ""level1"",
            ""states"": {
                ""level1"": {
                    ""initial"": ""level2"",
                    ""states"": {
                        ""level2"": {
                            ""initial"": ""level3"",
                            ""on"": {
                                ""ERROR"": "".error""
                            },
                            ""states"": {
                                ""level3"": {
                                    ""on"": {
                                        ""WORK"": ""working""
                                    }
                                },
                                ""working"": {},
                                ""error"": {}
                            }
                        }
                    }
                }
            }
        }";

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Send ERROR from level2
        // Assert - Should go to level1.level2.error
        SendEventAndWait(machine, "ERROR",
            s => s.CurrentState.Contains("error") && s.CurrentState.Contains("level2"),
            "state to contain both 'error' and 'level2'");
    }

    [Fact]
    public void AbsolutePath_ShouldStillWork()
    {
        // Arrange - Verify absolute paths still work (no dot prefix)
        var json = @"{
            ""id"": ""test"",
            ""initial"": ""stateA"",
            ""states"": {
                ""stateA"": {
                    ""on"": {
                        ""GO_B"": ""stateB""
                    }
                },
                ""stateB"": {},
                ""parent"": {
                    ""initial"": ""child"",
                    ""states"": {
                        ""child"": {
                            ""on"": {
                                ""GO_B"": ""stateB""
                            }
                        }
                    }
                }
            }
        }";

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act & Assert
        SendEventAndWait(machine, "GO_B",
            s => s.CurrentState == "stateB",
            "state to be 'stateB'");
    }

    [Fact]
    public void MixedPaths_RelativeAndAbsolute_ShouldBothWork()
    {
        // Arrange - Test that both relative and absolute paths work in same machine
        var json = @"{
            ""id"": ""test"",
            ""initial"": ""parent"",
            ""states"": {
                ""parent"": {
                    ""initial"": ""idle"",
                    ""on"": {
                        ""GLOBAL_ERROR"": ""globalError""
                    },
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""LOCAL_ERROR"": "".localError"",
                                ""GLOBAL_ERROR"": ""globalError""
                            }
                        },
                        ""localError"": {}
                    }
                },
                ""globalError"": {}
            }
        }";

        var factory = new XStateMachineFactory(Sys);

        // Test relative path
        var machine1 = factory.FromJson(json).BuildAndStart();
        SendEventAndWait(machine1, "LOCAL_ERROR",
            s => s.CurrentState.Contains("localError") && s.CurrentState.Contains("parent"),
            "state to contain both 'localError' and 'parent'");

        // Test absolute path
        var machine2 = factory.FromJson(json).BuildAndStart();
        SendEventAndWait(machine2, "GLOBAL_ERROR",
            s => s.CurrentState == "globalError",
            "state to be 'globalError'");
    }

    [Fact]
    public void CMPOrchestratorPattern_ShouldWork()
    {
        // Arrange - Simplified version of actual CMP orchestrator pattern
        var json = @"{
            ""id"": ""cmp"",
            ""type"": ""parallel"",
            ""context"": {
                ""retry_count"": 0,
                ""max_retries"": 3
            },
            ""states"": {
                ""orchestrator"": {
                    ""initial"": ""idle"",
                    ""on"": {
                        ""TIMEOUT"": "".error_timeout"",
                        ""PROCESS_ERROR"": "".error_handling""
                    },
                    ""states"": {
                        ""idle"": {
                            ""on"": {
                                ""START"": ""working""
                            }
                        },
                        ""working"": {
                            ""on"": {
                                ""COMPLETE"": ""idle""
                            }
                        },
                        ""error_timeout"": {
                            ""always"": [
                                {
                                    ""target"": "".retry"",
                                    ""guard"": ""canRetry""
                                },
                                {
                                    ""target"": "".error_fatal""
                                }
                            ]
                        },
                        ""error_handling"": {},
                        ""retry"": {},
                        ""error_fatal"": {
                            ""type"": ""final""
                        }
                    }
                }
            }
        }";

        var factory = new XStateMachineFactory(Sys);

        // Test timeout with retry available
        var machineCanRetry = factory.FromJson(json)
            .WithGuard("canRetry", (ctx, data) => ctx.Get<int>("retry_count") < ctx.Get<int>("max_retries"))
            .BuildAndStart();

        SendEventAndWait(machineCanRetry, "START",
            s => s.CurrentState.Contains("working"),
            "state to contain 'working'");
        SendEventAndWait(machineCanRetry, "TIMEOUT",
            s => s.CurrentState.Contains("retry") && s.CurrentState.Contains("orchestrator"),
            "state to contain both 'retry' and 'orchestrator'");

        // Test timeout with no retry available
        var machineNoRetry = factory.FromJson(json)
            .WithGuard("canRetry", (ctx, data) => false)
            .BuildAndStart();

        SendEventAndWait(machineNoRetry, "START",
            s => s.CurrentState.Contains("working"),
            "state to contain 'working'");
        SendEventAndWait(machineNoRetry, "TIMEOUT",
            s => s.CurrentState.Contains("error_fatal") && s.CurrentState.Contains("orchestrator"),
            "state to contain both 'error_fatal' and 'orchestrator'");
    }
}
