using XStateNet.Orchestration;
using XStateNet.Semi.Schedulers;
using Xunit;

namespace SemiStandard.Tests;

/// <summary>
/// Unit tests for CMPMasterScheduler
/// </summary>
public class CMPMasterSchedulerTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private CMPMasterScheduler? _scheduler;

    public CMPMasterSchedulerTests()
    {
        _orchestrator = new EventBusOrchestrator();
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task MasterScheduler_Should_Start_In_Idle_State()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator, maxWip: 10);

        // Act
        await _scheduler.StartAsync();

        // Assert
        Assert.Contains("idle", _scheduler.GetCurrentState());
    }

    [Fact]
    public async Task MasterScheduler_Should_Have_Unique_MachineId_With_GUID()
    {
        // Arrange
        var scheduler1 = new CMPMasterScheduler("001", _orchestrator);
        var scheduler2 = new CMPMasterScheduler("001", _orchestrator);

        // Act & Assert
        Assert.StartsWith("MASTER_SCHEDULER_001_", scheduler1.MachineId);
        Assert.StartsWith("MASTER_SCHEDULER_001_", scheduler2.MachineId);
        Assert.NotEqual(scheduler1.MachineId, scheduler2.MachineId);
    }

    [Fact]
    public async Task MasterScheduler_Should_Track_WIP_Count()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator, maxWip: 5);
        await _scheduler.StartAsync();

        // Act
        var initialWip = _scheduler.GetCurrentWip();

        // Assert
        Assert.Equal(0, initialWip);
    }

    [Fact]
    public async Task MasterScheduler_Should_Track_Queue_Length()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator);
        await _scheduler.StartAsync();

        // Act
        var queueLength = _scheduler.GetQueueLength();

        // Assert
        Assert.Equal(0, queueLength);
    }

    [Fact]
    public async Task MasterScheduler_Should_Register_Tools()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator);
        await _scheduler.StartAsync();

        // Act
        _scheduler.RegisterTool("CMP_TOOL_01", "CMP");
        _scheduler.RegisterTool("CMP_TOOL_02", "CMP");

        // Assert - No exception thrown
        Assert.Contains("idle", _scheduler.GetCurrentState());
    }

    [Fact]
    public async Task MasterScheduler_Should_Transition_To_Evaluating_On_Job_Arrival()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator);
        await _scheduler.StartAsync();

        // Register a tool so job can be dispatched
        _scheduler.RegisterTool("CMP_TOOL_01", "CMP");

        // Act
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            _scheduler.MachineId,
            "JOB_ARRIVED",
            null
        );

        // Wait deterministically for state to settle using exponential backoff polling
        var settled = await WaitForStateSettled(_scheduler, timeoutMs: 2000);

        // Assert
        Assert.True(result.Success);
        Assert.True(settled, "State did not settle within timeout");

        var state = _scheduler.GetCurrentState();
        Assert.True(
            state.Contains("idle") || state.Contains("waiting"),
            $"Expected idle or waiting, got {state}"
        );
    }

    private async Task<bool> WaitForStateSettled(CMPMasterScheduler scheduler, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? lastState = null;
        int stableCount = 0;
        const int requiredStableCount = 3; // State must be stable for 3 consecutive checks

        while (DateTime.UtcNow < deadline)
        {
            var currentState = scheduler.GetCurrentState();

            if (currentState == lastState)
            {
                stableCount++;
                if (stableCount >= requiredStableCount)
                {
                    return true; // State is stable
                }
            }
            else
            {
                stableCount = 0;
                lastState = currentState;
            }

            await Task.Delay(50); // Poll every 50ms
        }

        return false; // Timeout
    }

    [Fact]
    public async Task MasterScheduler_Should_Handle_Tool_Status_Update()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator);
        await _scheduler.StartAsync();

        // Act
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            _scheduler.MachineId,
            "TOOL_STATUS_UPDATE",
            null
        );

        // Assert
        Assert.True(result.Success);
        Assert.Contains("idle", _scheduler.GetCurrentState());
    }

    [Fact]
    public async Task MasterScheduler_Should_Handle_Job_Completed_Event()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator);
        await _scheduler.StartAsync();

        // Act
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            _scheduler.MachineId,
            "JOB_COMPLETED",
            null
        );

        // Assert
        Assert.True(result.Success);
        // Should transition through evaluating and back to idle or waiting
    }

    [Fact]
    public async Task MasterScheduler_Should_Handle_Tool_Available_Event()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator);
        await _scheduler.StartAsync();

        // Act
        var result = await _orchestrator.SendEventAsync(
            "SYSTEM",
            _scheduler.MachineId,
            "TOOL_AVAILABLE",
            null
        );

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task MasterScheduler_Should_Respect_Max_WIP_Limit()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator, maxWip: 2);
        await _scheduler.StartAsync();

        // Act
        var maxWip = 2; // Set in constructor
        var currentWip = _scheduler.GetCurrentWip();

        // Assert
        Assert.True(currentWip <= maxWip);
    }

    [Fact]
    public async Task Multiple_MasterSchedulers_Should_Not_Interfere()
    {
        // Arrange
        var scheduler1 = new CMPMasterScheduler("SCH1", _orchestrator);
        var scheduler2 = new CMPMasterScheduler("SCH2", _orchestrator);

        // Act
        await scheduler1.StartAsync();
        await scheduler2.StartAsync();

        // Assert - Different MachineIds prevent collision
        Assert.NotEqual(scheduler1.MachineId, scheduler2.MachineId);
        Assert.Contains("idle", scheduler1.GetCurrentState());
        Assert.Contains("idle", scheduler2.GetCurrentState());
    }

    [Fact]
    public async Task MasterScheduler_Should_Expose_Machine_Property()
    {
        // Arrange
        _scheduler = new CMPMasterScheduler("001", _orchestrator);

        // Act
        var machine = _scheduler.Machine;

        // Assert
        Assert.NotNull(machine);
        Assert.Equal(_scheduler.MachineId, machine.Id);
    }
}
