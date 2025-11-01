using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Tests for XState V5 metadata features: meta, description, tags, output
/// </summary>
public class MetadataTests : XStateTestKit
{
    [Fact]
    public void Meta_SimpleState_ShouldBeAccessible()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "meta": {
                        "view": "idleView",
                        "color": "blue",
                        "priority": 1
                    },
                    "on": { "START": "running" }
                },
                "running": {
                    "meta": {
                        "view": "runningView",
                        "color": "green"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Get state while in 'idle'
        WaitForStateName(machine, "idle");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.NotNull(snapshot.Meta);
        Assert.True(snapshot.Meta.ContainsKey("test.idle"));
        Assert.Equal("idleView", snapshot.Meta["test.idle"]["view"].ToString());
        Assert.Equal("blue", snapshot.Meta["test.idle"]["color"].ToString());

        // JSON numbers are parsed as JsonElement, need to extract the value
        var priorityValue = snapshot.Meta["test.idle"]["priority"];
        if (priorityValue is System.Text.Json.JsonElement jsonElement)
        {
            Assert.Equal(1, jsonElement.GetInt32());
        }
        else
        {
            Assert.Equal(1, Convert.ToInt32(priorityValue));
        }

        // Act - Transition to 'running' and check meta
        SendEventAndWait(machine, "START",
            s => s.CurrentState == "running",
            "running state");

        snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.NotNull(snapshot.Meta);
        Assert.True(snapshot.Meta.ContainsKey("test.running"));
        Assert.Equal("runningView", snapshot.Meta["test.running"]["view"].ToString());
        Assert.Equal("green", snapshot.Meta["test.running"]["color"].ToString());
    }

    [Fact]
    public void Meta_NestedStates_ShouldCollectFromAllAncestors()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "parentState",
            "states": {
                "parentState": {
                    "meta": {
                        "level": "parent",
                        "component": "ParentComponent"
                    },
                    "initial": "childState",
                    "states": {
                        "childState": {
                            "meta": {
                                "level": "child",
                                "component": "ChildComponent"
                            }
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act
        WaitForStateName(machine, "parentState.childState");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert - Should have meta from both parent and child
        Assert.NotNull(snapshot.Meta);
        Assert.Equal(2, snapshot.Meta.Count);
        Assert.True(snapshot.Meta.ContainsKey("test.parentState"));
        Assert.True(snapshot.Meta.ContainsKey("test.parentState.childState"));
        Assert.Equal("parent", snapshot.Meta["test.parentState"]["level"].ToString());
        Assert.Equal("child", snapshot.Meta["test.parentState.childState"]["level"].ToString());
    }

    [Fact]
    public void Description_ShouldBeAccessible()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "description": "Waiting for user input",
                    "on": { "START": "running" }
                },
                "running": {
                    "description": "Processing data"
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Check description in 'idle'
        WaitForStateName(machine, "idle");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.Equal("Waiting for user input", snapshot.Description);

        // Act - Transition to 'running'
        SendEventAndWait(machine, "START",
            s => s.CurrentState == "running",
            "running state");

        snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.Equal("Processing data", snapshot.Description);
    }

    [Fact]
    public void Tags_SimpleState_ShouldBeAccessible()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "idle",
            "states": {
                "idle": {
                    "tags": ["waiting", "interactive"],
                    "on": { "START": "processing" }
                },
                "processing": {
                    "tags": ["busy", "background"]
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Check tags in 'idle'
        WaitForStateName(machine, "idle");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.NotNull(snapshot.Tags);
        Assert.Contains("waiting", snapshot.Tags);
        Assert.Contains("interactive", snapshot.Tags);
        Assert.Equal(2, snapshot.Tags.Count);

        // Act - Transition to 'processing'
        SendEventAndWait(machine, "START",
            s => s.CurrentState == "processing",
            "processing state");

        snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.NotNull(snapshot.Tags);
        Assert.Contains("busy", snapshot.Tags);
        Assert.Contains("background", snapshot.Tags);
        Assert.Equal(2, snapshot.Tags.Count);
    }

    [Fact]
    public void Tags_NestedStates_ShouldMergeFromAllAncestors()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "parent",
            "states": {
                "parent": {
                    "tags": ["container", "visible"],
                    "initial": "child",
                    "states": {
                        "child": {
                            "tags": ["active", "editable"]
                        }
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act
        WaitForStateName(machine, "parent.child");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert - Should have tags from both parent and child
        Assert.NotNull(snapshot.Tags);
        Assert.Contains("container", snapshot.Tags);
        Assert.Contains("visible", snapshot.Tags);
        Assert.Contains("active", snapshot.Tags);
        Assert.Contains("editable", snapshot.Tags);
        Assert.Equal(4, snapshot.Tags.Count);
    }

    [Fact]
    public void Output_FinalState_ShouldBeAccessible()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "working",
            "states": {
                "working": {
                    "on": { "COMPLETE": "done" }
                },
                "done": {
                    "type": "final",
                    "output": {
                        "status": "completed",
                        "result": 42,
                        "message": "Task finished successfully"
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Initial state should not have output
        WaitForStateName(machine, "working");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.Null(snapshot.Output);
        Assert.Equal("active", snapshot.Status);

        // Act - Transition to final state
        SendEventAndWait(machine, "COMPLETE",
            s => s.CurrentState == "done",
            "done state");

        snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert
        Assert.NotNull(snapshot.Output);
        Assert.Equal("done", snapshot.Status); // Final state with output

        // Output should be a dictionary-like object
        var output = snapshot.Output as System.Text.Json.JsonElement?;
        Assert.NotNull(output);
    }

    [Fact]
    public void Status_ShouldReflectMachineState()
    {
        // Arrange
        var json = """
        {
            "id": "test",
            "initial": "active",
            "states": {
                "active": {
                    "on": { "FINISH": "done" }
                },
                "done": {
                    "type": "final",
                    "output": { "result": "success" }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act & Assert - Active state
        WaitForStateName(machine, "active");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;
        Assert.Equal("active", snapshot.Status);
        Assert.True(snapshot.IsRunning);

        // Act & Assert - Final state
        SendEventAndWait(machine, "FINISH",
            s => s.CurrentState == "done",
            "done state");

        snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;
        Assert.Equal("done", snapshot.Status);
    }

    [Fact]
    public void AllMetadata_CombinedTest()
    {
        // Arrange - Test all metadata features together
        var json = """
        {
            "id": "workflow",
            "initial": "processing",
            "states": {
                "processing": {
                    "description": "Processing user data",
                    "tags": ["busy", "important"],
                    "meta": {
                        "component": "ProcessingView",
                        "showSpinner": true
                    },
                    "on": { "DONE": "complete" }
                },
                "complete": {
                    "type": "final",
                    "description": "All processing complete",
                    "tags": ["finished"],
                    "meta": {
                        "component": "SuccessView"
                    },
                    "output": {
                        "processedItems": 100,
                        "duration": 5.2
                    }
                }
            }
        }
        """;

        var factory = new XStateMachineFactory(Sys);
        var machine = factory.FromJson(json).BuildAndStart();

        // Act - Check processing state
        WaitForStateName(machine, "processing");
        var snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert - All metadata present in processing state
        Assert.Equal("processing", snapshot.CurrentState);
        Assert.Equal("Processing user data", snapshot.Description);
        Assert.NotNull(snapshot.Tags);
        Assert.Contains("busy", snapshot.Tags);
        Assert.Contains("important", snapshot.Tags);
        Assert.NotNull(snapshot.Meta);
        Assert.True(snapshot.Meta.ContainsKey("workflow.processing"));
        Assert.Null(snapshot.Output);
        Assert.Equal("active", snapshot.Status);

        // Act - Transition to final state
        SendEventAndWait(machine, "DONE",
            s => s.CurrentState == "complete",
            "complete state");

        snapshot = machine.Ask<StateSnapshot>(new GetState(), TimeSpan.FromSeconds(3)).Result;

        // Assert - All metadata present in final state
        Assert.Equal("complete", snapshot.CurrentState);
        Assert.Equal("All processing complete", snapshot.Description);
        Assert.NotNull(snapshot.Tags);
        Assert.Contains("finished", snapshot.Tags);
        Assert.NotNull(snapshot.Meta);
        Assert.True(snapshot.Meta.ContainsKey("workflow.complete"));
        Assert.NotNull(snapshot.Output);
        Assert.Equal("done", snapshot.Status);
    }
}
