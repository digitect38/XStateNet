namespace SemiFlow.Algorithms.Models;

/// <summary>
/// Represents a wafer identifier (e.g., W001, W002, ..., W025)
/// </summary>
public readonly struct WaferId : IEquatable<WaferId>, IComparable<WaferId>
{
    public int Number { get; }

    public WaferId(int number)
    {
        if (number < 1)
            throw new ArgumentOutOfRangeException(nameof(number), "Wafer number must be >= 1");
        Number = number;
    }

    /// <summary>
    /// Parse wafer ID from string (e.g., "W001", "W25")
    /// </summary>
    public static WaferId Parse(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentNullException(nameof(value));

        if (!value.StartsWith("W", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Invalid wafer ID format: {value}. Must start with 'W'");

        if (!int.TryParse(value[1..], out var number))
            throw new FormatException($"Invalid wafer ID format: {value}");

        return new WaferId(number);
    }

    public static bool TryParse(string value, out WaferId result)
    {
        try
        {
            result = Parse(value);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Create a range of wafer IDs (e.g., W001 to W025)
    /// </summary>
    public static IEnumerable<WaferId> Range(int start, int count)
    {
        for (int i = 0; i < count; i++)
            yield return new WaferId(start + i);
    }

    /// <summary>
    /// Create wafer IDs for a standard FOUP (25 wafers)
    /// </summary>
    public static IReadOnlyList<WaferId> Foup(int startNumber = 1)
        => Range(startNumber, 25).ToList();

    public override string ToString() => $"W{Number:D3}";

    public bool Equals(WaferId other) => Number == other.Number;
    public override bool Equals(object? obj) => obj is WaferId other && Equals(other);
    public override int GetHashCode() => Number;
    public int CompareTo(WaferId other) => Number.CompareTo(other.Number);

    public static bool operator ==(WaferId left, WaferId right) => left.Equals(right);
    public static bool operator !=(WaferId left, WaferId right) => !left.Equals(right);
    public static bool operator <(WaferId left, WaferId right) => left.CompareTo(right) < 0;
    public static bool operator >(WaferId left, WaferId right) => left.CompareTo(right) > 0;

    public static implicit operator int(WaferId id) => id.Number;
    public static explicit operator WaferId(int number) => new(number);
}
