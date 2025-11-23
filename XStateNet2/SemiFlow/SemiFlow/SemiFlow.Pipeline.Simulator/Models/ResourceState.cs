namespace SemiFlow.Pipeline.Simulator.Models;

public class ResourceState
{
    public string Id { get; set; } = "";
    public ResourceType Type { get; set; }
    public ResourceStatus Status { get; set; } = ResourceStatus.Idle;
    public PipelineWafer? CurrentWafer { get; set; }
    public DateTime? BusySince { get; set; }
    public double TotalBusyTime { get; set; }
    public int TasksCompleted { get; set; }

    public void MarkBusy(PipelineWafer wafer)
    {
        Status = ResourceStatus.Busy;
        CurrentWafer = wafer;
        BusySince = DateTime.Now;
    }

    public void MarkIdle()
    {
        if (BusySince.HasValue)
        {
            TotalBusyTime += (DateTime.Now - BusySince.Value).TotalSeconds;
        }

        Status = ResourceStatus.Idle;
        CurrentWafer = null;
        BusySince = null;
        TasksCompleted++;
    }

    public double GetUtilization(DateTime startTime)
    {
        var elapsed = (DateTime.Now - startTime).TotalSeconds;
        var currentBusy = BusySince.HasValue
            ? (DateTime.Now - BusySince.Value).TotalSeconds
            : 0;

        return elapsed > 0
            ? ((TotalBusyTime + currentBusy) / elapsed) * 100.0
            : 0;
    }
}

public enum ResourceType
{
    Robot,
    Platen1,
    Platen2,
    Foup
}

public enum ResourceStatus
{
    Idle,
    Busy,
    Processing,
    Error
}
