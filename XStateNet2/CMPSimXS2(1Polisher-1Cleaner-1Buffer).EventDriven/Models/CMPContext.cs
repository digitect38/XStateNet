namespace CMPSimXS2.EventDriven.Models;

/// <summary>
/// CMP system context - all extended state for the CMP state machine
/// Matches the JavaScript context structure
/// </summary>
public class CMPContext
{
    // Wafer tracking
    public int CarrierUnprocessed { get; set; } = 25;
    public int CarrierProcessed { get; set; } = 0;
    public WaferRecord? CurrentWafer { get; set; }
    public List<WaferRecord> WaferHistory { get; set; } = new();

    // Resource locking
    public bool PlatenLocked { get; set; } = false;
    public bool RobotBusy { get; set; } = false;

    // Configuration
    public ProcessingConfig Config { get; set; } = new();

    // Error tracking
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
}
