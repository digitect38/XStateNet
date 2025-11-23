using SemiFlow.Pipeline.Simulator.Models;
using SemiFlow.Pipeline.Simulator.Pipeline;
using Spectre.Console;

namespace SemiFlow.Pipeline.Simulator.Schedulers;

/// <summary>
/// Level 2: Wafer Scheduler
/// Manages individual wafer workflow through pipeline stages
/// </summary>
public class WaferScheduler
{
    private readonly PipelineWafer _wafer;
    private readonly PipelineState _state;
    private readonly ResourceScheduler _resourceScheduler;
    private readonly SemaphoreSlim _pipelineSemaphore;

    public WaferScheduler(
        PipelineWafer wafer,
        PipelineState state,
        ResourceScheduler resourceScheduler,
        SemaphoreSlim pipelineSemaphore)
    {
        _wafer = wafer;
        _state = state;
        _resourceScheduler = resourceScheduler;
        _pipelineSemaphore = pipelineSemaphore;
    }

    public async Task RunAsync()
    {
        try
        {
            // Stage 1: Load from FOUP to Platen1
            await LoadFromFoupToPlaten1();

            // Stage 2: Process on Platen1
            await ProcessOnPlaten1();

            // Stage 3: Transfer from Platen1 to Platen2
            await TransferToPlaten2();

            // Stage 4: Process on Platen2
            await ProcessOnPlaten2();

            // Stage 5: Unload from Platen2 to FOUP
            await UnloadToFoup();

            // Complete
            CompleteWafer();
        }
        finally
        {
            // Release pipeline slot
            _pipelineSemaphore.Release();
        }
    }

    private async Task LoadFromFoupToPlaten1()
    {
        _wafer.EnterStage(WaferStage.LoadingToPlaten1);
        Log($"[W{_wafer.Id}] Stage 1: Loading FOUP → Platen1", Color.Blue);

        // Request robot
        await _resourceScheduler.RequestRobotAsync(_wafer, "FOUP", "PLATEN1");

        _wafer.ExitStage();
        _wafer.EnterStage(WaferStage.OnPlaten1, "PLATEN1");
        Log($"[W{_wafer.Id}] Placed on Platen1", Color.Green);
    }

    private async Task ProcessOnPlaten1()
    {
        _wafer.ExitStage();
        _wafer.EnterStage(WaferStage.ProcessingPlaten1, "PLATEN1");
        Log($"[W{_wafer.Id}] Stage 2: Processing on Platen1", Color.Orange1);

        // Request Platen1 processing
        await _resourceScheduler.RequestPlaten1ProcessingAsync(_wafer);

        _wafer.ExitStage();
        Log($"[W{_wafer.Id}] Platen1 processing complete", Color.Green);
    }

    private async Task TransferToPlaten2()
    {
        _wafer.EnterStage(WaferStage.TransferringToPlaten2);
        Log($"[W{_wafer.Id}] Stage 3: Transferring Platen1 → Platen2", Color.Blue);

        // Request robot
        await _resourceScheduler.RequestRobotAsync(_wafer, "PLATEN1", "PLATEN2");

        _wafer.ExitStage();
        _wafer.EnterStage(WaferStage.OnPlaten2, "PLATEN2");
        Log($"[W{_wafer.Id}] Placed on Platen2", Color.Green);
    }

    private async Task ProcessOnPlaten2()
    {
        _wafer.ExitStage();
        _wafer.EnterStage(WaferStage.ProcessingPlaten2, "PLATEN2");
        Log($"[W{_wafer.Id}] Stage 4: Processing on Platen2", Color.Orange1);

        // Request Platen2 processing
        await _resourceScheduler.RequestPlaten2ProcessingAsync(_wafer);

        _wafer.ExitStage();
        Log($"[W{_wafer.Id}] Platen2 processing complete", Color.Green);
    }

    private async Task UnloadToFoup()
    {
        _wafer.EnterStage(WaferStage.UnloadingToFoup);
        Log($"[W{_wafer.Id}] Stage 5: Unloading Platen2 → FOUP", Color.Blue);

        // Request robot
        await _resourceScheduler.RequestRobotAsync(_wafer, "PLATEN2", "FOUP");

        _wafer.ExitStage();
        Log($"[W{_wafer.Id}] Returned to FOUP", Color.Green);
    }

    private void CompleteWafer()
    {
        _wafer.EnterStage(WaferStage.Completed);
        _wafer.EndTime = DateTime.Now;
        _wafer.IsActive = false;

        _state.ActiveWafers.Remove(_wafer);
        _state.CompletedWafers.Add(_wafer);
        _state.WafersCompleted++;

        Log($"[W{_wafer.Id}] ✓ COMPLETED (Cycle: {_wafer.CycleTime.TotalSeconds:F1}s)", Color.Green);
    }

    private void Log(string message, Color color)
    {
        AnsiConsole.MarkupLine($"[{color}]{message.EscapeMarkup()}[/]");
    }
}
