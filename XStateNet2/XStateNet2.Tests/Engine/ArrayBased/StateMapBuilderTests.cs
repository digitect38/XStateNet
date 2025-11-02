using FluentAssertions;
using Xunit;
using XStateNet2.Core.Engine;
using XStateNet2.Core.Engine.ArrayBased;
using XStateNet2.Core.Runtime;

namespace XStateNet2.Tests.Engine.ArrayBased;

/// <summary>
/// Comprehensive tests for StateMapBuilder - the "compilation" step that converts
/// standard XState JSON definitions into array-optimized format for O(1) access.
/// This is the key component that enables the array optimization performance gains.
/// </summary>
public class StateMapBuilderTests
{
    #region Basic Building Tests

    [Fact]
    public void Build_SimpleStateMachine_ShouldCreateArrayRepresentation()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "simple",
            Initial = "idle",
            States = new Dictionary<string, XStateNode>
            {
                ["idle"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["START"] = new List<XStateTransition>
                        {
                            new XStateTransition { Targets = new List<string> { "busy" } }
                        }
                    }
                },
                ["busy"] = new XStateNode()
            }
        };

        var context = new InterpreterContext();
        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, context);

        // Assert
        machine.Should().NotBeNull();
        machine.Id.Should().Be("simple");
        machine.StateCount.Should().Be(2);
        machine.Map.States.Count.Should().Be(2);
        machine.Map.Events.Count.Should().Be(1);
        machine.GetStateName(machine.InitialStateId).Should().Be("idle");
    }

    [Fact]
    public void Build_ShouldMapAllStates()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "state1",
            States = new Dictionary<string, XStateNode>
            {
                ["state1"] = new XStateNode(),
                ["state2"] = new XStateNode(),
                ["state3"] = new XStateNode()
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        machine.Map.States.Count.Should().Be(3);
        machine.StateCount.Should().Be(3);
        machine.Map.States.GetIndex("state1").Should().BeLessThan(byte.MaxValue);
        machine.Map.States.GetIndex("state2").Should().BeLessThan(byte.MaxValue);
        machine.Map.States.GetIndex("state3").Should().BeLessThan(byte.MaxValue);
    }

    [Fact]
    public void Build_ShouldMapAllEvents()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "state",
            States = new Dictionary<string, XStateNode>
            {
                ["state"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["EVENT1"] = new List<XStateTransition> { new XStateTransition() },
                        ["EVENT2"] = new List<XStateTransition> { new XStateTransition() },
                        ["EVENT3"] = new List<XStateTransition> { new XStateTransition() }
                    }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        machine.Map.Events.Count.Should().Be(3);
        machine.Map.Events.GetIndex("EVENT1").Should().BeLessThan(byte.MaxValue);
        machine.Map.Events.GetIndex("EVENT2").Should().BeLessThan(byte.MaxValue);
        machine.Map.Events.GetIndex("EVENT3").Should().BeLessThan(byte.MaxValue);
    }

    #endregion

    #region State Node Conversion Tests

    [Fact]
    public void Build_StateWithEntryActions_ShouldConvertToByteArray()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "state",
            States = new Dictionary<string, XStateNode>
            {
                ["state"] = new XStateNode
                {
                    Entry = new List<object> { "action1", "action2", "action3" }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var stateId = machine.Map.States.GetIndex("state");
        var state = machine.GetState(stateId);

        state.Should().NotBeNull();
        state!.EntryActions.Should().NotBeNull();
        state.EntryActions!.Length.Should().Be(3);

        // Verify actions are mapped to byte indices
        machine.Map.Actions.Count.Should().Be(3);
    }

    [Fact]
    public void Build_StateWithExitActions_ShouldConvertToByteArray()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "state",
            States = new Dictionary<string, XStateNode>
            {
                ["state"] = new XStateNode
                {
                    Exit = new List<object> { "onExit" }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var stateId = machine.Map.States.GetIndex("state");
        var state = machine.GetState(stateId);

        state!.ExitActions.Should().NotBeNull();
        state.ExitActions!.Length.Should().Be(1);
    }

    [Fact]
    public void Build_StateTypes_ShouldMapCorrectly()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "normal",
            States = new Dictionary<string, XStateNode>
            {
                ["normal"] = new XStateNode { Type = null },
                ["final"] = new XStateNode { Type = "final" },
                ["parallel"] = new XStateNode { Type = "parallel" }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var normalId = machine.Map.States.GetIndex("normal");
        var finalId = machine.Map.States.GetIndex("final");
        var parallelId = machine.Map.States.GetIndex("parallel");

        machine.GetState(normalId)!.StateType.Should().Be(0); // Normal
        machine.GetState(finalId)!.StateType.Should().Be(1); // Final
        machine.GetState(parallelId)!.StateType.Should().Be(2); // Parallel
    }

    #endregion

    #region Transition Conversion Tests

    [Fact]
    public void Build_SimpleTransition_ShouldConvertToArrayFormat()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "a",
            States = new Dictionary<string, XStateNode>
            {
                ["a"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["NEXT"] = new List<XStateTransition>
                        {
                            new XStateTransition { Targets = new List<string> { "b" } }
                        }
                    }
                },
                ["b"] = new XStateNode()
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var stateAId = machine.Map.States.GetIndex("a");
        var eventId = machine.Map.Events.GetIndex("NEXT");

        var transitions = machine.GetTransitions(stateAId, eventId);
        transitions.Should().NotBeNull();
        transitions!.Length.Should().Be(1);

        var targetId = machine.Map.States.GetIndex("b");
        transitions[0].TargetStateIds![0].Should().Be(targetId);
    }

    [Fact]
    public void Build_TransitionWithGuard_ShouldMapGuardToByteIndex()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "idle",
            States = new Dictionary<string, XStateNode>
            {
                ["idle"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["GO"] = new List<XStateTransition>
                        {
                            new XStateTransition
                            {
                                Targets = new List<string> { "busy" },
                                Cond = "isReady"
                            }
                        }
                    }
                },
                ["busy"] = new XStateNode()
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var stateId = machine.Map.States.GetIndex("idle");
        var eventId = machine.Map.Events.GetIndex("GO");
        var transitions = machine.GetTransitions(stateId, eventId);

        transitions![0].HasGuard.Should().BeTrue();
        transitions[0].GuardId.Should().BeLessThan(byte.MaxValue);

        machine.Map.Guards.Count.Should().BeGreaterThan(0);
        machine.Map.Guards.GetIndex("isReady").Should().Be(transitions[0].GuardId);
    }

    [Fact]
    public void Build_TransitionWithActions_ShouldMapActionsToByteIndices()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "state",
            States = new Dictionary<string, XStateNode>
            {
                ["state"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["EVENT"] = new List<XStateTransition>
                        {
                            new XStateTransition
                            {
                                Actions = new List<object> { "action1", "action2" }
                            }
                        }
                    }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var stateId = machine.Map.States.GetIndex("state");
        var eventId = machine.Map.Events.GetIndex("EVENT");
        var transitions = machine.GetTransitions(stateId, eventId);

        transitions![0].HasActions.Should().BeTrue();
        transitions[0].ActionIds!.Length.Should().Be(2);
        machine.Map.Actions.Count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void Build_MultipleTransitionsForSameEvent_ShouldAllBeConverted()
    {
        // Arrange - Multiple transitions with different guards
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "idle",
            States = new Dictionary<string, XStateNode>
            {
                ["idle"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["CLICK"] = new List<XStateTransition>
                        {
                            new XStateTransition { Targets = new List<string> { "state1" }, Cond = "guard1" },
                            new XStateTransition { Targets = new List<string> { "state2" }, Cond = "guard2" },
                            new XStateTransition { Targets = new List<string> { "state3" } } // No guard
                        }
                    }
                },
                ["state1"] = new XStateNode(),
                ["state2"] = new XStateNode(),
                ["state3"] = new XStateNode()
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var stateId = machine.Map.States.GetIndex("idle");
        var eventId = machine.Map.Events.GetIndex("CLICK");
        var transitions = machine.GetTransitions(stateId, eventId);

        transitions.Should().NotBeNull();
        transitions!.Length.Should().Be(3);
        transitions[0].HasGuard.Should().BeTrue();
        transitions[1].HasGuard.Should().BeTrue();
        transitions[2].HasGuard.Should().BeFalse();
    }

    [Fact]
    public void Build_InternalTransition_ShouldSetFlagCorrectly()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "state",
            States = new Dictionary<string, XStateNode>
            {
                ["state"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["INTERNAL"] = new List<XStateTransition>
                        {
                            new XStateTransition { Internal = true }
                        },
                        ["EXTERNAL"] = new List<XStateTransition>
                        {
                            new XStateTransition { Internal = false, Targets = new List<string> { "state" } }
                        }
                    }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var stateId = machine.Map.States.GetIndex("state");
        var internalEventId = machine.Map.Events.GetIndex("INTERNAL");
        var externalEventId = machine.Map.Events.GetIndex("EXTERNAL");

        var internalTrans = machine.GetTransitions(stateId, internalEventId);
        var externalTrans = machine.GetTransitions(stateId, externalEventId);

        internalTrans![0].IsInternal.Should().BeTrue();
        externalTrans![0].IsInternal.Should().BeFalse();
    }

    #endregion

    #region Always Transitions Tests

    [Fact]
    public void Build_AlwaysTransitions_ShouldConvertCorrectly()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "processing",
            States = new Dictionary<string, XStateNode>
            {
                ["processing"] = new XStateNode
                {
                    Always = new List<XStateTransition>
                    {
                        new XStateTransition
                        {
                            Targets = new List<string> { "idle" },
                            Cond = "isDone"
                        }
                    }
                },
                ["idle"] = new XStateNode()
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        var processingId = machine.Map.States.GetIndex("processing");
        var processingState = machine.GetState(processingId);

        processingState!.AlwaysTransitions.Should().NotBeNull();
        processingState.AlwaysTransitions!.Length.Should().Be(1);
        processingState.AlwaysTransitions[0].HasGuard.Should().BeTrue();

        var idleId = machine.Map.States.GetIndex("idle");
        processingState.AlwaysTransitions[0].TargetStateIds![0].Should().Be(idleId);
    }

    #endregion

    #region Hierarchical States Tests

    [Fact]
    public void Build_CompoundState_ShouldSetInitialState()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "parent",
            States = new Dictionary<string, XStateNode>
            {
                ["parent"] = new XStateNode
                {
                    Initial = "child1",
                    States = new Dictionary<string, XStateNode>
                    {
                        ["child1"] = new XStateNode(),
                        ["child2"] = new XStateNode()
                    }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        machine.Map.States.Count.Should().BeGreaterThan(1); // parent + children
    }

    [Fact]
    public void Build_NestedStates_ShouldUseFullyQualifiedNames()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "test",
            Initial = "parent",
            States = new Dictionary<string, XStateNode>
            {
                ["parent"] = new XStateNode
                {
                    Initial = "child",
                    States = new Dictionary<string, XStateNode>
                    {
                        ["child"] = new XStateNode()
                    }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        machine.Map.States.TryGetIndex("parent", out _).Should().BeTrue();
        machine.Map.States.TryGetIndex("parent.child", out _).Should().BeTrue();
    }

    #endregion

    #region Real-World Examples

    [Fact]
    public void Build_RobotSchedulerMachine_ShouldBuildCorrectly()
    {
        // Arrange - Simplified robot scheduler JSON
        var script = new XStateMachineScript
        {
            Id = "robotScheduler",
            Initial = "idle",
            States = new Dictionary<string, XStateNode>
            {
                ["idle"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["REGISTER_ROBOT"] = new List<XStateTransition>
                        {
                            new XStateTransition { Internal = true, Actions = new List<object> { "registerRobot" } }
                        },
                        ["UPDATE_ROBOT_STATE"] = new List<XStateTransition>
                        {
                            new XStateTransition { Internal = true, Actions = new List<object> { "updateRobotState" } }
                        },
                        ["REQUEST_TRANSFER"] = new List<XStateTransition>
                        {
                            new XStateTransition
                            {
                                Targets = new List<string> { "processing" },
                                Actions = new List<object> { "queueOrAssignTransfer", "processTransfers" }
                            }
                        }
                    }
                },
                ["processing"] = new XStateNode
                {
                    On = new Dictionary<string, List<XStateTransition>>
                    {
                        ["REGISTER_ROBOT"] = new List<XStateTransition>
                        {
                            new XStateTransition { Internal = true, Actions = new List<object> { "registerRobot" } }
                        },
                        ["UPDATE_ROBOT_STATE"] = new List<XStateTransition>
                        {
                            new XStateTransition { Internal = true, Actions = new List<object> { "updateRobotState" } }
                        },
                        ["REQUEST_TRANSFER"] = new List<XStateTransition>
                        {
                            new XStateTransition { Internal = true, Actions = new List<object> { "queueOrAssignTransfer" } }
                        }
                    },
                    Always = new List<XStateTransition>
                    {
                        new XStateTransition
                        {
                            Targets = new List<string> { "idle" },
                            Cond = "hasNoPendingWork"
                        }
                    }
                }
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        machine.Id.Should().Be("robotScheduler");
        machine.StateCount.Should().Be(2);

        // Verify state mapping
        machine.Map.States.Count.Should().Be(2);
        var idleId = machine.Map.States.GetIndex("idle");
        var processingId = machine.Map.States.GetIndex("processing");
        idleId.Should().BeLessThan(byte.MaxValue);
        processingId.Should().BeLessThan(byte.MaxValue);

        // Verify event mapping
        machine.Map.Events.Count.Should().Be(3);
        machine.Map.Events.GetIndex("REGISTER_ROBOT").Should().BeLessThan(byte.MaxValue);
        machine.Map.Events.GetIndex("UPDATE_ROBOT_STATE").Should().BeLessThan(byte.MaxValue);
        machine.Map.Events.GetIndex("REQUEST_TRANSFER").Should().BeLessThan(byte.MaxValue);

        // Verify action mapping
        machine.Map.Actions.Count.Should().Be(4);
        machine.Map.Actions.GetIndex("registerRobot").Should().BeLessThan(byte.MaxValue);
        machine.Map.Actions.GetIndex("updateRobotState").Should().BeLessThan(byte.MaxValue);
        machine.Map.Actions.GetIndex("queueOrAssignTransfer").Should().BeLessThan(byte.MaxValue);
        machine.Map.Actions.GetIndex("processTransfers").Should().BeLessThan(byte.MaxValue);

        // Verify guard mapping
        machine.Map.Guards.Count.Should().Be(1);
        machine.Map.Guards.GetIndex("hasNoPendingWork").Should().BeLessThan(byte.MaxValue);

        // Verify processing state has always transition
        var processingState = machine.GetState(processingId);
        processingState!.AlwaysTransitions.Should().NotBeNull();
        processingState.AlwaysTransitions!.Length.Should().Be(1);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Build_EmptyStateMachine_ShouldHandleGracefully()
    {
        // Arrange
        var script = new XStateMachineScript
        {
            Id = "empty",
            Initial = "only",
            States = new Dictionary<string, XStateNode>
            {
                ["only"] = new XStateNode()
            }
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        machine.Should().NotBeNull();
        machine.StateCount.Should().Be(1);
        machine.Map.States.Count.Should().Be(1);
        machine.Map.Events.Count.Should().Be(0);
    }

    [Fact]
    public void Build_ManyStates_ShouldNotExceedByteLimit()
    {
        // Arrange - Create machine with many states (but within byte limit)
        var states = new Dictionary<string, XStateNode>();
        for (int i = 0; i < 200; i++)
        {
            states[$"state{i}"] = new XStateNode();
        }

        var script = new XStateMachineScript
        {
            Id = "large",
            Initial = "state0",
            States = states
        };

        var builder = new StateMapBuilder();

        // Act
        var machine = builder.Build(script, new InterpreterContext());

        // Assert
        machine.StateCount.Should().Be(200);
        machine.Map.States.Count.Should().Be(200);

        // Verify all states are mapped to valid byte indices
        for (int i = 0; i < 200; i++)
        {
            var stateId = machine.Map.States.GetIndex($"state{i}");
            stateId.Should().BeLessThan(byte.MaxValue);
        }
    }

    #endregion
}
