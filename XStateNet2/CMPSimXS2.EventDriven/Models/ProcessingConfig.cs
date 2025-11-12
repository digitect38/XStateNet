namespace CMPSimXS2.EventDriven.Models;

/// <summary>
/// Processing configuration (timings and timeouts)
/// </summary>
public class ProcessingConfig
{
    public int PolishTime { get; set; } = 2000;
    public int MoveTime { get; set; } = 500;
    public int PickPlaceTime { get; set; } = 300;
    public int Timeout { get; set; } = 5000;
}
