using SemiFlow.Pipeline.Simulator.Models;
using SemiFlow.Pipeline.Simulator.Pipeline;
using Spectre.Console;

namespace SemiFlow.Pipeline.Simulator.Schedulers;

/// <summary>
/// Level 1: Coordinator Scheduler
/// Manages overall system orchestration and wafer dispatching
/// </summary>
public class CoordinatorScheduler
{
    private readonly PipelineState _state;
    private readonly ResourceScheduler _resourceScheduler;
    private readonly List<Task> _waferTasks = new();
    private readonly SemaphoreSlim _dispatchSemaphore;

    public CoordinatorScheduler(PipelineState state, ResourceScheduler resourceScheduler)
    {
        _state = state;
        _resourceScheduler = resourceScheduler;
        _dispatchSemaphore = new SemaphoreSlim(state.MaxPipelineDepth, state.MaxPipelineDepth);
    }

    public async Task RunAsync()
    {
        InitializeCoordinator();

        // Wait for resources to be ready
        while (!AllResourcesReady())
        {
            await Task.Delay(100);
        }

        Log("[COORD] All resources ready, starting coordinator", Color.Cyan1);

        // Start resource schedulers
        var resourceTask = _resourceScheduler.RunAsync();
        Log("[COORD] Resource scheduler started", Color.Grey);

        // Dispatch wafers
        Log("[COORD] Starting wafer dispatch...", Color.Grey);
        var dispatchTask = DispatchWafersAsync();

        // Wait for all wafers to complete
        await dispatchTask;
        await Task.WhenAll(_waferTasks);

        // Wait for pipeline to empty
        while (!_state.PipelineEmpty)
        {
            await Task.Delay(1000);
        }

        // Shutdown
        _state.SystemRunning = false;
        await resourceTask;

        FinalizeCoordinator();
    }

    private void InitializeCoordinator()
    {
        Log("[COORD] Initializing coordinator scheduler", Color.Cyan1);

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

        Log($"[COORD] Created {_state.TotalWafers} wafers for processing", Color.Green);
    }

    private bool AllResourcesReady()
    {
        return _state.Resources.Values.All(r => r.Status == ResourceStatus.Idle);
    }

    private async Task DispatchWafersAsync()
    {
        while (_state.WaitingWafers.Count > 0)
        {
            // Wait for pipeline capacity
            await _dispatchSemaphore.WaitAsync();

            if (_state.WaitingWafers.TryDequeue(out var wafer))
            {
                _state.WafersDispatched++;
                _state.ActiveWafers.Add(wafer);

                Log($"[COORD] Dispatching wafer {wafer.Id} (Pipeline depth: {_state.CurrentPipelineDepth})", Color.Yellow);

                // Create wafer scheduler instance
                var waferScheduler = new WaferScheduler(wafer, _state, _resourceScheduler, _dispatchSemaphore);
                var task = waferScheduler.RunAsync();
                _waferTasks.Add(task);

                // Control dispatch rate
                await Task.Delay(2000); // 2 second dispatch interval
            }
        }

        Log("[COORD] All wafers dispatched", Color.Green);
    }

    private void FinalizeCoordinator()
    {
        _state.EndTime = DateTime.Now;
        Log("[COORD] Coordinator finalized", Color.Green);
    }

    private void Log(string message, Color color)
    {
        AnsiConsole.MarkupLine($"[{color}]{message.EscapeMarkup()}[/]");
    }
}
