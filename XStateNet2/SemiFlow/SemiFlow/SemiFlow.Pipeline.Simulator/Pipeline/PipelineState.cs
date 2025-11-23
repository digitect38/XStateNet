using SemiFlow.Pipeline.Simulator.Models;

namespace SemiFlow.Pipeline.Simulator.Pipeline;

public class PipelineState
{
    // System state
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public bool SystemRunning { get; set; } = true;

    // Lot configuration
    public string LotId { get; set; } = "LOT001";
    public int TotalWafers { get; set; } = 25;
    public int MaxPipelineDepth { get; set; } = 5;

    // Wafer tracking
    public Queue<PipelineWafer> WaitingWafers { get; set; } = new();
    public List<PipelineWafer> ActiveWafers { get; set; } = new();
    public List<PipelineWafer> CompletedWafers { get; set; } = new();

    // Resources
    public Dictionary<string, ResourceState> Resources { get; set; } = new();

    // Pipeline metrics
    public int WafersDispatched { get; set; } = 0;
    public int WafersCompleted { get; set; } = 0;

    // Computed properties
    public int CurrentPipelineDepth => ActiveWafers.Count;

    public bool CanDispatch => CurrentPipelineDepth < MaxPipelineDepth && WaitingWafers.Count > 0;

    public bool PipelineEmpty => ActiveWafers.Count == 0;

    public TimeSpan ElapsedTime => EndTime.HasValue
        ? EndTime.Value - StartTime
        : DateTime.Now - StartTime;

    public double ThroughputWafersPerHour
    {
        get
        {
            var hours = ElapsedTime.TotalHours;
            return hours > 0 ? WafersCompleted / hours : 0;
        }
    }

    public double AverageCycleTime
    {
        get
        {
            if (CompletedWafers.Count == 0) return 0;
            return CompletedWafers.Average(w => w.CycleTime.TotalSeconds);
        }
    }

    // Stage counts for visualization
    public int GetWafersInStage(WaferStage stage)
    {
        return ActiveWafers.Count(w => w.CurrentStage == stage);
    }

    public Dictionary<WaferStage, int> GetPipelineSnapshot()
    {
        return new Dictionary<WaferStage, int>
        {
            [WaferStage.LoadingToPlaten1] = GetWafersInStage(WaferStage.LoadingToPlaten1),
            [WaferStage.OnPlaten1] = GetWafersInStage(WaferStage.OnPlaten1),
            [WaferStage.ProcessingPlaten1] = GetWafersInStage(WaferStage.ProcessingPlaten1),
            [WaferStage.TransferringToPlaten2] = GetWafersInStage(WaferStage.TransferringToPlaten2),
            [WaferStage.OnPlaten2] = GetWafersInStage(WaferStage.OnPlaten2),
            [WaferStage.ProcessingPlaten2] = GetWafersInStage(WaferStage.ProcessingPlaten2),
            [WaferStage.UnloadingToFoup] = GetWafersInStage(WaferStage.UnloadingToFoup)
        };
    }
}
