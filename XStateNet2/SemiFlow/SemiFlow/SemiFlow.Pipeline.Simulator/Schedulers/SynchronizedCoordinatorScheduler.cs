using SemiFlow.Pipeline.Simulator.Models;
using SemiFlow.Pipeline.Simulator.Pipeline;
using Spectre.Console;

namespace SemiFlow.Pipeline.Simulator.Schedulers;

/// <summary>
/// Level 1: Synchronized Coordinator Scheduler
/// Manages synchronized batch processing where all wafers move through stages together
/// </summary>
public class SynchronizedCoordinatorScheduler
{
    private readonly PipelineState _state;
    private readonly ResourceScheduler _resourceScheduler;
    private readonly List<PipelineWafer> _activeWafers = new();

    public SynchronizedCoordinatorScheduler(PipelineState state, ResourceScheduler resourceScheduler)
    {
        _state = state;
        _resourceScheduler = resourceScheduler;
    }

    public async Task RunAsync()
    {
        InitializeCoordinator();

        // Wait for resources to be ready
        while (!AllResourcesReady())
        {
            await Task.Delay(100);
        }

        Log("[COORD] All resources ready, starting synchronized coordinator", Color.Cyan1);

        // Start resource schedulers
        var resourceTask = _resourceScheduler.RunAsync();
        Log("[COORD] Resource scheduler started", Color.Grey);

        // Process wafers in synchronized batches
        await ProcessSynchronizedBatches();

        // Shutdown
        _state.SystemRunning = false;
        await resourceTask;

        FinalizeCoordinator();
    }

    private void InitializeCoordinator()
    {
        Log("[COORD] Initializing synchronized coordinator scheduler", Color.Cyan1);

        // Create wafers
        for (int i = 1; i <= _state.TotalWafers; i++)
        {
            var wafer = new PipelineWafer
            {
                Id = i,
                LotId = _state.LotId,
                StartTime = DateTime.Now,
                CurrentStage = WaferStage.InFoup
            };
            _state.WaitingWafers.Enqueue(wafer);
        }

        Log($"[COORD] Created {_state.TotalWafers} wafers for synchronized processing", Color.Green);
    }

    private bool AllResourcesReady()
    {
        return _state.Resources.Values.All(r => r.Status == ResourceStatus.Idle);
    }

    private async Task ProcessSynchronizedBatches()
    {
        // In synchronized mode with single-capacity stations (1 Robot, 1 Platen1, 1 Platen2),
        // we process one wafer at a time through the entire pipeline
        Log("[COORD] ═══════════════════════════════════════════", Color.Cyan1);
        Log("[COORD] Synchronized flow: One wafer at a time", Color.Yellow);
        Log("[COORD] Station capacity: Robot=1, Platen1=1, Platen2=1", Color.Yellow);
        Log("[COORD] ═══════════════════════════════════════════", Color.Cyan1);

        int waferNumber = 1;
        while (_state.WaitingWafers.Count > 0)
        {
            if (_state.WaitingWafers.TryDequeue(out var wafer))
            {
                _state.ActiveWafers.Add(wafer);
                _state.WafersDispatched++;

                Log($"[COORD] ═══════════════════════════════════════════", Color.Cyan1);
                Log($"[COORD] Processing wafer {waferNumber}/{_state.TotalWafers}: W{wafer.Id}", Color.Yellow);
                Log($"[COORD] ═══════════════════════════════════════════", Color.Cyan1);

                // Process this single wafer through all stages
                await ProcessWaferThroughPipeline(wafer);

                Log($"[COORD] Wafer W{wafer.Id} completed", Color.Green);
                Log($"[COORD] ═══════════════════════════════════════════", Color.Cyan1);

                waferNumber++;
            }
        }

        Log("[COORD] All wafers processed", Color.Green);
    }

    private async Task ProcessWaferThroughPipeline(PipelineWafer wafer)
    {
        // Stage 1: Load wafer from FOUP to Platen1
        await LoadToPlaten1(wafer);

        // Stage 2: Process wafer on Platen1
        await ProcessOnPlaten1(wafer);

        // Stage 3: Transfer wafer from Platen1 to Platen2
        await TransferToPlaten2(wafer);

        // Stage 4: Process wafer on Platen2
        await ProcessOnPlaten2(wafer);

        // Stage 5: Unload wafer from Platen2 to FOUP
        await UnloadToFoup(wafer);

        // Complete wafer
        CompleteWafer(wafer);
    }

    private async Task LoadToPlaten1(PipelineWafer wafer)
    {
        wafer.EnterStage(WaferStage.LoadingToPlaten1);
        Log($"[W{wafer.Id}] Stage 1: Loading FOUP → Platen1", Color.Blue);

        // Robot operation: pick from FOUP, move, place on PLATEN1
        await _resourceScheduler.RequestRobotAsync(wafer, "FOUP", "PLATEN1");

        wafer.ExitStage();
        wafer.EnterStage(WaferStage.OnPlaten1, "PLATEN1");
        Log($"[W{wafer.Id}] Placed on Platen1", Color.Green);
    }

    private async Task ProcessOnPlaten1(PipelineWafer wafer)
    {
        wafer.ExitStage();
        wafer.EnterStage(WaferStage.ProcessingPlaten1, "PLATEN1");
        Log($"[W{wafer.Id}] Stage 2: Processing on Platen1", Color.Orange1);

        // Platen1 processing
        await _resourceScheduler.RequestPlaten1ProcessingAsync(wafer);

        wafer.ExitStage();
        Log($"[W{wafer.Id}] Platen1 processing complete", Color.Green);
    }

    private async Task TransferToPlaten2(PipelineWafer wafer)
    {
        wafer.EnterStage(WaferStage.TransferringToPlaten2);
        Log($"[W{wafer.Id}] Stage 3: Transferring Platen1 → Platen2", Color.Blue);

        // Robot operation: pick from PLATEN1, move, place on PLATEN2
        await _resourceScheduler.RequestRobotAsync(wafer, "PLATEN1", "PLATEN2");

        wafer.ExitStage();
        wafer.EnterStage(WaferStage.OnPlaten2, "PLATEN2");
        Log($"[W{wafer.Id}] Placed on Platen2", Color.Green);
    }

    private async Task ProcessOnPlaten2(PipelineWafer wafer)
    {
        wafer.ExitStage();
        wafer.EnterStage(WaferStage.ProcessingPlaten2, "PLATEN2");
        Log($"[W{wafer.Id}] Stage 4: Processing on Platen2", Color.Orange1);

        // Platen2 processing
        await _resourceScheduler.RequestPlaten2ProcessingAsync(wafer);

        wafer.ExitStage();
        Log($"[W{wafer.Id}] Platen2 processing complete", Color.Green);
    }

    private async Task UnloadToFoup(PipelineWafer wafer)
    {
        wafer.EnterStage(WaferStage.UnloadingToFoup);
        Log($"[W{wafer.Id}] Stage 5: Unloading Platen2 → FOUP", Color.Blue);

        // Robot operation: pick from PLATEN2, move, place in FOUP
        await _resourceScheduler.RequestRobotAsync(wafer, "PLATEN2", "FOUP");

        wafer.ExitStage();
        Log($"[W{wafer.Id}] Returned to FOUP", Color.Green);
    }

    private void CompleteWafer(PipelineWafer wafer)
    {
        wafer.EnterStage(WaferStage.Completed);
        wafer.EndTime = DateTime.Now;
        wafer.IsActive = false;

        _state.ActiveWafers.Remove(wafer);
        _state.CompletedWafers.Add(wafer);
        _state.WafersCompleted++;

        Log($"[COORD] W{wafer.Id} ✓ COMPLETED (Cycle: {wafer.CycleTime.TotalSeconds:F1}s)", Color.Green);
    }

    private void FinalizeCoordinator()
    {
        _state.EndTime = DateTime.Now;
        Log("[COORD] Synchronized coordinator finalized", Color.Green);
    }

    private void Log(string message, Color color)
    {
        AnsiConsole.MarkupLine($"[{color}]{message.EscapeMarkup()}[/]");
    }
}
