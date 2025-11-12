namespace CMPSimXS2.EventDriven.Models;

/// <summary>
/// Wafer record for tracking wafer processing history
/// </summary>
public class WaferRecord
{
    public string Id { get; set; } = string.Empty;
    public long StartTime { get; set; }
    public long? PickTime { get; set; }
    public long? LoadTime { get; set; }
    public long? ProcessTime { get; set; }
    public long? UnloadTime { get; set; }
    public long? CompleteTime { get; set; }
    public long? CycleTime { get; set; }
}
