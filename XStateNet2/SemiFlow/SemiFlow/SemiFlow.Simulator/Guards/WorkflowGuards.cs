using SemiFlow.Simulator.Models;
using SemiFlow.Simulator.Simulation;

namespace SemiFlow.Simulator.Guards;

public class WorkflowGuards
{
    private readonly SimulationState _state;

    public WorkflowGuards(SimulationState state)
    {
        _state = state;
    }

    public bool HasMoreWafers()
    {
        return _state.WaferQueue.Count > 0 || _state.ProcessedWafers < _state.TotalWafers;
    }

    public bool IsTwoStepProcess()
    {
        return _state.CurrentWafer?.ProcessType == ProcessType.TwoStep;
    }

    public bool PlatenProcessComplete()
    {
        if (_state.SelectedPlaten == null) return false;

        var platen = _state.Stations[_state.SelectedPlaten];
        return platen.State == StationState.Idle;
    }

    public bool SystemRunning()
    {
        return _state.SystemRunning;
    }

    public bool IsCriticalError()
    {
        return _state.ErrorCount > 5; // More than 5 errors is critical
    }

    public bool ShouldContinueLoop()
    {
        return HasMoreWafers() && _state.SystemRunning;
    }

    public bool IsReady()
    {
        // Check if system is ready for next operation
        return _state.SystemRunning && !IsCriticalError();
    }

    public bool ResourceReady()
    {
        // Check if required resources are available
        return _state.Stations["ROBOT1"].State == StationState.Idle;
    }
}
