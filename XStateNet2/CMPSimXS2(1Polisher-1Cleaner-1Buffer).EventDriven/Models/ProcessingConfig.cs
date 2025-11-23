namespace CMPSimXS2.EventDriven.Models;

/// <summary>
/// Processing configuration (timings and timeouts)
/// </summary>
public class ProcessingConfig
{
    public int PolishTime { get; set; } = 100;//2000;
    public int MoveTime { get; set; } = 50;
    public int PickPlaceTime { get; set; } = 30;
    public int Timeout { get; set; } = 1000;
}
