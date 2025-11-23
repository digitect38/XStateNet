namespace SemiFlow.Simulator.Models;

public class Wafer
{
    public int Id { get; set; }
    public string LotId { get; set; } = "";
    public WaferState State { get; set; } = WaferState.InFoup;
    public ProcessType ProcessType { get; set; } = ProcessType.OneStep;
    public int ProcessStep { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? StartProcessTime { get; set; }
    public DateTime? EndProcessTime { get; set; }
    public List<ProcessHistory> History { get; set; } = new();
    public string CurrentLocation { get; set; } = "FOUP1";
}

public enum WaferState
{
    InFoup,
    OnRobot,
    OnPlaten,
    Processing,
    Completed,
    Error
}

public enum ProcessType
{
    OneStep,
    TwoStep
}

public class ProcessHistory
{
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = "";
    public string Location { get; set; } = "";
    public string Details { get; set; } = "";
}
