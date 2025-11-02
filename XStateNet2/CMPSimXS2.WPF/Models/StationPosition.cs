namespace CMPSimXS2.WPF.Models;

/// <summary>
/// Defines the position and capacity of each station in the CMP tool
/// </summary>
public class StationPosition
{
    public string Name { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int MaxCapacity { get; init; }
    public List<int> WaferSlots { get; set; }

    public StationPosition(string name, double x, double y, double width, double height, int maxCapacity)
    {
        Name = name;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        MaxCapacity = maxCapacity;
        WaferSlots = new List<int>();
    }

    public bool CanAcceptWafer()
    {
        return WaferSlots.Count < MaxCapacity;
    }

    public void AddWafer(int waferId)
    {
        if (CanAcceptWafer())
        {
            WaferSlots.Add(waferId);
        }
    }

    public void RemoveWafer(int waferId)
    {
        WaferSlots.Remove(waferId);
    }

    /// <summary>
    /// Get the position for a wafer in this station
    /// </summary>
    public (double X, double Y) GetWaferPosition(int slotIndex)
    {
        if (Name == "LoadPort")
        {
            // 5x5 grid layout (25 wafers)
            // slotIndex: 0-24
            int row = slotIndex / 5;  // 0-4
            int col = slotIndex % 5;  // 0-4

            double cellWidth = Width / 5;
            double cellHeight = Height / 5;

            // Center wafer in each cell
            double waferX = X + (col * cellWidth) + (cellWidth / 2);
            double waferY = Y + (row * cellHeight) + (cellHeight / 2);

            return (waferX, waferY);
        }
        else
        {
            // Center wafer in processing station
            return (X + Width / 2, Y + Height / 2);
        }
    }
}
