using System.Collections.ObjectModel;
using System.ComponentModel;
using CMPSimulator.Models;

namespace CMPSimulator.Controllers;

/// <summary>
/// Common interface for Forward Priority Controller implementations
/// </summary>
public interface IForwardPriorityController : INotifyPropertyChanged, IDisposable
{
    ObservableCollection<Wafer> Wafers { get; }
    Dictionary<string, StationPosition> Stations { get; }

    string PolisherStatus { get; }
    string CleanerStatus { get; }
    string BufferStatus { get; }
    string R1Status { get; }
    string R2Status { get; }
    string R3Status { get; }

    event EventHandler<string>? LogMessage;
    event EventHandler? StationStatusChanged;

    Task StartSimulation();
    Task ExecuteOneStep();
    void StopSimulation();
    void ResetSimulation();
}
