using System.Collections.ObjectModel;
using CMPSimXS2.WPF.Models;

namespace CMPSimXS2.WPF.Controllers;

public enum ExecutionMode
{
    Async,
    Sync
}

/// <summary>
/// Interface for the Forward Priority Controller
/// TODO: Implement with XStateNet2.Core state machines
/// </summary>
public interface IForwardPriorityController : IDisposable
{
    // Station status properties
    string R1Status { get; }
    string R2Status { get; }
    string R3Status { get; }
    string PolisherStatus { get; }
    string CleanerStatus { get; }
    string BufferStatus { get; }

    // Collections
    ObservableCollection<Wafer> Wafers { get; }

    // Events
    event EventHandler? StationStatusChanged;

    // Control methods
    void Start();
    void Stop();
    void Reset();
    void SetExecutionMode(ExecutionMode mode);

    // Simulation control methods
    Task StartSimulation();
    Task ExecuteOneStep();
    void StopSimulation();
    void ResetSimulation();

    // Settings update
    void UpdateSettings(int r1Transfer, int polisher, int r2Transfer, int cleaner, int r3Transfer, int bufferHold, int loadPortReturn);
}
