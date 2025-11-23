using SemiFlow.Pipeline.Simulator.Models;
using SemiFlow.Pipeline.Simulator.Pipeline;
using Spectre.Console;

namespace SemiFlow.Pipeline.Simulator.Schedulers;

/// <summary>
/// Level 3: Resource Scheduler
/// Manages robot, Platen1, and Platen2 resources
/// </summary>
public class ResourceScheduler
{
    private readonly PipelineState _state;
    private readonly SemaphoreSlim _robotLock = new(1, 1);
    private readonly SemaphoreSlim _platen1Lock = new(1, 1);
    private readonly SemaphoreSlim _platen2Lock = new(1, 1);

    private readonly Random _random = new();

    public ResourceScheduler(PipelineState state)
    {
        _state = state;
        InitializeResources();
    }

    private void InitializeResources()
    {
        _state.Resources["ROBOT"] = new ResourceState
        {
            Id = "ROBOT",
            Type = ResourceType.Robot,
            Status = ResourceStatus.Idle
        };

        _state.Resources["PLATEN1"] = new ResourceState
        {
            Id = "PLATEN1",
            Type = ResourceType.Platen1,
            Status = ResourceStatus.Idle
        };

        _state.Resources["PLATEN2"] = new ResourceState
        {
            Id = "PLATEN2",
            Type = ResourceType.Platen2,
            Status = ResourceStatus.Idle
        };

        _state.Resources["FOUP"] = new ResourceState
        {
            Id = "FOUP",
            Type = ResourceType.Foup,
            Status = ResourceStatus.Idle
        };
    }

    public async Task RunAsync()
    {
        // Resource schedulers run in background monitoring state
        while (_state.SystemRunning)
        {
            await Task.Delay(1000);
            UpdateResourceMetrics();
        }
    }

    // Robot operations
    public async Task RequestRobotAsync(PipelineWafer wafer, string from, string to)
    {
        await _robotLock.WaitAsync();

        try
        {
            var robot = _state.Resources["ROBOT"];
            robot.MarkBusy(wafer);

            Log($"[ROBOT] Acquired for W{wafer.Id}: {from} → {to}", Color.Magenta1);

            // Simulate robot operations
            await Task.Delay(_random.Next(1000, 2000)); // Pick
            Log($"[ROBOT] Pick from {from}", Color.Grey);

            await Task.Delay(_random.Next(1500, 2500)); // Move
            Log($"[ROBOT] Move to {to}", Color.Grey);

            await Task.Delay(_random.Next(1000, 2000)); // Place
            Log($"[ROBOT] Place on {to}", Color.Grey);

            robot.MarkIdle();
            Log($"[ROBOT] Released (Tasks: {robot.TasksCompleted})", Color.Magenta1);
        }
        finally
        {
            _robotLock.Release();
        }
    }

    // Platen1 operations
    public async Task RequestPlaten1ProcessingAsync(PipelineWafer wafer)
    {
        await _platen1Lock.WaitAsync();

        try
        {
            var platen = _state.Resources["PLATEN1"];
            platen.Status = ResourceStatus.Processing;
            platen.CurrentWafer = wafer;
            platen.BusySince = DateTime.Now;

            Log($"[PLATEN1] Processing W{wafer.Id}", Color.Orange1);

            // Simulate CMP processing (5-7 seconds scaled down)
            await Task.Delay(_random.Next(5000, 7000));

            if (platen.BusySince.HasValue)
            {
                platen.TotalBusyTime += (DateTime.Now - platen.BusySince.Value).TotalSeconds;
            }

            platen.Status = ResourceStatus.Idle;
            platen.CurrentWafer = null;
            platen.BusySince = null;
            platen.TasksCompleted++;

            Log($"[PLATEN1] W{wafer.Id} complete (Processed: {platen.TasksCompleted})", Color.Green);
        }
        finally
        {
            _platen1Lock.Release();
        }
    }

    // Platen2 operations
    public async Task RequestPlaten2ProcessingAsync(PipelineWafer wafer)
    {
        await _platen2Lock.WaitAsync();

        try
        {
            var platen = _state.Resources["PLATEN2"];
            platen.Status = ResourceStatus.Processing;
            platen.CurrentWafer = wafer;
            platen.BusySince = DateTime.Now;

            Log($"[PLATEN2] Processing W{wafer.Id}", Color.Orange1);

            // Simulate CMP processing (5-7 seconds scaled down)
            await Task.Delay(_random.Next(5000, 7000));

            if (platen.BusySince.HasValue)
            {
                platen.TotalBusyTime += (DateTime.Now - platen.BusySince.Value).TotalSeconds;
            }

            platen.Status = ResourceStatus.Idle;
            platen.CurrentWafer = null;
            platen.BusySince = null;
            platen.TasksCompleted++;

            Log($"[PLATEN2] W{wafer.Id} complete (Processed: {platen.TasksCompleted})", Color.Green);
        }
        finally
        {
            _platen2Lock.Release();
        }
    }

    private void UpdateResourceMetrics()
    {
        // Metrics are updated via GetUtilization() when needed
    }

    private void Log(string message, Color color)
    {
        AnsiConsole.MarkupLine($"[{color}]{message.EscapeMarkup()}[/]");
    }
}
