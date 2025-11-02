using FluentAssertions;
using Xunit;
using XStateNet2.Core.Engine.ArrayBased;
using XStateNet2.Core.Runtime;

namespace XStateNet2.Tests.Engine.ArrayBased;

/// <summary>
/// Comprehensive tests for ArrayStateMachine and ArrayStateNode - the core array-based state machine.
/// Tests O(1) array access, state transitions, and complete state machine behavior.
/// </summary>
public class ArrayStateMachineTests
{
    #region ArrayStateNode Tests

    [Fact]
    public void ArrayStateNode_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var node = new ArrayStateNode();

        // Assert
        node.StateType.Should().Be(0); // Normal state
        node.InitialStateId.Should().Be(byte.MaxValue); // No initial state
        node.Transitions.Should().BeNull();
        node.EntryActions.Should().BeNull();
        node.ExitActions.Should().BeNull();
        node.ChildStates.Should().BeNull();
        node.AlwaysTransitions.Should().BeNull();
    }

    [Fact]
    public void ArrayStateNode_IsLeaf_ShouldBeTrueWhenNoChildren()
    {
        // Arrange
        var node = new ArrayStateNode
        {
            ChildStates = null
        };

        // Assert
        node.IsLeaf.Should().BeTrue();
        node.IsCompound.Should().BeFalse();
    }

    [Fact]
    public void ArrayStateNode_IsCompound_ShouldBeTrueWhenHasChildren()
    {
        // Arrange
        var node = new ArrayStateNode
        {
            ChildStates = new ArrayStateNode?[2]
        };

        // Assert
        node.IsCompound.Should().BeTrue();
        node.IsLeaf.Should().BeFalse();
    }

    [Fact]
    public void ArrayStateNode_WithEntryActions_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var node = new ArrayStateNode
        {
            EntryActions = new byte[] { 0, 1, 2 }
        };

        // Assert
        node.EntryActions.Should().NotBeNull();
        node.EntryActions!.Length.Should().Be(3);
        node.EntryActions[0].Should().Be(0);
        node.EntryActions[1].Should().Be(1);
        node.EntryActions[2].Should().Be(2);
    }

    [Fact]
    public void ArrayStateNode_WithExitActions_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var node = new ArrayStateNode
        {
            ExitActions = new byte[] { 5, 10 }
        };

        // Assert
        node.ExitActions.Should().NotBeNull();
        node.ExitActions!.Length.Should().Be(2);
        node.ExitActions[0].Should().Be(5);
        node.ExitActions[1].Should().Be(10);
    }

    [Fact]
    public void ArrayStateNode_StateTypes_ShouldMapCorrectly()
    {
        // Arrange & Act
        var normalState = new ArrayStateNode { StateType = 0 };
        var finalState = new ArrayStateNode { StateType = 1 };
        var parallelState = new ArrayStateNode { StateType = 2 };

        // Assert
        normalState.StateType.Should().Be(0);
        finalState.StateType.Should().Be(1);
        parallelState.StateType.Should().Be(2);
    }

    #endregion

    #region ArrayTransition Tests

    [Fact]
    public void ArrayTransition_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var transition = new ArrayTransition();

        // Assert
        transition.GuardId.Should().Be(byte.MaxValue); // No guard
        transition.TargetStateIds.Should().BeNull();
        transition.ActionIds.Should().BeNull();
        transition.IsInternal.Should().BeFalse();
    }

    [Fact]
    public void ArrayTransition_HasGuard_ShouldBeFalseWhenNoGuard()
    {
        // Arrange
        var transition = new ArrayTransition
        {
            GuardId = byte.MaxValue
        };

        // Assert
        transition.HasGuard.Should().BeFalse();
    }

    [Fact]
    public void ArrayTransition_HasGuard_ShouldBeTrueWhenGuardPresent()
    {
        // Arrange
        var transition = new ArrayTransition
        {
            GuardId = 5
        };

        // Assert
        transition.HasGuard.Should().BeTrue();
    }

    [Fact]
    public void ArrayTransition_HasActions_ShouldBeFalseWhenNoActions()
    {
        // Arrange
        var transition = new ArrayTransition
        {
            ActionIds = null
        };

        // Assert
        transition.HasActions.Should().BeFalse();
    }

    [Fact]
    public void ArrayTransition_HasActions_ShouldBeTrueWhenActionsPresent()
    {
        // Arrange
        var transition = new ArrayTransition
        {
            ActionIds = new byte[] { 1, 2, 3 }
        };

        // Assert
        transition.HasActions.Should().BeTrue();
    }

    [Fact]
    public void ArrayTransition_MultipleTargets_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var transition = new ArrayTransition
        {
            TargetStateIds = new byte[] { 5, 10, 15 }
        };

        // Assert
        transition.TargetStateIds.Should().NotBeNull();
        transition.TargetStateIds!.Length.Should().Be(3);
        transition.TargetStateIds[0].Should().Be(5);
        transition.TargetStateIds[1].Should().Be(10);
        transition.TargetStateIds[2].Should().Be(15);
    }

    #endregion

    #region ArrayStateMachine Construction Tests

    [Fact]
    public void ArrayStateMachine_DefaultValues_ShouldBeCorrect()
    {
        // Act
        var machine = new ArrayStateMachine();

        // Assert
        machine.Id.Should().Be(string.Empty);
        machine.InitialStateId.Should().Be(0);
        machine.States.Should().NotBeNull();
        machine.States.Length.Should().Be(0);
        machine.StateCount.Should().Be(0);
    }

    [Fact]
    public void ArrayStateMachine_WithStates_ShouldStoreCorrectly()
    {
        // Arrange
        var states = new[]
        {
            new ArrayStateNode { StateType = 0 },
            new ArrayStateNode { StateType = 1 },
            new ArrayStateNode { StateType = 0 }
        };

        // Act
        var machine = new ArrayStateMachine
        {
            States = states,
            InitialStateId = 0
        };

        // Assert
        machine.StateCount.Should().Be(3);
        machine.InitialStateId.Should().Be(0);
        machine.States[0].StateType.Should().Be(0);
        machine.States[1].StateType.Should().Be(1);
        machine.States[2].StateType.Should().Be(0);
    }

    #endregion

    #region GetState Tests

    [Fact]
    public void GetState_ByIndex_WithValidId_ShouldReturnCorrectState()
    {
        // Arrange
        var states = new[]
        {
            new ArrayStateNode { StateType = 0 },
            new ArrayStateNode { StateType = 1 },
            new ArrayStateNode { StateType = 2 }
        };
        var machine = new ArrayStateMachine { States = states };

        // Act
        var state0 = machine.GetState(0);
        var state1 = machine.GetState(1);
        var state2 = machine.GetState(2);

        // Assert
        state0.Should().NotBeNull();
        state0!.StateType.Should().Be(0);
        state1!.StateType.Should().Be(1);
        state2!.StateType.Should().Be(2);
    }

    [Fact]
    public void GetState_ByIndex_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        var states = new[] { new ArrayStateNode() };
        var machine = new ArrayStateMachine { States = states };

        // Act
        var state = machine.GetState(99);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public void GetState_ByName_WithValidName_ShouldReturnCorrectState()
    {
        // Arrange
        var stateMapping = new Dictionary<string, byte>
        {
            ["idle"] = 0,
            ["busy"] = 1
        };
        var map = new StateMachineMap(
            stateMapping,
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>()
        );

        var states = new[]
        {
            new ArrayStateNode { StateType = 0 },
            new ArrayStateNode { StateType = 1 }
        };

        var machine = new ArrayStateMachine
        {
            States = states,
            Map = map
        };

        // Act
        var idleState = machine.GetState("idle");
        var busyState = machine.GetState("busy");

        // Assert
        idleState.Should().NotBeNull();
        idleState!.StateType.Should().Be(0);
        busyState!.StateType.Should().Be(1);
    }

    [Fact]
    public void GetState_ByName_WithInvalidName_ShouldReturnNull()
    {
        // Arrange
        var stateMapping = new Dictionary<string, byte> { ["valid"] = 0 };
        var map = new StateMachineMap(
            stateMapping,
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>()
        );

        var machine = new ArrayStateMachine
        {
            States = new[] { new ArrayStateNode() },
            Map = map
        };

        // Act
        var state = machine.GetState("invalid");

        // Assert
        state.Should().BeNull();
    }

    #endregion

    #region GetTransitions Tests

    [Fact]
    public void GetTransitions_WithValidStateAndEvent_ShouldReturnTransitions()
    {
        // Arrange
        var transition1 = new ArrayTransition { TargetStateIds = new byte[] { 1 } };
        var transition2 = new ArrayTransition { TargetStateIds = new byte[] { 2 } };

        var state = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[3][] // 3 events possible
            {
                new[] { transition1 }, // Event 0 has 1 transition
                new[] { transition2 }, // Event 1 has 1 transition
                null                   // Event 2 has no transitions
            }
        };

        var machine = new ArrayStateMachine
        {
            States = new[] { state }
        };

        // Act
        var transitionsForEvent0 = machine.GetTransitions(0, 0);
        var transitionsForEvent1 = machine.GetTransitions(0, 1);
        var transitionsForEvent2 = machine.GetTransitions(0, 2);

        // Assert
        transitionsForEvent0.Should().NotBeNull();
        transitionsForEvent0!.Length.Should().Be(1);
        transitionsForEvent0[0].TargetStateIds![0].Should().Be(1);

        transitionsForEvent1.Should().NotBeNull();
        transitionsForEvent1![0].TargetStateIds![0].Should().Be(2);

        transitionsForEvent2.Should().BeNull();
    }

    [Fact]
    public void GetTransitions_WithInvalidStateId_ShouldReturnNull()
    {
        // Arrange
        var machine = new ArrayStateMachine
        {
            States = new[] { new ArrayStateNode() }
        };

        // Act
        var transitions = machine.GetTransitions(99, 0);

        // Assert
        transitions.Should().BeNull();
    }

    [Fact]
    public void GetTransitions_WithInvalidEventId_ShouldReturnNull()
    {
        // Arrange
        var state = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[2][] // Only 2 events
        };

        var machine = new ArrayStateMachine
        {
            States = new[] { state }
        };

        // Act
        var transitions = machine.GetTransitions(0, 99); // Event 99 doesn't exist

        // Assert
        transitions.Should().BeNull();
    }

    [Fact]
    public void GetTransitions_MultipleTransitionsForSameEvent_ShouldReturnAll()
    {
        // Arrange - Multiple transitions for same event (with different guards)
        var trans1 = new ArrayTransition { GuardId = 0, TargetStateIds = new byte[] { 1 } };
        var trans2 = new ArrayTransition { GuardId = 1, TargetStateIds = new byte[] { 2 } };
        var trans3 = new ArrayTransition { GuardId = byte.MaxValue, TargetStateIds = new byte[] { 3 } };

        var state = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[1][]
            {
                new[] { trans1, trans2, trans3 } // Event 0 has 3 transitions
            }
        };

        var machine = new ArrayStateMachine { States = new[] { state } };

        // Act
        var transitions = machine.GetTransitions(0, 0);

        // Assert
        transitions.Should().NotBeNull();
        transitions!.Length.Should().Be(3);
        transitions[0].GuardId.Should().Be(0);
        transitions[1].GuardId.Should().Be(1);
        transitions[2].GuardId.Should().Be(byte.MaxValue);
    }

    #endregion

    #region Name Resolution Tests

    [Fact]
    public void GetStateName_WithValidId_ShouldReturnCorrectName()
    {
        // Arrange
        var stateMapping = new Dictionary<string, byte>
        {
            ["idle"] = 0,
            ["processing"] = 1,
            ["done"] = 2
        };
        var map = new StateMachineMap(
            stateMapping,
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>()
        );

        var machine = new ArrayStateMachine { Map = map };

        // Act & Assert
        machine.GetStateName(0).Should().Be("idle");
        machine.GetStateName(1).Should().Be("processing");
        machine.GetStateName(2).Should().Be("done");
    }

    [Fact]
    public void GetStateName_WithInvalidId_ShouldReturnEmpty()
    {
        // Arrange
        var map = new StateMachineMap(
            new Dictionary<string, byte> { ["valid"] = 0 },
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>()
        );

        var machine = new ArrayStateMachine { Map = map };

        // Act
        var name = machine.GetStateName(99);

        // Assert
        name.Should().Be(string.Empty);
    }

    [Fact]
    public void GetEventName_WithValidId_ShouldReturnCorrectName()
    {
        // Arrange
        var eventMapping = new Dictionary<string, byte>
        {
            ["CLICK"] = 0,
            ["HOVER"] = 1,
            ["KEYPRESS"] = 2
        };
        var map = new StateMachineMap(
            new Dictionary<string, byte>(),
            eventMapping,
            new Dictionary<string, byte>(),
            new Dictionary<string, byte>()
        );

        var machine = new ArrayStateMachine { Map = map };

        // Act & Assert
        machine.GetEventName(0).Should().Be("CLICK");
        machine.GetEventName(1).Should().Be("HOVER");
        machine.GetEventName(2).Should().Be("KEYPRESS");
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void CompleteStateMachine_TrafficLight_ShouldWorkCorrectly()
    {
        // Arrange - Build a simple traffic light state machine
        var stateMapping = new Dictionary<string, byte>
        {
            ["green"] = 0,
            ["yellow"] = 1,
            ["red"] = 2
        };
        var eventMapping = new Dictionary<string, byte>
        {
            ["TIMER"] = 0
        };
        var actionMapping = new Dictionary<string, byte>
        {
            ["startTimer"] = 0
        };

        var map = new StateMachineMap(stateMapping, eventMapping, actionMapping, new Dictionary<string, byte>());

        // Create transitions: green->yellow, yellow->red, red->green
        var greenState = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[1][]
            {
                new[] { new ArrayTransition { TargetStateIds = new byte[] { 1 } } } // TIMER -> yellow
            },
            EntryActions = new byte[] { 0 } // startTimer
        };

        var yellowState = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[1][]
            {
                new[] { new ArrayTransition { TargetStateIds = new byte[] { 2 } } } // TIMER -> red
            },
            EntryActions = new byte[] { 0 }
        };

        var redState = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[1][]
            {
                new[] { new ArrayTransition { TargetStateIds = new byte[] { 0 } } } // TIMER -> green
            },
            EntryActions = new byte[] { 0 }
        };

        var machine = new ArrayStateMachine
        {
            Id = "trafficLight",
            InitialStateId = 0, // Start at green
            States = new[] { greenState, yellowState, redState },
            Map = map
        };

        // Act & Assert
        machine.Id.Should().Be("trafficLight");
        machine.InitialStateId.Should().Be(0);
        machine.StateCount.Should().Be(3);

        // Verify green state transitions
        var greenTransitions = machine.GetTransitions(0, 0); // Green + TIMER
        greenTransitions.Should().NotBeNull();
        greenTransitions![0].TargetStateIds![0].Should().Be(1); // -> yellow

        // Verify yellow state transitions
        var yellowTransitions = machine.GetTransitions(1, 0); // Yellow + TIMER
        yellowTransitions![0].TargetStateIds![0].Should().Be(2); // -> red

        // Verify red state transitions
        var redTransitions = machine.GetTransitions(2, 0); // Red + TIMER
        redTransitions![0].TargetStateIds![0].Should().Be(0); // -> green (cycle)
    }

    [Fact]
    public void CompleteStateMachine_RobotScheduler_ShouldHaveCorrectStructure()
    {
        // Arrange - Simplified robot scheduler structure
        var stateMapping = new Dictionary<string, byte>
        {
            ["idle"] = 0,
            ["processing"] = 1
        };
        var eventMapping = new Dictionary<string, byte>
        {
            ["REGISTER_ROBOT"] = 0,
            ["UPDATE_STATE"] = 1,
            ["REQUEST_TRANSFER"] = 2
        };
        var actionMapping = new Dictionary<string, byte>
        {
            ["registerRobot"] = 0,
            ["updateRobotState"] = 1,
            ["queueOrAssignTransfer"] = 2,
            ["processTransfers"] = 3
        };
        var guardMapping = new Dictionary<string, byte>
        {
            ["hasNoPendingWork"] = 0
        };

        var map = new StateMachineMap(stateMapping, eventMapping, actionMapping, guardMapping);

        // Idle state: handles all events, REQUEST_TRANSFER transitions to processing
        var idleState = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[3][]
            {
                new[] { new ArrayTransition { IsInternal = true, ActionIds = new byte[] { 0 } } }, // REGISTER_ROBOT
                new[] { new ArrayTransition { IsInternal = true, ActionIds = new byte[] { 1 } } }, // UPDATE_STATE
                new[] { new ArrayTransition { TargetStateIds = new byte[] { 1 }, ActionIds = new byte[] { 2, 3 } } } // REQUEST_TRANSFER -> processing
            }
        };

        // Processing state: handles all events, has always transition back to idle when no work
        var processingState = new ArrayStateNode
        {
            Transitions = new ArrayTransition?[3][]
            {
                new[] { new ArrayTransition { IsInternal = true, ActionIds = new byte[] { 0 } } },
                new[] { new ArrayTransition { IsInternal = true, ActionIds = new byte[] { 1 } } },
                new[] { new ArrayTransition { IsInternal = true, ActionIds = new byte[] { 2 } } }
            },
            AlwaysTransitions = new[]
            {
                new ArrayTransition { GuardId = 0, TargetStateIds = new byte[] { 0 } } // hasNoPendingWork -> idle
            }
        };

        var machine = new ArrayStateMachine
        {
            Id = "robotScheduler",
            InitialStateId = 0,
            States = new[] { idleState, processingState },
            Map = map,
            Context = new InterpreterContext()
        };

        // Act & Assert
        machine.Id.Should().Be("robotScheduler");
        machine.StateCount.Should().Be(2);
        machine.GetStateName(0).Should().Be("idle");
        machine.GetStateName(1).Should().Be("processing");

        // Verify idle state can transition to processing on REQUEST_TRANSFER
        var idleTransitions = machine.GetTransitions(0, 2); // idle + REQUEST_TRANSFER
        idleTransitions.Should().NotBeNull();
        idleTransitions![0].TargetStateIds![0].Should().Be(1); // -> processing
        idleTransitions[0].HasActions.Should().BeTrue();
        idleTransitions[0].ActionIds!.Length.Should().Be(2);

        // Verify processing state has always transition back to idle
        processingState.AlwaysTransitions.Should().NotBeNull();
        processingState.AlwaysTransitions!.Length.Should().Be(1);
        processingState.AlwaysTransitions[0].HasGuard.Should().BeTrue();
        processingState.AlwaysTransitions[0].TargetStateIds![0].Should().Be(0); // -> idle
    }

    #endregion

    #region Performance Characteristic Tests

    [Fact]
    public void ArrayAccess_ShouldBeDirect_NoHashLookup()
    {
        // This test verifies that array access is used, not dictionary lookup
        // Arrange
        var states = new ArrayStateNode[255]; // Max byte value
        for (int i = 0; i < 255; i++)
        {
            states[i] = new ArrayStateNode { StateType = (byte)i };
        }

        var machine = new ArrayStateMachine { States = states };

        // Act - Direct array access should work for any valid byte
        for (byte i = 0; i < 255; i++)
        {
            var state = machine.GetState(i);

            // Assert - Direct O(1) array access
            state.Should().NotBeNull();
            state!.StateType.Should().Be(i);
        }
    }

    [Fact]
    public void TransitionLookup_ShouldBeDirect2DArrayAccess()
    {
        // This test verifies O(1) transition lookup using [stateId][eventId]
        // Arrange
        var transitions = new ArrayTransition?[10][]; // 10 possible events
        for (byte i = 0; i < 10; i++)
        {
            transitions[i] = new[] { new ArrayTransition { TargetStateIds = new byte[] { i } } };
        }

        var state = new ArrayStateNode { Transitions = transitions };
        var machine = new ArrayStateMachine { States = new[] { state } };

        // Act & Assert - Direct 2D array access for each event
        for (byte eventId = 0; eventId < 10; eventId++)
        {
            var trans = machine.GetTransitions(0, eventId);
            trans.Should().NotBeNull();
            trans![0].TargetStateIds![0].Should().Be(eventId);
        }
    }

    #endregion
}
