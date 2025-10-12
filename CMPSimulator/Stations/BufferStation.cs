namespace CMPSimulator.Stations;

/// <summary>
/// Buffer station with state machine behavior
/// States: Empty → Occupied → Dispatching → Empty
/// </summary>
public class BufferStation : BaseStation
{
    public enum State
    {
        Empty,
        Occupied,
        Dispatching
    }

    private State _currentState = State.Empty;
    private int? _currentWaferId;

    public State CurrentState => _currentState;

    public BufferStation(string name)
        : base(name, maxCapacity: 1)
    {
    }

    public override void AddWafer(int waferId)
    {
        if (_currentState != State.Empty)
        {
            throw new InvalidOperationException($"{Name} is not empty");
        }

        base.AddWafer(waferId);
        _currentWaferId = waferId;
        TransitionTo(State.Occupied);
        Log($"Stored Wafer {waferId}");

        // Immediately prepare to dispatch
        CheckAndDispatch();
    }

    private async void CheckAndDispatch()
    {
        if (!_currentWaferId.HasValue) return;

        // Small delay to allow destination to be ready
        await Task.Delay(100);

        if (_currentState == State.Occupied)
        {
            var waferId = _currentWaferId.Value;
            TransitionTo(State.Dispatching);
            Log($"Wafer {waferId} ready for dispatch to LoadPort");

            // Notify that wafer is ready
            RaiseWaferReady(waferId, "LoadPort");
        }
    }

    public void PickupWafer(int waferId)
    {
        if (_currentState != State.Dispatching)
        {
            throw new InvalidOperationException($"{Name} not in dispatching state");
        }

        if (_currentWaferId != waferId)
        {
            throw new InvalidOperationException($"Wrong wafer ID");
        }

        RemoveWafer(waferId);
        _currentWaferId = null;
        TransitionTo(State.Empty);
        Log($"Wafer {waferId} picked up, now Empty");
    }

    private void TransitionTo(State newState)
    {
        var oldState = _currentState;
        _currentState = newState;
        Log($"State: {oldState} → {newState}");
    }
}
