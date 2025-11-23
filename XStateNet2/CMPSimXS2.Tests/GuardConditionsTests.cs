using Xunit;
using CMPSimXS2.Parallel;

namespace CMPSimXS2.Tests;

/// <summary>
/// Unit tests for GuardConditions bitmasking system
/// Tests all 22 individual flags and 7 complex condition combinations
/// </summary>
public class GuardConditionsTests
{
    #region Individual Flag Tests

    [Fact]
    public void Robot1Free_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.Robot1Free;
        Assert.Equal((uint)0x00001, (uint)condition);
        Assert.Equal("0x000001", condition.ToHexString());
    }

    [Fact]
    public void Robot2Free_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.Robot2Free;
        Assert.Equal((uint)0x00002, (uint)condition);
        Assert.Equal("0x000002", condition.ToHexString());
    }

    [Fact]
    public void Robot3Free_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.Robot3Free;
        Assert.Equal((uint)0x00004, (uint)condition);
        Assert.Equal("0x000004", condition.ToHexString());
    }

    [Fact]
    public void PlatenFree_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.PlatenFree;
        Assert.Equal((uint)0x00008, (uint)condition);
        Assert.Equal("0x000008", condition.ToHexString());
    }

    [Fact]
    public void CleanerFree_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.CleanerFree;
        Assert.Equal((uint)0x00010, (uint)condition);
        Assert.Equal("0x000010", condition.ToHexString());
    }

    [Fact]
    public void BufferFree_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.BufferFree;
        Assert.Equal((uint)0x00020, (uint)condition);
        Assert.Equal("0x000020", condition.ToHexString());
    }

    [Fact]
    public void PlatenLocationFree_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.PlatenLocationFree;
        Assert.Equal((uint)0x00040, (uint)condition);
        Assert.Equal("0x000040", condition.ToHexString());
    }

    [Fact]
    public void CleanerLocationFree_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.CleanerLocationFree;
        Assert.Equal((uint)0x00080, (uint)condition);
        Assert.Equal("0x000080", condition.ToHexString());
    }

    [Fact]
    public void BufferLocationFree_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.BufferLocationFree;
        Assert.Equal((uint)0x00100, (uint)condition);
        Assert.Equal("0x000100", condition.ToHexString());
    }

    [Fact]
    public void HasRobot1Permission_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.HasRobot1Permission;
        Assert.Equal((uint)0x00200, (uint)condition);
        Assert.Equal("0x000200", condition.ToHexString());
    }

    [Fact]
    public void HasRobot2Permission_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.HasRobot2Permission;
        Assert.Equal((uint)0x00400, (uint)condition);
        Assert.Equal("0x000400", condition.ToHexString());
    }

    [Fact]
    public void HasRobot3Permission_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.HasRobot3Permission;
        Assert.Equal((uint)0x00800, (uint)condition);
        Assert.Equal("0x000800", condition.ToHexString());
    }

    [Fact]
    public void HasPlatenPermission_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.HasPlatenPermission;
        Assert.Equal((uint)0x01000, (uint)condition);
        Assert.Equal("0x001000", condition.ToHexString());
    }

    [Fact]
    public void HasCleanerPermission_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.HasCleanerPermission;
        Assert.Equal((uint)0x02000, (uint)condition);
        Assert.Equal("0x002000", condition.ToHexString());
    }

    [Fact]
    public void HasBufferPermission_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.HasBufferPermission;
        Assert.Equal((uint)0x04000, (uint)condition);
        Assert.Equal("0x004000", condition.ToHexString());
    }

    [Fact]
    public void WaferOnRobot_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.WaferOnRobot;
        Assert.Equal((uint)0x08000, (uint)condition);
        Assert.Equal("0x008000", condition.ToHexString());
    }

    [Fact]
    public void WaferAtPlaten_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.WaferAtPlaten;
        Assert.Equal((uint)0x10000, (uint)condition);
        Assert.Equal("0x010000", condition.ToHexString());
    }

    [Fact]
    public void WaferAtCleaner_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.WaferAtCleaner;
        Assert.Equal((uint)0x20000, (uint)condition);
        Assert.Equal("0x020000", condition.ToHexString());
    }

    [Fact]
    public void WaferAtBuffer_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.WaferAtBuffer;
        Assert.Equal((uint)0x40000, (uint)condition);
        Assert.Equal("0x040000", condition.ToHexString());
    }

    [Fact]
    public void PolishComplete_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.PolishComplete;
        Assert.Equal((uint)0x80000, (uint)condition);
        Assert.Equal("0x080000", condition.ToHexString());
    }

    [Fact]
    public void CleanComplete_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.CleanComplete;
        Assert.Equal((uint)0x100000, (uint)condition);
        Assert.Equal("0x100000", condition.ToHexString());
    }

    [Fact]
    public void BufferComplete_ShouldHaveCorrectBitValue()
    {
        var condition = GuardConditions.BufferComplete;
        Assert.Equal((uint)0x200000, (uint)condition);
        Assert.Equal("0x200000", condition.ToHexString());
    }

    #endregion

    #region Complex Condition Tests

    [Fact]
    public void CanPickFromCarrier_ShouldRequireRobot1Permission()
    {
        var current = GuardConditions.HasRobot1Permission;
        Assert.True(current.HasAll(GuardConditions.CanPickFromCarrier));

        var missing = GuardConditions.None;
        Assert.False(missing.HasAll(GuardConditions.CanPickFromCarrier));
    }

    [Fact]
    public void CanMoveToPlaten_ShouldRequireWaferOnRobotAndRobot1Permission()
    {
        var current = GuardConditions.WaferOnRobot | GuardConditions.HasRobot1Permission;
        Assert.True(current.HasAll(GuardConditions.CanMoveToPlaten));

        var onlyWafer = GuardConditions.WaferOnRobot;
        Assert.False(onlyWafer.HasAll(GuardConditions.CanMoveToPlaten));

        var onlyPermission = GuardConditions.HasRobot1Permission;
        Assert.False(onlyPermission.HasAll(GuardConditions.CanMoveToPlaten));
    }

    [Fact]
    public void CanPlaceOnPlaten_ShouldRequireWaferOnRobotAndPlatenPermission()
    {
        var current = GuardConditions.WaferOnRobot | GuardConditions.HasPlatenPermission;
        Assert.True(current.HasAll(GuardConditions.CanPlaceOnPlaten));

        var onlyWafer = GuardConditions.WaferOnRobot;
        Assert.False(onlyWafer.HasAll(GuardConditions.CanPlaceOnPlaten));

        var onlyPermission = GuardConditions.HasPlatenPermission;
        Assert.False(onlyPermission.HasAll(GuardConditions.CanPlaceOnPlaten));
    }

    [Fact]
    public void CanStartPolish_ShouldRequireWaferAtPlatenAndPlatenFree()
    {
        var current = GuardConditions.WaferAtPlaten | GuardConditions.PlatenFree;
        Assert.True(current.HasAll(GuardConditions.CanStartPolish));

        var onlyWafer = GuardConditions.WaferAtPlaten;
        Assert.False(onlyWafer.HasAll(GuardConditions.CanStartPolish));

        var onlyPlaten = GuardConditions.PlatenFree;
        Assert.False(onlyPlaten.HasAll(GuardConditions.CanStartPolish));
    }

    [Fact]
    public void CanPickFromPlaten_ShouldRequirePolishCompleteAndRobot2Permission()
    {
        var current = GuardConditions.PolishComplete | GuardConditions.HasRobot2Permission;
        Assert.True(current.HasAll(GuardConditions.CanPickFromPlaten));

        var onlyComplete = GuardConditions.PolishComplete;
        Assert.False(onlyComplete.HasAll(GuardConditions.CanPickFromPlaten));

        var onlyPermission = GuardConditions.HasRobot2Permission;
        Assert.False(onlyPermission.HasAll(GuardConditions.CanPickFromPlaten));
    }

    [Fact]
    public void CanMoveToCleaner_ShouldRequireWaferOnRobotAndRobot2Permission()
    {
        var current = GuardConditions.WaferOnRobot | GuardConditions.HasRobot2Permission;
        Assert.True(current.HasAll(GuardConditions.CanMoveToCleaner));

        var onlyWafer = GuardConditions.WaferOnRobot;
        Assert.False(onlyWafer.HasAll(GuardConditions.CanMoveToCleaner));

        var onlyPermission = GuardConditions.HasRobot2Permission;
        Assert.False(onlyPermission.HasAll(GuardConditions.CanMoveToCleaner));
    }

    [Fact]
    public void CanPlaceOnCleaner_ShouldRequireWaferOnRobotAndCleanerPermission()
    {
        var current = GuardConditions.WaferOnRobot | GuardConditions.HasCleanerPermission;
        Assert.True(current.HasAll(GuardConditions.CanPlaceOnCleaner));

        var onlyWafer = GuardConditions.WaferOnRobot;
        Assert.False(onlyWafer.HasAll(GuardConditions.CanPlaceOnCleaner));

        var onlyPermission = GuardConditions.HasCleanerPermission;
        Assert.False(onlyPermission.HasAll(GuardConditions.CanPlaceOnCleaner));
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void HasAll_ShouldReturnTrueWhenAllRequiredFlagsPresent()
    {
        var current = GuardConditions.HasRobot1Permission | GuardConditions.WaferOnRobot;
        var required = GuardConditions.HasRobot1Permission;

        Assert.True(current.HasAll(required));
    }

    [Fact]
    public void HasAll_ShouldReturnFalseWhenAnyRequiredFlagMissing()
    {
        var current = GuardConditions.HasRobot1Permission;
        var required = GuardConditions.HasRobot1Permission | GuardConditions.WaferOnRobot;

        Assert.False(current.HasAll(required));
    }

    [Fact]
    public void HasAny_ShouldReturnTrueWhenAnyFlagPresent()
    {
        var current = GuardConditions.HasRobot1Permission;
        var check = GuardConditions.HasRobot1Permission | GuardConditions.WaferOnRobot;

        Assert.True(current.HasAny(check));
    }

    [Fact]
    public void HasAny_ShouldReturnFalseWhenNoFlagsPresent()
    {
        var current = GuardConditions.HasRobot2Permission;
        var check = GuardConditions.HasRobot1Permission | GuardConditions.WaferOnRobot;

        Assert.False(current.HasAny(check));
    }

    [Fact]
    public void Set_ShouldAddFlagsToCondition()
    {
        var current = GuardConditions.None;
        var updated = current.Set(GuardConditions.HasRobot1Permission);

        Assert.True(updated.HasAll(GuardConditions.HasRobot1Permission));
        Assert.Equal("0x000200", updated.ToHexString());
    }

    [Fact]
    public void Set_ShouldPreserveExistingFlags()
    {
        var current = GuardConditions.HasRobot1Permission;
        var updated = current.Set(GuardConditions.WaferOnRobot);

        Assert.True(updated.HasAll(GuardConditions.HasRobot1Permission));
        Assert.True(updated.HasAll(GuardConditions.WaferOnRobot));
        Assert.Equal("0x008200", updated.ToHexString());
    }

    [Fact]
    public void Clear_ShouldRemoveFlagsFromCondition()
    {
        var current = GuardConditions.HasRobot1Permission | GuardConditions.WaferOnRobot;
        var updated = current.Clear(GuardConditions.WaferOnRobot);

        Assert.True(updated.HasAll(GuardConditions.HasRobot1Permission));
        Assert.False(updated.HasAll(GuardConditions.WaferOnRobot));
        Assert.Equal("0x000200", updated.ToHexString());
    }

    [Fact]
    public void Clear_ShouldNotAffectOtherFlags()
    {
        var current = GuardConditions.HasRobot1Permission | GuardConditions.WaferOnRobot | GuardConditions.HasPlatenPermission;
        var updated = current.Clear(GuardConditions.WaferOnRobot);

        Assert.True(updated.HasAll(GuardConditions.HasRobot1Permission));
        Assert.True(updated.HasAll(GuardConditions.HasPlatenPermission));
        Assert.False(updated.HasAll(GuardConditions.WaferOnRobot));
    }

    [Fact]
    public void Toggle_ShouldAddFlagIfNotPresent()
    {
        var current = GuardConditions.None;
        var updated = current.Toggle(GuardConditions.HasRobot1Permission);

        Assert.True(updated.HasAll(GuardConditions.HasRobot1Permission));
    }

    [Fact]
    public void Toggle_ShouldRemoveFlagIfPresent()
    {
        var current = GuardConditions.HasRobot1Permission;
        var updated = current.Toggle(GuardConditions.HasRobot1Permission);

        Assert.False(updated.HasAll(GuardConditions.HasRobot1Permission));
        Assert.Equal("0x000000", updated.ToHexString());
    }

    [Fact]
    public void ToHexString_ShouldFormatCorrectly()
    {
        var condition = GuardConditions.HasRobot1Permission | GuardConditions.WaferOnRobot | GuardConditions.HasPlatenPermission;
        Assert.Equal("0x009200", condition.ToHexString());
    }

    #endregion

    #region Wafer Lifecycle Scenario Tests

    [Fact]
    public void WaferLifecycle_StartToPickup_ShouldRequireOnlyRobot1Permission()
    {
        var conditions = GuardConditions.None;

        // Initial state - no conditions met
        Assert.False(conditions.HasAll(GuardConditions.CanPickFromCarrier));

        // Robot1 permission granted
        conditions = conditions.Set(GuardConditions.HasRobot1Permission);
        Assert.True(conditions.HasAll(GuardConditions.CanPickFromCarrier));
        Assert.Equal("0x000200", conditions.ToHexString());
    }

    [Fact]
    public void WaferLifecycle_PickupToPlaten_ShouldTrackWaferLocation()
    {
        var conditions = GuardConditions.HasRobot1Permission;

        // After pickup - wafer on robot
        conditions = conditions.Set(GuardConditions.WaferOnRobot);
        Assert.True(conditions.HasAll(GuardConditions.CanMoveToPlaten));
        Assert.Equal("0x008200", conditions.ToHexString());

        // Get platen permission
        conditions = conditions.Set(GuardConditions.HasPlatenPermission);
        Assert.True(conditions.HasAll(GuardConditions.CanPlaceOnPlaten));
        Assert.Equal("0x009200", conditions.ToHexString());

        // After placing - wafer at platen, clear robot flags
        conditions = conditions.Clear(GuardConditions.WaferOnRobot | GuardConditions.HasPlatenPermission);
        conditions = conditions.Set(GuardConditions.WaferAtPlaten);
        Assert.True(conditions.HasAll(GuardConditions.WaferAtPlaten));
        Assert.False(conditions.HasAll(GuardConditions.WaferOnRobot));
        Assert.Equal("0x010200", conditions.ToHexString());
    }

    [Fact]
    public void WaferLifecycle_PolishingPhase_ShouldTrackProcessCompletion()
    {
        var conditions = GuardConditions.WaferAtPlaten | GuardConditions.PlatenFree;

        // Can start polish
        Assert.True(conditions.HasAll(GuardConditions.CanStartPolish));

        // After polish complete
        conditions = conditions.Set(GuardConditions.PolishComplete);
        conditions = conditions.Set(GuardConditions.HasRobot2Permission);

        // Can pick from platen
        Assert.True(conditions.HasAll(GuardConditions.CanPickFromPlaten));
        Assert.Equal("0x090408", conditions.ToHexString());
    }

    [Fact]
    public void WaferLifecycle_CompleteFlow_ShouldTransitionThroughAllStations()
    {
        var conditions = GuardConditions.None;

        // Stage 1: Pick from carrier
        conditions = conditions.Set(GuardConditions.HasRobot1Permission);
        Assert.True(conditions.HasAll(GuardConditions.CanPickFromCarrier));

        // Stage 2: Move to platen
        conditions = conditions.Set(GuardConditions.WaferOnRobot);
        Assert.True(conditions.HasAll(GuardConditions.CanMoveToPlaten));

        // Stage 3: Place on platen
        conditions = conditions.Set(GuardConditions.HasPlatenPermission);
        Assert.True(conditions.HasAll(GuardConditions.CanPlaceOnPlaten));

        // Stage 4: Polish
        conditions = conditions.Clear(GuardConditions.WaferOnRobot | GuardConditions.HasPlatenPermission);
        conditions = conditions.Set(GuardConditions.WaferAtPlaten | GuardConditions.PlatenFree);
        Assert.True(conditions.HasAll(GuardConditions.CanStartPolish));

        // Stage 5: Pick from platen (R2)
        conditions = conditions.Set(GuardConditions.PolishComplete | GuardConditions.HasRobot2Permission);
        Assert.True(conditions.HasAll(GuardConditions.CanPickFromPlaten));

        // Stage 6: Move to cleaner
        conditions = conditions.Clear(GuardConditions.WaferAtPlaten);
        conditions = conditions.Set(GuardConditions.WaferOnRobot);
        Assert.True(conditions.HasAll(GuardConditions.CanMoveToCleaner));

        // Stage 7: Place on cleaner
        conditions = conditions.Set(GuardConditions.HasCleanerPermission);
        Assert.True(conditions.HasAll(GuardConditions.CanPlaceOnCleaner));

        // Stage 8: Clean
        conditions = conditions.Clear(GuardConditions.WaferOnRobot | GuardConditions.HasCleanerPermission);
        conditions = conditions.Set(GuardConditions.WaferAtCleaner | GuardConditions.CleanerFree);
        Assert.True(conditions.HasAll(GuardConditions.CanStartClean));

        // Stage 9: Pick from cleaner (R3)
        conditions = conditions.Set(GuardConditions.CleanComplete | GuardConditions.HasRobot3Permission);
        Assert.True(conditions.HasAll(GuardConditions.CanPickFromCleaner));

        // Stage 10: Move to buffer
        conditions = conditions.Clear(GuardConditions.WaferAtCleaner);
        conditions = conditions.Set(GuardConditions.WaferOnRobot);
        Assert.True(conditions.HasAll(GuardConditions.CanMoveToBuffer));

        // Stage 11: Place on buffer
        conditions = conditions.Set(GuardConditions.HasBufferPermission);
        Assert.True(conditions.HasAll(GuardConditions.CanPlaceOnBuffer));

        // Stage 12: Buffer
        conditions = conditions.Clear(GuardConditions.WaferOnRobot | GuardConditions.HasBufferPermission);
        conditions = conditions.Set(GuardConditions.WaferAtBuffer | GuardConditions.BufferFree);
        Assert.True(conditions.HasAll(GuardConditions.CanStartBuffer));

        // Stage 13: Pick from buffer (R1 return)
        conditions = conditions.Set(GuardConditions.BufferComplete | GuardConditions.HasRobot1Permission);
        Assert.True(conditions.HasAll(GuardConditions.CanPickFromBuffer));

        // Stage 14: Return to carrier
        conditions = conditions.Clear(GuardConditions.WaferAtBuffer);
        conditions = conditions.Set(GuardConditions.WaferOnRobot);
        Assert.True(conditions.HasAll(GuardConditions.CanReturnToCarrier));
    }

    #endregion
}
