using System;
using System.Threading;
using System.Threading.Tasks;

var cts = new CancellationTokenSource();
var sequence = new CmpSequence();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
await sequence.SequenceLoop(cts.Token);

public class CmpSequence
{
    private Carrier carrier = new();
    private Loadport loadport = new();
    private Platen platen = new();
    private Cleaner cleaner = new();
    private Buffer buffer = new();
    private Robot R1 = new("R1");
    private Robot R2 = new("R2");
    private Robot R3 = new("R3");

    public async Task SequenceLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (carrier.IsArrived && loadport.IsEmpty)
                AttachCarrier();
            else
                continue;

            if (carrier.IsAttached)
                StartProcess();
            else
                continue;

            await ProcessAsync(token);

            if (carrier.IsAttached)
                DetachCarrier();
            else
                continue;
        }
    }

    private async Task ProcessAsync(CancellationToken token)
    {
        Console.WriteLine("=== Process started ===");

        await Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                // 병렬로 각 조건을 평가하고 실행
                var tasks = new[]
                {
                    Task.Run(() => { if (carrier.HaveNPW && platen.IsDone) R1.Pick(carrier); }),
                    Task.Run(() => { if (R1.HasNPW && platen.IsEmpty) R1.Place(platen); }),
                    Task.Run(() => { if (platen.IsDone && R1.IsEmpty) R2.Pick(platen); }),
                    Task.Run(() => { if (R2.HasWafer && cleaner.IsEmpty) R2.Place(cleaner); }),
                    Task.Run(() => { if (cleaner.IsDone && R3.IsEmpty) R3.Pick(cleaner); }),
                    Task.Run(() => { if (R3.HasWafer && buffer.IsEmpty) R3.Place(buffer); }),
                    Task.Run(() => { if (buffer.HasWafer && R1.IsEmpty) R1.Pick(buffer); }),
                    Task.Run(() => { if (R1.HasPW) R1.Place(carrier); })
                };

                await Task.WhenAll(tasks);

                if (AllStationsEmpty() && AllRobotsEmpty() && carrier.AllProcessed)
                {
                    Console.WriteLine("=== Process completed ===");
                    break;
                }

                await Task.Delay(100); // loop interval
            }
        });
    }

    private bool AllStationsEmpty() => platen.IsEmpty && cleaner.IsEmpty && buffer.IsEmpty;
    private bool AllRobotsEmpty() => !R1.HasWafer && !R2.HasWafer && !R3.HasWafer;

    private void AttachCarrier() => carrier.Attach(loadport);
    private void DetachCarrier() => carrier.Detach(loadport);
    private void StartProcess() => Console.WriteLine("Process started!");
}


public class Wafer
{
    public bool IsProcessed { get; set; } = false;
}

public enum ProcessStationState
{
    Empty,
    Idle,
    Processing,
    Done
}

public enum BufferStationState
{
    Empty,
    HasWafer
}

public interface IBufferStation
{
    BufferStationState CurrentState { get; }
    bool IsEmpty => CurrentState == BufferStationState.Empty;
    bool HasWafer => CurrentState == BufferStationState.HasWafer;
    Wafer? Pick();
    void Place(Wafer w);
}

public interface IProcessStation : IBufferStation
{
    new ProcessStationState CurrentState { get; }
    bool Processing => CurrentState == ProcessStationState.Processing;
    bool IsDone => CurrentState == ProcessStationState.Done;
}

public class Carrier : IBufferStation
{
    public bool IsArrived = false; 
    public BufferStationState CurrentState { get; } = BufferStationState.HasWafer;
    public Wafer?[] _wafers = new Wafer[25];

    public bool IsAttached { get; private set; }
    public bool HaveNPW => true;

    public bool AllProcessed {
        get {
            foreach (var w in _wafers)
                if(!w.IsProcessed) return false;
            return true;
        }
    }

    public Wafer? Pick()
    {
        Wafer? wafer = null;

        for (int i = 0; i < 25; i++)
        {
            if (!_wafers[i].IsProcessed)
            {
                wafer = _wafers[i];
                _wafers[i] = null;
                return wafer;
            }
        }

        return null;
    }

    public void Place(Wafer wafer)
    {
        wafer.IsProcessed = true;

        for (int i = 0; i < 25; i++)
        {
            if (_wafers[i] == null)
            {
                _wafers[i] = wafer;
            }
        }
    }

    public void Attach(Loadport lp) { IsAttached = true; Console.WriteLine("Carrier attached."); }
    public void Detach(Loadport lp) { IsAttached = false; Console.WriteLine("Carrier detached."); }
}

public class Loadport
{
    public bool IsEmpty => true;
}

public class BufferStationBase : IBufferStation
{
    protected Wafer? _wafer = null;

    public BufferStationState CurrentState { set; get; } = BufferStationState.Empty;
    public bool IsEmpty => CurrentState == BufferStationState.Empty;
    public bool HasWafer => CurrentState == BufferStationState.HasWafer;

    public void Place(Wafer w)
    {
        _wafer = w;
    }

    public Wafer? Pick()
    {
        var w = _wafer;
        _wafer = null;
        return w;
    }
}

public class ProcessStationBase : BufferStationBase
{
    public new ProcessStationState CurrentState { set; get; } = ProcessStationState.Empty;
    public bool IsDone => CurrentState == ProcessStationState.Done;
}

public class Platen : ProcessStationBase
{    
}

public class Cleaner : ProcessStationBase
{
}

public class Buffer : BufferStationBase
{    
}

public class Robot
{
    public string Name { get; }
    public bool HasPW => false;
    public bool HasNPW => false;
    public bool HasWafer => HasPW || HasNPW;
    public bool IsEmpty => !HasWafer;
    public Wafer? wafer = null;
    public Robot(string name) => Name = name;

    public void Pick(IBufferStation from)
    {
        wafer = from.Pick();
        Console.WriteLine($"{Name} pick {from}");
    }
    public void Place(IBufferStation to)
    {
        if(wafer == null) throw new Exception("wafer is null");
        to.Place(wafer);
        wafer = null;
        Console.WriteLine($"{Name} place {to}");
    }
}
