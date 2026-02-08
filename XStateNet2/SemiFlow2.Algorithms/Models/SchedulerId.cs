namespace SemiFlow.Algorithms.Models;

/// <summary>
/// Represents a scheduler identifier with type and number
/// </summary>
public readonly struct SchedulerId : IEquatable<SchedulerId>
{
    public SchedulerType Type { get; }
    public int Number { get; }
    public string? Zone { get; }

    public SchedulerId(SchedulerType type, int number, string? zone = null)
    {
        if (number < 1)
            throw new ArgumentOutOfRangeException(nameof(number), "Scheduler number must be >= 1");

        Type = type;
        Number = number;
        Zone = zone;
    }

    /// <summary>
    /// Parse scheduler ID from string (e.g., "MSC_001", "WSC_ZONE_A_001")
    /// </summary>
    public static SchedulerId Parse(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentNullException(nameof(value));

        var parts = value.Split('_');
        if (parts.Length < 2)
            throw new FormatException($"Invalid scheduler ID format: {value}");

        var type = parts[0] switch
        {
            "MSC" => SchedulerType.Master,
            "WSC" => SchedulerType.Wafer,
            "RSC" => SchedulerType.Robot,
            "STN" => SchedulerType.Station,
            _ => throw new FormatException($"Unknown scheduler type: {parts[0]}")
        };

        // Handle zone (e.g., WSC_ZONE_A_001)
        string? zone = null;
        int numberIndex = parts.Length - 1;

        if (parts.Length > 2)
        {
            zone = string.Join("_", parts[1..^1]);
        }

        if (!int.TryParse(parts[numberIndex], out var number))
            throw new FormatException($"Invalid scheduler number: {parts[numberIndex]}");

        return new SchedulerId(type, number, zone);
    }

    /// <summary>
    /// Create a Master Scheduler ID
    /// </summary>
    public static SchedulerId Master(int number) => new(SchedulerType.Master, number);

    /// <summary>
    /// Create a Wafer Scheduler ID
    /// </summary>
    public static SchedulerId Wafer(int number) => new(SchedulerType.Wafer, number);

    /// <summary>
    /// Create a Robot Scheduler ID
    /// </summary>
    public static SchedulerId Robot(int number) => new(SchedulerType.Robot, number);

    /// <summary>
    /// Create a Station ID
    /// </summary>
    public static SchedulerId Station(int number) => new(SchedulerType.Station, number);

    public string Prefix => Type switch
    {
        SchedulerType.Master => "MSC",
        SchedulerType.Wafer => "WSC",
        SchedulerType.Robot => "RSC",
        SchedulerType.Station => "STN",
        _ => "UNK"
    };

    public Layer Layer => Type switch
    {
        SchedulerType.Master => Layer.L1,
        SchedulerType.Wafer => Layer.L2,
        SchedulerType.Robot => Layer.L3,
        SchedulerType.Station => Layer.L4,
        _ => Layer.L4
    };

    public override string ToString()
    {
        if (Zone != null)
            return $"{Prefix}_{Zone}_{Number:D3}";
        return $"{Prefix}_{Number:D3}";
    }

    public bool Equals(SchedulerId other)
        => Type == other.Type && Number == other.Number && Zone == other.Zone;

    public override bool Equals(object? obj) => obj is SchedulerId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Type, Number, Zone);

    public static bool operator ==(SchedulerId left, SchedulerId right) => left.Equals(right);
    public static bool operator !=(SchedulerId left, SchedulerId right) => !left.Equals(right);
}

public enum SchedulerType
{
    Master,   // MSC - L1
    Wafer,    // WSC - L2
    Robot,    // RSC - L3
    Station   // STN - L4
}

public enum Layer
{
    L1 = 1,  // Master Scheduler
    L2 = 2,  // Wafer Scheduler
    L3 = 3,  // Robot Scheduler
    L4 = 4   // Station
}
