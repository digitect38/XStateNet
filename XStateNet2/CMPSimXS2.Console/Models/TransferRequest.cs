namespace CMPSimXS2.Console.Models;

/// <summary>
/// Transfer request data model for robot scheduling
/// Represents a request to move a wafer from one station to another
/// </summary>
public class TransferRequest
{
    /// <summary>
    /// Wafer ID to transfer
    /// </summary>
    public int WaferId { get; set; }

    /// <summary>
    /// Source station ID (e.g., "Carrier", "Polisher", "Cleaner", "Buffer")
    /// MUST be non-null and non-empty
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Destination station ID (e.g., "Polisher", "Cleaner", "Buffer", "Carrier")
    /// MUST be non-null and non-empty
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// Priority of this transfer (higher = more urgent)
    /// Default: 0 (normal priority)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Timestamp when this request was created
    /// Used for FIFO ordering within same priority
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Optional: Preferred robot ID (null = auto-select)
    /// </summary>
    public string? PreferredRobotId { get; set; }

    /// <summary>
    /// Callback action when transfer is completed
    /// </summary>
    public Action<int>? OnCompleted { get; set; }

    /// <summary>
    /// Validate that this transfer request has all required fields
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(From))
            throw new ArgumentException("Transfer request 'From' cannot be null or empty", nameof(From));

        if (string.IsNullOrEmpty(To))
            throw new ArgumentException("Transfer request 'To' cannot be null or empty", nameof(To));

        if (WaferId <= 0)
            throw new ArgumentException("Transfer request 'WaferId' must be positive", nameof(WaferId));
    }

    public override string ToString()
    {
        return $"TransferRequest(Wafer={WaferId}, {From}→{To}, Priority={Priority})";
    }
}
