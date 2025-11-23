namespace CMPSimXS2.EventDriven.Models;

/// <summary>
/// Wafer record for tracking wafer processing history
/// </summary>
public class WaferRecord
{
    public string Id { get; set; } = string.Empty;
    public long StartTime { get; set; }

    // R-1: Carrier -> Platen
    public long? PickTime { get; set; }
    public long? LoadTime { get; set; }
    public long? ProcessTime { get; set; }

    // Polisher
    public long? PolishTime { get; set; }

    // R-2: Platen -> Cleaner
    public long? PlatenUnloadTime { get; set; }
    public long? CleanLoadTime { get; set; }

    // Cleaner
    public long? CleanTime { get; set; }

    // R-3: Cleaner -> Buffer
    public long? CleanerUnloadTime { get; set; }
    public long? BufferLoadTime { get; set; }

    // Buffer
    public long? BufferTime { get; set; }

    // R-1: Buffer -> Carrier
    public long? BufferUnloadTime { get; set; }
    public long? UnloadTime { get; set; }

    public long? CompleteTime { get; set; }
    public long? CycleTime { get; set; }
}
