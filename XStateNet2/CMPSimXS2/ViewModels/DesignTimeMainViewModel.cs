using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows;
using CMPSimXS2.Models;

namespace CMPSimXS2.ViewModels;

/// <summary>
/// Design-time only ViewModel with hardcoded sample data for XAML Designer.
/// This class explicitly initializes sample data to ensure reliable design-time rendering.
/// </summary>
public class DesignTimeMainViewModel : ViewModelBase
{
    public ObservableCollection<StationViewModel> Stations { get; }
    public ObservableCollection<StationViewModel> Robots { get; }
    public ObservableCollection<Wafer> Wafers { get; }

    public bool IsRunning { get; set; }
    public int WaferCount { get; set; } = 25;
    public int ProcessedWafers { get; set; } = 0;
    public string Status { get; set; } = "Design Mode - Sample Data (5 Stations + 3 Robots + 25 Wafers)";
    public string? CurrentCarrierId { get; set; }

    public DesignTimeMainViewModel()
    {
        Stations = new ObservableCollection<StationViewModel>();
        Robots = new ObservableCollection<StationViewModel>();
        Wafers = new ObservableCollection<Wafer>();

        // Explicitly initialize design-time sample data
        InitializeSampleStations();
        InitializeSampleRobots();
        InitializeSampleWafers();
    }

    private void InitializeSampleStations()
    {
        // LoadPort (empty state - light green)
        var loadPort = new StationViewModel("LoadPort")
        {
            X = 250,
            Y = 30,
            CurrentState = "empty",
            CurrentWafer = null
        };
        Stations.Add(loadPort);

        // Carrier (loaded state with wafers - cornflower blue)
        var carrier = new StationViewModel("Carrier")
        {
            X = 450,
            Y = 30,
            CurrentState = "loaded",
            CurrentWafer = 5
        };
        Stations.Add(carrier);

        // Polisher (processing state - tomato red)
        var polisher = new StationViewModel("Polisher")
        {
            X = 650,
            Y = 30,
            CurrentState = "processing",
            CurrentWafer = 3
        };
        Stations.Add(polisher);

        // Cleaner (cleaning state - tomato red)
        var cleaner = new StationViewModel("Cleaner")
        {
            X = 850,
            Y = 30,
            CurrentState = "cleaning",
            CurrentWafer = 7
        };
        Stations.Add(cleaner);

        // Buffer (occupied state - gold)
        var buffer = new StationViewModel("Buffer")
        {
            X = 1050,
            Y = 30,
            CurrentState = "occupied",
            CurrentWafer = 2
        };
        Stations.Add(buffer);
    }

    private void InitializeSampleRobots()
    {
        // Robot 1 (idle)
        var robot1 = new StationViewModel("Robot 1")
        {
            X = 350,
            Y = 300,
            CurrentState = "idle",
            CurrentWafer = null
        };
        Robots.Add(robot1);

        // Robot 2 (carrying wafer)
        var robot2 = new StationViewModel("Robot 2")
        {
            X = 520,
            Y = 300,
            CurrentState = "carrying",
            CurrentWafer = 8
        };
        Robots.Add(robot2);

        // Robot 3 (idle)
        var robot3 = new StationViewModel("Robot 3")
        {
            X = 690,
            Y = 300,
            CurrentState = "idle",
            CurrentWafer = null
        };
        Robots.Add(robot3);
    }

    private void InitializeSampleWafers()
    {
        for (int i = 1; i <= 25; i++)
        {
            Wafers.Add(new Wafer(i)
            {
                CurrentStation = i <= 5 ? "Carrier" : "Processing",
                ProcessingState = i <= 5 ? "NotProcessed" : (i <= 15 ? "Polished" : "Cleaned")
            });
        }
    }

    /// <summary>
    /// Factory method to create design-time instance with sample data
    /// </summary>
    public static DesignTimeMainViewModel Create()
    {
        return new DesignTimeMainViewModel();
    }
}
