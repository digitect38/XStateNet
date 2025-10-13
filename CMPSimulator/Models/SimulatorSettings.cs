using System.Text.Json.Serialization;

namespace CMPSimulator.Models;

/// <summary>
/// Simulator settings for serialization/deserialization
/// Includes timing configuration and station geometry
/// </summary>
public class SimulatorSettings
{
    // Timing configuration
    public int R1TransferTime { get; set; } = 1000;
    public int PolisherTime { get; set; } = 5000;
    public int R2TransferTime { get; set; } = 1000;
    public int CleanerTime { get; set; } = 3000;
    public int R3TransferTime { get; set; } = 1000;
    public int BufferHoldTime { get; set; } = 0;
    public int LoadPortReturnTime { get; set; } = 1000;

    // Station geometry
    public StationGeometry LoadPort { get; set; } = new StationGeometry { Left = 80, Top = 80, Width = 160, Height = 240 };
    public StationGeometry R1 { get; set; } = new StationGeometry { Left = 248, Top = 80, Width = 112, Height = 240 };
    public StationGeometry Polisher { get; set; } = new StationGeometry { Left = 368, Top = 80, Width = 272, Height = 112 };
    public StationGeometry R2 { get; set; } = new StationGeometry { Left = 648, Top = 80, Width = 112, Height = 112 };
    public StationGeometry Cleaner { get; set; } = new StationGeometry { Left = 768, Top = 80, Width = 112, Height = 240 };
    public StationGeometry R3 { get; set; } = new StationGeometry { Left = 648, Top = 208, Width = 112, Height = 112 };
    public StationGeometry Buffer { get; set; } = new StationGeometry { Left = 368, Top = 208, Width = 272, Height = 112 };
}

/// <summary>
/// Station geometry (position and size)
/// </summary>
public class StationGeometry
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    [JsonIgnore]
    public string PositionString => $"({Left}, {Top})";

    [JsonIgnore]
    public string SizeString => $"{Width} × {Height}";
}
