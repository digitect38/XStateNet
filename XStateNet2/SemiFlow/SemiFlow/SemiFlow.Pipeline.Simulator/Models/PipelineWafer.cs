namespace SemiFlow.Pipeline.Simulator.Models;

public class PipelineWafer
{
    public int Id { get; set; }
    public string LotId { get; set; } = "";
    public WaferStage CurrentStage { get; set; } = WaferStage.InFoup;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<StageHistory> History { get; set; } = new();

    // Pipeline tracking
    public bool IsActive { get; set; } = true;
    public string? CurrentResource { get; set; }
    public DateTime StageStartTime { get; set; }

    public TimeSpan CycleTime => EndTime.HasValue
        ? EndTime.Value - StartTime
        : DateTime.Now - StartTime;

    public void EnterStage(WaferStage stage, string? resource = null)
    {
        CurrentStage = stage;
        CurrentResource = resource;
        StageStartTime = DateTime.Now;

        History.Add(new StageHistory
        {
            Stage = stage,
            Resource = resource,
            EnterTime = DateTime.Now
        });
    }

    public void ExitStage()
    {
        if (History.Count > 0)
        {
            History[^1].ExitTime = DateTime.Now;
        }
    }
}

public enum WaferStage
{
    InFoup,
    LoadingToPlaten1,
    OnPlaten1,
    ProcessingPlaten1,
    TransferringToPlaten2,
    OnPlaten2,
    ProcessingPlaten2,
    UnloadingToFoup,
    Completed
}

public class StageHistory
{
    public WaferStage Stage { get; set; }
    public string? Resource { get; set; }
    public DateTime EnterTime { get; set; }
    public DateTime? ExitTime { get; set; }

    public TimeSpan Duration => ExitTime.HasValue
        ? ExitTime.Value - EnterTime
        : DateTime.Now - EnterTime;
}
