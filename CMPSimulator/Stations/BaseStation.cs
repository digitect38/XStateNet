namespace CMPSimulator.Stations;

/// <summary>
/// Base class for all stations with state machine behavior
/// Each station is independent and communicates via events
/// </summary>
public abstract class BaseStation
{
    public string Name { get; }
    public int MaxCapacity { get; }
    public List<int> WaferSlots { get; } = new();

    protected BaseStation(string name, int maxCapacity)
    {
        Name = name;
        MaxCapacity = maxCapacity;
    }

    public bool CanAcceptWafer() => WaferSlots.Count < MaxCapacity;

    public virtual void AddWafer(int waferId)
    {
        if (!CanAcceptWafer())
            throw new InvalidOperationException($"{Name} is full!");
        WaferSlots.Add(waferId);
        OnWaferAdded(waferId);
    }

    public virtual void RemoveWafer(int waferId)
    {
        if (!WaferSlots.Remove(waferId))
            throw new InvalidOperationException($"Wafer {waferId} not found in {Name}");
        OnWaferRemoved(waferId);
    }

    protected virtual void OnWaferAdded(int waferId) { }
    protected virtual void OnWaferRemoved(int waferId) { }

    // Events for communication
    public event EventHandler<WaferEventArgs>? WaferReady;
    public event EventHandler<TransferRequestEventArgs>? TransferRequested;
    public event EventHandler<string>? LogMessage;

    protected void RaiseWaferReady(int waferId, string destination)
    {
        WaferReady?.Invoke(this, new WaferEventArgs(waferId, destination));
    }

    protected void RaiseTransferRequest(int waferId, string from, string to)
    {
        TransferRequested?.Invoke(this, new TransferRequestEventArgs(waferId, from, to));
    }

    protected void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{Name}] {message}");
    }
}

public class WaferEventArgs : EventArgs
{
    public int WaferId { get; }
    public string Destination { get; }

    public WaferEventArgs(int waferId, string destination)
    {
        WaferId = waferId;
        Destination = destination;
    }
}

public class TransferRequestEventArgs : EventArgs
{
    public int WaferId { get; }
    public string From { get; }
    public string To { get; }

    public TransferRequestEventArgs(int waferId, string from, string to)
    {
        WaferId = waferId;
        From = from;
        To = to;
    }
}
