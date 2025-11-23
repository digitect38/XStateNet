using System;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Bitmask for tracking resource availability in SystemCoordinator
/// Used for synchronized push-based scheduling
/// </summary>
[Flags]
public enum ResourceAvailability : uint
{
    None = 0,

    // Robots (Bits 0-2)
    Robot1Free = 1 << 0,              // 0x000001
    Robot2Free = 1 << 1,              // 0x000002
    Robot3Free = 1 << 2,              // 0x000004

    // Equipment (Bits 3-5)
    PlatenFree = 1 << 3,              // 0x000008
    CleanerFree = 1 << 4,             // 0x000010
    BufferFree = 1 << 5,              // 0x000020

    // Locations (Bits 6-8)
    PlatenLocationFree = 1 << 6,      // 0x000040
    CleanerLocationFree = 1 << 7,     // 0x000080
    BufferLocationFree = 1 << 8,      // 0x000100

    // Combinations for optimal scheduling
    AllRobotsFree = Robot1Free | Robot2Free | Robot3Free,                              // 0x000007
    AllEquipmentFree = PlatenFree | CleanerFree | BufferFree,                         // 0x000038
    AllLocationsFree = PlatenLocationFree | CleanerLocationFree | BufferLocationFree, // 0x0001C0
    AllResourcesFree = AllRobotsFree | AllEquipmentFree | AllLocationsFree,           // 0x0001FF

    // Stage-specific combinations for push scheduling
    CanExecuteP1Stage = Robot1Free | PlatenLocationFree,                              // 0x000041
    CanExecuteP2Stage = Robot2Free | PlatenFree | CleanerLocationFree,               // 0x000096
    CanExecuteP3Stage = Robot3Free | CleanerFree | BufferLocationFree,               // 0x000114
    CanExecuteP4Stage = Robot1Free | BufferFree | BufferLocationFree,                // 0x000121
}

/// <summary>
/// Extension methods for ResourceAvailability bitmask operations
/// </summary>
public static class ResourceAvailabilityExtensions
{
    /// <summary>
    /// Check if all required resources are available
    /// </summary>
    public static bool HasAll(this ResourceAvailability current, ResourceAvailability required)
    {
        return (current & required) == required;
    }

    /// <summary>
    /// Check if any of the resources are available
    /// </summary>
    public static bool HasAny(this ResourceAvailability current, ResourceAvailability check)
    {
        return (current & check) != ResourceAvailability.None;
    }

    /// <summary>
    /// Mark resources as available (set bits)
    /// </summary>
    public static ResourceAvailability MarkAvailable(this ResourceAvailability current, ResourceAvailability toMark)
    {
        return current | toMark;
    }

    /// <summary>
    /// Mark resources as busy (clear bits)
    /// </summary>
    public static ResourceAvailability MarkBusy(this ResourceAvailability current, ResourceAvailability toMark)
    {
        return current & ~toMark;
    }

    /// <summary>
    /// Toggle resource availability
    /// </summary>
    public static ResourceAvailability Toggle(this ResourceAvailability current, ResourceAvailability toToggle)
    {
        return current ^ toToggle;
    }

    /// <summary>
    /// Format as hex string for debugging
    /// </summary>
    public static string ToHexString(this ResourceAvailability availability)
    {
        return $"0x{(uint)availability:X6}";
    }

    /// <summary>
    /// Get readable string of available resources
    /// </summary>
    public static string ToReadableString(this ResourceAvailability availability)
    {
        var parts = new List<string>();

        if (availability.HasAny(ResourceAvailability.Robot1Free)) parts.Add("R-1");
        if (availability.HasAny(ResourceAvailability.Robot2Free)) parts.Add("R-2");
        if (availability.HasAny(ResourceAvailability.Robot3Free)) parts.Add("R-3");
        if (availability.HasAny(ResourceAvailability.PlatenFree)) parts.Add("PLATEN");
        if (availability.HasAny(ResourceAvailability.CleanerFree)) parts.Add("CLEANER");
        if (availability.HasAny(ResourceAvailability.BufferFree)) parts.Add("BUFFER");
        if (availability.HasAny(ResourceAvailability.PlatenLocationFree)) parts.Add("PLATEN_LOC");
        if (availability.HasAny(ResourceAvailability.CleanerLocationFree)) parts.Add("CLEANER_LOC");
        if (availability.HasAny(ResourceAvailability.BufferLocationFree)) parts.Add("BUFFER_LOC");

        return parts.Count > 0 ? string.Join(",", parts) : "NONE";
    }

    /// <summary>
    /// Map resource name to bitmask flag
    /// </summary>
    public static ResourceAvailability FromResourceName(string resourceName)
    {
        return resourceName switch
        {
            "R-1" => ResourceAvailability.Robot1Free,
            "R-2" => ResourceAvailability.Robot2Free,
            "R-3" => ResourceAvailability.Robot3Free,
            "PLATEN" => ResourceAvailability.PlatenFree,
            "CLEANER" => ResourceAvailability.CleanerFree,
            "BUFFER" => ResourceAvailability.BufferFree,
            "PLATEN_LOCATION" => ResourceAvailability.PlatenLocationFree,
            "CLEANER_LOCATION" => ResourceAvailability.CleanerLocationFree,
            "BUFFER_LOCATION" => ResourceAvailability.BufferLocationFree,
            _ => ResourceAvailability.None
        };
    }
}
