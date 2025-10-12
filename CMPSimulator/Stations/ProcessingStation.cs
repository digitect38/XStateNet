namespace CMPSimulator.Stations;

/// <summary>
/// Processing station (Polisher or Cleaner) with state machine behavior
/// States: Idle → Processing → Dispatching → Idle
/// </summary>
public class ProcessingStation : BaseStation
{
    public enum State
    {
        Idle,
        Processing,
        Dispatching
    }

    private State _currentState = State.Idle;
    private int? _currentWaferId;
    private readonly int _processingTimeMs;
    private CancellationTokenSource? _processingCancellation;

    public State CurrentState => _currentState;

    public ProcessingStation(string name, int processingTimeMs)
        : base(name, maxCapacity: 1)
    {
        _processingTimeMs = processingTimeMs;
    }

    public override void AddWafer(int waferId)
    {
        if (_currentState != State.Idle)
        {
            Log($"❌ Cannot accept wafer {waferId} in state {_currentState}");
            throw new InvalidOperationException($"{Name} is not idle");
        }

        base.AddWafer(waferId);
        _currentWaferId = waferId;
        TransitionTo(State.Processing);
        StartProcessing();
    }

    private async void StartProcessing()
    {
        if (!_currentWaferId.HasValue) return;

        _processingCancellation = new CancellationTokenSource();
        var waferId = _currentWaferId.Value;

        Log($"⚙️  Processing Wafer {waferId} ({_processingTimeMs}ms)");

        try
        {
            await Task.Delay(_processingTimeMs, _processingCancellation.Token);

            if (_currentState == State.Processing && _currentWaferId == waferId)
            {
                Log($"✓ Wafer {waferId} processing complete");
                TransitionTo(State.Dispatching);

                // Notify that wafer is ready for pickup
                RaiseWaferReady(waferId, GetNextDestination());
            }
        }
        catch (OperationCanceledException)
        {
            Log($"Processing cancelled for Wafer {waferId}");
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
        TransitionTo(State.Idle);
        Log($"Wafer {waferId} picked up, now Idle");
    }

    private void TransitionTo(State newState)
    {
        var oldState = _currentState;
        _currentState = newState;
        Log($"State: {oldState} → {newState}");
    }

    private string GetNextDestination()
    {
        return Name == "Polisher" ? "Cleaner" : "Buffer";
    }

    public void Cancel()
    {
        _processingCancellation?.Cancel();
    }
}
