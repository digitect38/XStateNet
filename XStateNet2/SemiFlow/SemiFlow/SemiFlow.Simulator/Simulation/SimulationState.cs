using SemiFlow.Simulator.Models;

namespace SemiFlow.Simulator.Simulation;

public class SimulationState
{
    // Stations
    public Dictionary<string, Station> Stations { get; set; } = new();

    // Wafers
    public Queue<Wafer> WaferQueue { get; set; } = new();
    public List<Wafer> ActiveWafers { get; set; } = new();
    public List<Wafer> CompletedWafers { get; set; } = new();

    // Counters
    public int ProcessedWafers { get; set; } = 0;
    public int TotalWafers { get; set; } = 25;
    public int ErrorCount { get; set; } = 0;

    // Timing
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }

    // Current wafer being processed
    public Wafer? CurrentWafer { get; set; }

    // Process tracking
    public string? SelectedPlaten { get; set; }
    public string? FirstPlaten { get; set; }
    public string? SecondPlaten { get; set; }

    // Metrics
    public Dictionary<string, double> Metrics { get; set; } = new()
    {
        ["platen1_utilization"] = 0.0,
        ["platen2_utilization"] = 0.0,
        ["robot_utilization"] = 0.0,
        ["avg_cycle_time"] = 0.0,
        ["throughput"] = 0.0
    };

    // Configuration
    public string CurrentLot { get; set; } = "LOT001";
    public bool SystemRunning { get; set; } = true;

    // Statistics
    public int OneStepWafers { get; set; } = 0;
    public int TwoStepWafers { get; set; } = 0;

    public TimeSpan ElapsedTime => EndTime.HasValue
        ? EndTime.Value - StartTime
        : DateTime.Now - StartTime;

    public double ThroughputWafersPerHour
    {
        get
        {
            var hours = ElapsedTime.TotalHours;
            return hours > 0 ? ProcessedWafers / hours : 0;
        }
    }
}
