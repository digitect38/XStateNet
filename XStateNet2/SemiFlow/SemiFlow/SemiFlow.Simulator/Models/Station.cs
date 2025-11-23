namespace SemiFlow.Simulator.Models;

public class Station
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public StationState State { get; set; } = StationState.Idle;
    public Wafer? CurrentWafer { get; set; }
    public int Capacity { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
    public DateTime LastStateChange { get; set; } = DateTime.Now;
    public double UtilizationTime { get; set; } = 0;
    public int ProcessedWafers { get; set; } = 0;
}

public enum StationState
{
    Idle,
    Busy,
    Processing,
    Error,
    Maintenance
}
