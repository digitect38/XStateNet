namespace CMPSimXS2.Console.Models;

/// <summary>
/// Simple console-friendly wafer model without WPF dependencies
/// </summary>
public class Wafer
{
    public int Id { get; }
    public string JourneyStage { get; set; } = "InCarrier";
    public string CurrentStation { get; set; } = "Carrier";
    public string ProcessingState { get; set; } = "Raw";
    public bool IsCompleted { get; set; }

    public Wafer(int id)
    {
        Id = id;
    }
}
