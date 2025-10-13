using System.Collections.ObjectModel;
using System.ComponentModel;
using CMPSimulator.Models;

namespace CMPSimulator.Controllers;

/// <summary>
/// Execution mode for the simulator
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Async mode: Event-driven automatic execution
    /// </summary>
    Async,

    /// <summary>
    /// Sync mode: Step-by-step manual execution
    /// </summary>
    Sync
}

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
    void SetExecutionMode(ExecutionMode mode);
    void UpdateSettings(int r1Transfer, int polisher, int r2Transfer, int cleaner, int r3Transfer, int bufferHold, int loadPortReturn);
}
