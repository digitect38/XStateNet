using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleTaskLoopCMP;

/// <summary>
/// Akka.NET Task-Loop Scheduler: State Machine with Individual Pick/Place Operations
///
/// DESIGN:
/// - Each robot runs a continuous TaskLoop that checks its state and makes decisions
/// - Robots use their internal state (Empty, HasPW, HasNPW, HasWafer) to decide next action
/// - Pre-positioning and waiting loops are explicitly handled
/// - No message passing - direct state inspection
///
/// EXPECTED: Simpler than event-driven Akka, easier to understand and debug
/// </summary>
public class SimpleTaskLoopScheduler
{
    private readonly Carrier _carrier;
    private readonly Polisher _polisher;
    private readonly Cleaner _cleaner;
    private readonly Buffer _buffer;

    private readonly Robot _robot1;
    private readonly Robot _robot2;
    private readonly Robot _robot3;

    private readonly DateTime _startTime;
    private int _totalTransfersCompleted = 0;
    private int _totalPrePositions = 0;

    private CancellationTokenSource? _cts;

    public SimpleTaskLoopScheduler(
        Robot robot1,
        Robot robot2,
        Robot robot3,
        Carrier carrier,
        Polisher polisher,
        Cleaner cleaner,
        Buffer buffer)
    {
        _robot1 = robot1;
        _robot2 = robot2;
        _robot3 = robot3;
        _carrier = carrier;
        _polisher = polisher;
        _cleaner = cleaner;
        _buffer = buffer;
        _startTime = DateTime.UtcNow;

        Logger.Log("[TaskLoop] Scheduler initialized");
        Logger.Log("[TaskLoop]   R1: Carrier ↔ Polisher, Buffer → Carrier");
        Logger.Log("[TaskLoop]   R2: Polisher → Cleaner");
        Logger.Log("[TaskLoop]   R3: Cleaner → Buffer");
    }

    public async Task StartAsync()
    {
        Logger.Log("[TaskLoop] Starting scheduler...");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Start processing tasks for each station
        var polisherTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var polisherStation = (ProcessStationBase)_polisher;
                if (polisherStation.CurrentState == StationState.Idle)
                {
                    await polisherStation.ProcessWaferAsync(token);
                }
                await Task.Delay(10, token);
            }
        }, token);

        var cleanerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var cleanerStation = (ProcessStationBase)_cleaner;
                if (cleanerStation.CurrentState == StationState.Idle)
                {
                    await cleanerStation.ProcessWaferAsync(token);
                }
                await Task.Delay(10, token);
            }
        }, token);

        // Start robot task loops
        var r1Task = R1TaskLoop(token);
        var r2Task = R2TaskLoop(token);
        var r3Task = R3TaskLoop(token);

        Logger.Log("[TaskLoop] All task loops started");

        // Wait for completion
        while (!_carrier.AllProcessed && !token.IsCancellationRequested)
        {
            await Task.Delay(100, token);
        }
    }

    public void Stop()
    {
        Logger.Log($"[TaskLoop] Stopping scheduler (Transfers: {_totalTransfersCompleted}, Pre-positions: {_totalPrePositions})");
        _cts?.Cancel();
    }

    public void PrintStatistics()
    {
        var runtime = (DateTime.UtcNow - _startTime).TotalSeconds;
        Logger.Log($"");
        Logger.Log($"=== TaskLoop Scheduler Statistics ===");
        Logger.Log($"Runtime: {runtime:F1} seconds");
        Logger.Log($"Total transfers completed: {_totalTransfersCompleted}");
        Logger.Log($"Pre-positioning optimizations: {_totalPrePositions} ({100.0 * _totalPrePositions / Math.Max(1, _totalTransfersCompleted):F1}%)");
        Logger.Log($"Average throughput: {_totalTransfersCompleted / runtime:F2} transfers/sec");
        Logger.Log($"======================================");
    }

    // R1 Task Loop: Carrier ↔ Polisher, Buffer → Carrier
    private async Task R1TaskLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var polisherStation = (ProcessStationBase)_polisher;
                var bufferStation = (BufferStationBase)_buffer;

                // Priority 1: If R1 is empty AND Buffer has processed wafer → Pick from Buffer
                if (_robot1.IsEmpty && bufferStation.HasWafer)
                {
                    var wafer = bufferStation.GetWaferForValidation();
                    if (wafer?.IsProcessed == true)
                    {
                        await _robot1.MoveToAsync(_buffer);
                        await _robot1.PickAsync(_buffer);
                        Interlocked.Increment(ref _totalTransfersCompleted);
                    }
                }

                // Priority 2: If R1 has processed wafer → Place on Carrier
                if (_robot1.HasPW)
                {
                    await _robot1.MoveToAsync(_carrier);
                    await _robot1.PlaceAsync(_carrier);
                    await _robot1.MoveToHomeAsync();
                }

                // Priority 3: If R1 is empty AND Carrier has unprocessed wafer → Pick from Carrier
                if (_robot1.IsEmpty && _carrier.HaveNPW)
                {
                    await _robot1.MoveToAsync(_carrier);
                    await _robot1.PickAsync(_carrier);
                    Interlocked.Increment(ref _totalTransfersCompleted);
                }

                // Priority 4: If R1 has unprocessed wafer → PrePosition to Polisher
                if (_robot1.HasNPW)
                {
                    // Pre-position when polisher is AlmostDone
                    if (polisherStation.IsAlmostDone && !polisherStation.IsDone)
                    {
                        await _robot1.MoveToAsync(_polisher);
                        Interlocked.Increment(ref _totalPrePositions);
                        Logger.Log($"[TaskLoop] R1 pre-positioned to Polisher");
                    }

                    // Wait while Polisher is not empty
                    while (_robot1.HasNPW && !polisherStation.IsEmpty && !token.IsCancellationRequested)
                    {
                        await Task.Delay(1, token);
                    }

                    // Place wafer on Polisher when empty
                    if (_robot1.HasNPW && polisherStation.IsEmpty)
                    {
                        await _robot1.MoveToAsync(_polisher);
                        await _robot1.PlaceAsync(_polisher);
                        await _robot1.MoveToHomeAsync();
                    }
                }

                await Task.Delay(1, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // R2 Task Loop: Polisher → Cleaner
    private async Task R2TaskLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var polisherStation = (ProcessStationBase)_polisher;
                var cleanerStation = (ProcessStationBase)_cleaner;

                // If R2 is empty → PrePosition to Polisher
                if (_robot2.IsEmpty)
                {
                    // Pre-position when polisher is AlmostDone
                    if (polisherStation.IsAlmostDone && !polisherStation.IsDone)
                    {
                        await _robot2.MoveToAsync(_polisher);
                        Interlocked.Increment(ref _totalPrePositions);
                        Logger.Log($"[TaskLoop] R2 pre-positioned to Polisher");
                    }

                    // Pick from Polisher when Done
                    if (polisherStation.IsDone)
                    {
                        await _robot2.MoveToAsync(_polisher);
                        await _robot2.PickAsync(_polisher);
                        Interlocked.Increment(ref _totalTransfersCompleted);
                    }
                }

                // If R2 has wafer → PrePosition to Cleaner
                if (_robot2.HasWafer)
                {
                    // Pre-position when cleaner is AlmostDone
                    if (cleanerStation.IsAlmostDone && !cleanerStation.IsDone)
                    {
                        await _robot2.MoveToAsync(_cleaner);
                        Interlocked.Increment(ref _totalPrePositions);
                        Logger.Log($"[TaskLoop] R2 pre-positioned to Cleaner");
                    }

                    // Wait while Cleaner is not empty
                    while (_robot2.HasWafer && !cleanerStation.IsEmpty && !token.IsCancellationRequested)
                    {
                        await Task.Delay(1, token);
                    }

                    // Place wafer on Cleaner when empty
                    if (_robot2.HasWafer && cleanerStation.IsEmpty)
                    {
                        await _robot2.MoveToAsync(_cleaner);
                        await _robot2.PlaceAsync(_cleaner);
                        await _robot2.MoveToHomeAsync();
                    }
                }

                await Task.Delay(1, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // R3 Task Loop: Cleaner → Buffer
    private async Task R3TaskLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var cleanerStation = (ProcessStationBase)_cleaner;
                var bufferStation = (BufferStationBase)_buffer;

                // If R3 is empty → PrePosition to Cleaner
                if (_robot3.IsEmpty)
                {
                    // Pre-position when cleaner is AlmostDone
                    if (cleanerStation.IsAlmostDone && !cleanerStation.IsDone)
                    {
                        await _robot3.MoveToAsync(_cleaner);
                        Interlocked.Increment(ref _totalPrePositions);
                        Logger.Log($"[TaskLoop] R3 pre-positioned to Cleaner");
                    }

                    // Pick from Cleaner when Done
                    if (cleanerStation.IsDone)
                    {
                        await _robot3.MoveToAsync(_cleaner);
                        await _robot3.PickAsync(_cleaner);
                        Interlocked.Increment(ref _totalTransfersCompleted);
                    }
                }

                // If R3 has wafer → PrePosition to Buffer
                if (_robot3.HasWafer)
                {
                    // Pre-position to Buffer (buffer doesn't have AlmostDone, so just move)
                    if (bufferStation.IsEmpty)
                    {
                        await _robot3.MoveToAsync(_buffer);
                    }

                    // Wait while Buffer is not empty
                    while (_robot3.HasWafer && !bufferStation.IsEmpty && !token.IsCancellationRequested)
                    {
                        await Task.Delay(1, token);
                    }

                    // Place wafer on Buffer when empty
                    if (_robot3.HasWafer && bufferStation.IsEmpty)
                    {
                        await _robot3.MoveToAsync(_buffer);
                        await _robot3.PlaceAsync(_buffer);
                        await _robot3.MoveToHomeAsync();
                    }
                }

                await Task.Delay(1, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
