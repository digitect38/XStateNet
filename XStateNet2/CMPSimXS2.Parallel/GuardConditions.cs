using System;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Bitmask-based guard conditions for state transitions.
/// Allows efficient checking of multiple conditions simultaneously.
/// </summary>
[Flags]
public enum GuardConditions : uint
{
    None = 0,

    // ===== Resource Availability (Robots) =====
    Robot1Free = 1 << 0,              // 0x00001 - R-1 is available
    Robot2Free = 1 << 1,              // 0x00002 - R-2 is available
    Robot3Free = 1 << 2,              // 0x00004 - R-3 is available

    // ===== Equipment Availability =====
    PlatenFree = 1 << 3,              // 0x00008 - Platen is not processing
    CleanerFree = 1 << 4,             // 0x00010 - Cleaner is not processing
    BufferFree = 1 << 5,              // 0x00020 - Buffer is not processing

    // ===== Location Availability =====
    PlatenLocationFree = 1 << 6,      // 0x00040 - Physical platen location is empty
    CleanerLocationFree = 1 << 7,     // 0x00080 - Physical cleaner location is empty
    BufferLocationFree = 1 << 8,      // 0x00100 - Physical buffer location is empty

    // ===== Robot Permissions (from Coordinator) =====
    HasRobot1Permission = 1 << 9,     // 0x00200 - Coordinator granted R-1 permission
    HasRobot2Permission = 1 << 10,    // 0x00400 - Coordinator granted R-2 permission
    HasRobot3Permission = 1 << 11,    // 0x00800 - Coordinator granted R-3 permission

    // ===== Location Permissions =====
    HasPlatenPermission = 1 << 12,    // 0x01000 - Coordinator granted platen location
    HasCleanerPermission = 1 << 13,   // 0x02000 - Coordinator granted cleaner location
    HasBufferPermission = 1 << 14,    // 0x04000 - Coordinator granted buffer location

    // ===== Wafer State Flags =====
    WaferOnRobot = 1 << 15,           // 0x08000 - Wafer is currently held by robot
    WaferAtPlaten = 1 << 16,          // 0x10000 - Wafer is physically at platen
    WaferAtCleaner = 1 << 17,         // 0x20000 - Wafer is physically at cleaner
    WaferAtBuffer = 1 << 18,          // 0x40000 - Wafer is physically at buffer

    // ===== Process State Flags =====
    PolishComplete = 1 << 19,         // 0x80000 - Polishing process finished
    CleanComplete = 1 << 20,          // 0x100000 - Cleaning process finished
    BufferComplete = 1 << 21,         // 0x200000 - Buffering process finished

    // ===== Complex Condition Combinations =====
    // Stage 1: Carrier -> Platen
    CanPickFromCarrier = HasRobot1Permission,
    CanMoveToPlaten = WaferOnRobot | HasRobot1Permission,
    CanPlaceOnPlaten = WaferOnRobot | HasPlatenPermission,

    // Stage 2: Platen Processing
    CanStartPolish = WaferAtPlaten | PlatenFree,

    // Stage 3: Platen -> Cleaner
    CanPickFromPlaten = PolishComplete | HasRobot2Permission,
    CanMoveToCleaner = WaferOnRobot | HasRobot2Permission,
    CanPlaceOnCleaner = WaferOnRobot | HasCleanerPermission,

    // Stage 4: Cleaner Processing
    CanStartClean = WaferAtCleaner | CleanerFree,

    // Stage 5: Cleaner -> Buffer
    CanPickFromCleaner = CleanComplete | HasRobot3Permission,
    CanMoveToBuffer = WaferOnRobot | HasRobot3Permission,
    CanPlaceOnBuffer = WaferOnRobot | HasBufferPermission,

    // Stage 6: Buffer Processing
    CanStartBuffer = WaferAtBuffer | BufferFree,

    // Stage 7: Buffer -> Carrier (Return)
    CanPickFromBuffer = BufferComplete | HasRobot1Permission,
    CanReturnToCarrier = WaferOnRobot | HasRobot1Permission,
}

/// <summary>
/// Extension methods for GuardConditions bitmask operations
/// </summary>
public static class GuardConditionsExtensions
{
    /// <summary>
    /// Check if all specified conditions are set
    /// </summary>
    public static bool HasAll(this GuardConditions current, GuardConditions required)
    {
        return (current & required) == required;
    }

    /// <summary>
    /// Check if any of the specified conditions are set
    /// </summary>
    public static bool HasAny(this GuardConditions current, GuardConditions conditions)
    {
        return (current & conditions) != GuardConditions.None;
    }

    /// <summary>
    /// Set (turn on) specific conditions
    /// </summary>
    public static GuardConditions Set(this GuardConditions current, GuardConditions toSet)
    {
        return current | toSet;
    }

    /// <summary>
    /// Clear (turn off) specific conditions
    /// </summary>
    public static GuardConditions Clear(this GuardConditions current, GuardConditions toClear)
    {
        return current & ~toClear;
    }

    /// <summary>
    /// Toggle specific conditions
    /// </summary>
    public static GuardConditions Toggle(this GuardConditions current, GuardConditions toToggle)
    {
        return current ^ toToggle;
    }

    /// <summary>
    /// Format conditions for logging
    /// </summary>
    public static string ToHexString(this GuardConditions conditions)
    {
        return $"0x{(uint)conditions:X6}";
    }
}
