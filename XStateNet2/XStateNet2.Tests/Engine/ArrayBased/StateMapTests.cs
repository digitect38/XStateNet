using FluentAssertions;
using Xunit;
using XStateNet2.Core.Engine.ArrayBased;

namespace XStateNet2.Tests.Engine.ArrayBased;

/// <summary>
/// Comprehensive tests for StateMap - the bidirectional string↔byte mapping system.
/// Tests O(1) lookup performance foundation of the array optimization.
/// </summary>
public class StateMapTests
{
    #region Basic Mapping Tests

    [Fact]
    public void Constructor_ShouldCreateBidirectionalMapping()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["idle"] = 0,
            ["busy"] = 1,
            ["error"] = 2
        };

        // Act
        var stateMap = new StateMap(mapping);

        // Assert
        stateMap.GetIndex("idle").Should().Be(0);
        stateMap.GetIndex("busy").Should().Be(1);
        stateMap.GetIndex("error").Should().Be(2);

        stateMap.GetString(0).Should().Be("idle");
        stateMap.GetString(1).Should().Be("busy");
        stateMap.GetString(2).Should().Be("error");
    }

    [Fact]
    public void Count_ShouldReturnCorrectNumberOfMappings()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["state1"] = 0,
            ["state2"] = 1,
            ["state3"] = 2,
            ["state4"] = 3
        };

        // Act
        var stateMap = new StateMap(mapping);

        // Assert
        stateMap.Count.Should().Be(4);
    }

    [Fact]
    public void EmptyMapping_ShouldWork()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>();

        // Act
        var stateMap = new StateMap(mapping);

        // Assert
        stateMap.Count.Should().Be(0);
        stateMap.GetIndex("anything").Should().Be(byte.MaxValue);
        stateMap.GetString(0).Should().Be(string.Empty);
    }

    #endregion

    #region String→Byte Lookup Tests

    [Fact]
    public void GetIndex_WithValidString_ShouldReturnCorrectIndex()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["PICKUP"] = 0,
            ["DROP"] = 1,
            ["MOVE"] = 2
        };
        var stateMap = new StateMap(mapping);

        // Act & Assert
        stateMap.GetIndex("PICKUP").Should().Be(0);
        stateMap.GetIndex("DROP").Should().Be(1);
        stateMap.GetIndex("MOVE").Should().Be(2);
    }

    [Fact]
    public void GetIndex_WithInvalidString_ShouldReturnByteMaxValue()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["valid"] = 0
        };
        var stateMap = new StateMap(mapping);

        // Act
        var index = stateMap.GetIndex("invalid");

        // Assert
        index.Should().Be(byte.MaxValue);
    }

    [Fact]
    public void GetIndex_WithNull_ShouldReturnByteMaxValue()
    {
        // Arrange
        var mapping = new Dictionary<string, byte> { ["valid"] = 0 };
        var stateMap = new StateMap(mapping);

        // Act
        var index = stateMap.GetIndex(null!);

        // Assert
        index.Should().Be(byte.MaxValue);
    }

    [Fact]
    public void GetIndex_CaseSensitive_ShouldNotMatch()
    {
        // Arrange
        var mapping = new Dictionary<string, byte> { ["Event"] = 0 };
        var stateMap = new StateMap(mapping);

        // Act & Assert
        stateMap.GetIndex("Event").Should().Be(0);
        stateMap.GetIndex("event").Should().Be(byte.MaxValue);
        stateMap.GetIndex("EVENT").Should().Be(byte.MaxValue);
    }

    #endregion

    #region Byte→String Lookup Tests

    [Fact]
    public void GetString_WithValidIndex_ShouldReturnCorrectString()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["idle"] = 0,
            ["processing"] = 1,
            ["done"] = 2
        };
        var stateMap = new StateMap(mapping);

        // Act & Assert
        stateMap.GetString(0).Should().Be("idle");
        stateMap.GetString(1).Should().Be("processing");
        stateMap.GetString(2).Should().Be("done");
    }

    [Fact]
    public void GetString_WithInvalidIndex_ShouldReturnEmptyString()
    {
        // Arrange
        var mapping = new Dictionary<string, byte> { ["valid"] = 0 };
        var stateMap = new StateMap(mapping);

        // Act & Assert
        stateMap.GetString(99).Should().Be(string.Empty);
        stateMap.GetString(byte.MaxValue).Should().Be(string.Empty);
    }

    [Fact]
    public void GetString_WithOutOfRangeIndex_ShouldReturnEmptyString()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["state0"] = 0,
            ["state1"] = 1
        };
        var stateMap = new StateMap(mapping);

        // Act
        var result = stateMap.GetString(10); // Way out of range

        // Assert
        result.Should().Be(string.Empty);
    }

    #endregion

    #region TryGetIndex Tests

    [Fact]
    public void TryGetIndex_WithValidString_ShouldReturnTrueAndCorrectIndex()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["hasGuard"] = 5,
            ["noGuard"] = 10
        };
        var stateMap = new StateMap(mapping);

        // Act
        var found = stateMap.TryGetIndex("hasGuard", out var index);

        // Assert
        found.Should().BeTrue();
        index.Should().Be(5);
    }

    [Fact]
    public void TryGetIndex_WithInvalidString_ShouldReturnFalse()
    {
        // Arrange
        var mapping = new Dictionary<string, byte> { ["valid"] = 0 };
        var stateMap = new StateMap(mapping);

        // Act
        var found = stateMap.TryGetIndex("invalid", out var index);

        // Assert
        found.Should().BeFalse();
        index.Should().Be(0); // Default value for out parameter
    }

    [Fact]
    public void TryGetIndex_MultipleCallsForSameKey_ShouldBeConsistent()
    {
        // Arrange
        var mapping = new Dictionary<string, byte> { ["key"] = 42 };
        var stateMap = new StateMap(mapping);

        // Act
        var found1 = stateMap.TryGetIndex("key", out var index1);
        var found2 = stateMap.TryGetIndex("key", out var index2);

        // Assert
        found1.Should().BeTrue();
        found2.Should().BeTrue();
        index1.Should().Be(42);
        index2.Should().Be(42);
    }

    #endregion

    #region Edge Cases and Performance

    [Fact]
    public void LargeMapping_ShouldHandleCorrectly()
    {
        // Arrange - Create mapping with many entries
        var mapping = new Dictionary<string, byte>();
        for (byte i = 0; i < 200; i++)
        {
            mapping[$"state_{i}"] = i;
        }

        // Act
        var stateMap = new StateMap(mapping);

        // Assert
        stateMap.Count.Should().Be(200);
        stateMap.GetIndex("state_0").Should().Be(0);
        stateMap.GetIndex("state_100").Should().Be(100);
        stateMap.GetIndex("state_199").Should().Be(199);
        stateMap.GetString(0).Should().Be("state_0");
        stateMap.GetString(199).Should().Be("state_199");
    }

    [Fact]
    public void MaxByteValue_ShouldBeReservedForInvalid()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["valid1"] = 0,
            ["valid2"] = 254 // Max valid is 254, 255 is reserved
        };
        var stateMap = new StateMap(mapping);

        // Act & Assert
        stateMap.GetIndex("valid1").Should().Be(0);
        stateMap.GetIndex("valid2").Should().Be(254);
        stateMap.GetIndex("invalid").Should().Be(byte.MaxValue); // 255 = invalid marker
    }

    [Fact]
    public void NonContiguousIndices_ShouldWork()
    {
        // Arrange - Indices don't need to be 0,1,2,3...
        var mapping = new Dictionary<string, byte>
        {
            ["stateA"] = 5,
            ["stateB"] = 10,
            ["stateC"] = 20
        };

        // Act
        var stateMap = new StateMap(mapping);

        // Assert
        stateMap.GetIndex("stateA").Should().Be(5);
        stateMap.GetIndex("stateB").Should().Be(10);
        stateMap.GetIndex("stateC").Should().Be(20);
        stateMap.GetString(5).Should().Be("stateA");
        stateMap.GetString(10).Should().Be("stateB");
        stateMap.GetString(20).Should().Be("stateC");
    }

    [Fact]
    public void SpecialCharactersInStrings_ShouldWork()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["state.child"] = 0,
            ["Robot 1"] = 1,
            ["_private"] = 2,
            ["$special"] = 3
        };

        // Act
        var stateMap = new StateMap(mapping);

        // Assert
        stateMap.GetIndex("state.child").Should().Be(0);
        stateMap.GetIndex("Robot 1").Should().Be(1);
        stateMap.GetIndex("_private").Should().Be(2);
        stateMap.GetIndex("$special").Should().Be(3);
    }

    #endregion

    #region Bidirectional Consistency Tests

    [Fact]
    public void RoundTrip_StringToIndexToString_ShouldBeIdentical()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["original"] = 42
        };
        var stateMap = new StateMap(mapping);

        // Act
        var index = stateMap.GetIndex("original");
        var roundTrip = stateMap.GetString(index);

        // Assert
        roundTrip.Should().Be("original");
    }

    [Fact]
    public void RoundTrip_IndexToStringToIndex_ShouldBeIdentical()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["test"] = 7
        };
        var stateMap = new StateMap(mapping);

        // Act
        var str = stateMap.GetString(7);
        var roundTrip = stateMap.GetIndex(str);

        // Assert
        roundTrip.Should().Be(7);
    }

    [Fact]
    public void MultipleMappings_AllShouldBeIndependent()
    {
        // Arrange
        var mapping = new Dictionary<string, byte>
        {
            ["A"] = 0,
            ["B"] = 1,
            ["C"] = 2
        };
        var stateMap = new StateMap(mapping);

        // Act & Assert - Changing one shouldn't affect others
        stateMap.GetIndex("A").Should().Be(0);
        stateMap.GetIndex("B").Should().Be(1);
        stateMap.GetIndex("C").Should().Be(2);

        stateMap.GetString(0).Should().Be("A");
        stateMap.GetString(1).Should().Be("B");
        stateMap.GetString(2).Should().Be("C");
    }

    #endregion
}

/// <summary>
/// Tests for StateMachineMap - the complete mapping system for array-based optimization.
/// </summary>
public class StateMachineMapTests
{
    [Fact]
    public void Constructor_ShouldInitializeAllMaps()
    {
        // Arrange
        var states = new Dictionary<string, byte> { ["idle"] = 0, ["busy"] = 1 };
        var events = new Dictionary<string, byte> { ["START"] = 0, ["STOP"] = 1 };
        var actions = new Dictionary<string, byte> { ["onStart"] = 0, ["onStop"] = 1 };
        var guards = new Dictionary<string, byte> { ["canStart"] = 0 };

        // Act
        var map = new StateMachineMap(states, events, actions, guards);

        // Assert
        map.States.Should().NotBeNull();
        map.Events.Should().NotBeNull();
        map.Actions.Should().NotBeNull();
        map.Guards.Should().NotBeNull();

        map.States.Count.Should().Be(2);
        map.Events.Count.Should().Be(2);
        map.Actions.Count.Should().Be(2);
        map.Guards.Count.Should().Be(1);
    }

    [Fact]
    public void AllMaps_ShouldBeIndependent()
    {
        // Arrange - Same string in different maps should have different indices
        var states = new Dictionary<string, byte> { ["test"] = 0 };
        var events = new Dictionary<string, byte> { ["test"] = 5 };
        var actions = new Dictionary<string, byte> { ["test"] = 10 };
        var guards = new Dictionary<string, byte> { ["test"] = 15 };

        // Act
        var map = new StateMachineMap(states, events, actions, guards);

        // Assert - Same string "test" mapped to different indices
        map.States.GetIndex("test").Should().Be(0);
        map.Events.GetIndex("test").Should().Be(5);
        map.Actions.GetIndex("test").Should().Be(10);
        map.Guards.GetIndex("test").Should().Be(15);
    }

    [Fact]
    public void EmptyMaps_ShouldWork()
    {
        // Arrange
        var empty = new Dictionary<string, byte>();

        // Act
        var map = new StateMachineMap(empty, empty, empty, empty);

        // Assert
        map.States.Count.Should().Be(0);
        map.Events.Count.Should().Be(0);
        map.Actions.Count.Should().Be(0);
        map.Guards.Count.Should().Be(0);
    }

    [Fact]
    public void RealWorldExample_RobotScheduler_ShouldMapCorrectly()
    {
        // Arrange - Realistic robot scheduler state machine
        var states = new Dictionary<string, byte>
        {
            ["idle"] = 0,
            ["processing"] = 1
        };
        var events = new Dictionary<string, byte>
        {
            ["REGISTER_ROBOT"] = 0,
            ["UPDATE_STATE"] = 1,
            ["REQUEST_TRANSFER"] = 2
        };
        var actions = new Dictionary<string, byte>
        {
            ["registerRobot"] = 0,
            ["updateRobotState"] = 1,
            ["queueOrAssignTransfer"] = 2,
            ["processTransfers"] = 3
        };
        var guards = new Dictionary<string, byte>
        {
            ["hasNoPendingWork"] = 0,
            ["hasPendingWork"] = 1
        };

        // Act
        var map = new StateMachineMap(states, events, actions, guards);

        // Assert - Verify all mappings work correctly
        map.States.GetIndex("idle").Should().Be(0);
        map.States.GetIndex("processing").Should().Be(1);

        map.Events.GetIndex("REGISTER_ROBOT").Should().Be(0);
        map.Events.GetIndex("UPDATE_STATE").Should().Be(1);
        map.Events.GetIndex("REQUEST_TRANSFER").Should().Be(2);

        map.Actions.GetIndex("registerRobot").Should().Be(0);
        map.Actions.GetIndex("processTransfers").Should().Be(3);

        map.Guards.GetIndex("hasNoPendingWork").Should().Be(0);
        map.Guards.GetIndex("hasPendingWork").Should().Be(1);
    }
}
