using System;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

/// <summary>
/// Unit tests for E84HandoffMachine (SEMI E84 standard)
/// Tests the orchestrator-based implementation
/// </summary>
public class E84HandoffMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E84HandoffMachine _e84Machine;

    public E84HandoffMachineTests()
    {
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 4,
            EnableMetrics = false
        };

        _orchestrator = new EventBusOrchestrator(config);
        _e84Machine = new E84HandoffMachine("LP01", _orchestrator);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Start_In_Idle_State()
    {
        // Act
        await _e84Machine.StartAsync();

        // Assert
        var currentState = _e84Machine.GetCurrentState();
        Assert.Contains("idle", currentState);
        Assert.False(_e84Machine.LoadRequest);
        Assert.False(_e84Machine.UnloadRequest);
        Assert.False(_e84Machine.Ready);
        Assert.False(_e84Machine.EsInterlock);
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Transition_To_NotReady_When_CS0_On()
    {
        // Arrange
        await _e84Machine.StartAsync();

        // Act
        var result = await _e84Machine.SetCS0Async(true);

        // Assert
        AssertState(result, "notReady");
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Set_LoadRequest_In_ReadyToLoad_State()
    {
        // Arrange
        await _e84Machine.StartAsync();

        // Act - Trigger CS_0_ON to enter notReady
        var result = await _e84Machine.SetCS0Async(true);
        AssertState(result, "notReady");

        // Trigger READY to enter readyToLoad (this will set LoadRequest)
        result = await _e84Machine.SetReadyAsync();

        // Assert
        AssertState(result, "readyToLoad");
        Assert.True(_e84Machine.LoadRequest);
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Transition_Through_Load_Sequence()
    {
        // Arrange
        await _e84Machine.StartAsync();

        // Act - Complete load sequence
        // 1. CS_0_ON (carrier arrives)
        var result = await _e84Machine.SetCS0Async(true);
        AssertState(result, "notReady");

        // 2. READY (ready to accept carrier)
        result = await _e84Machine.SetReadyAsync();
        AssertState(result, "readyToLoad");
        Assert.True(_e84Machine.LoadRequest);

        // 3. TR_REQ_ON (transport system requests transfer)
        result = await _e84Machine.SetTransferRequestAsync(true);
        AssertState(result, "transferReady");
        Assert.True(_e84Machine.Ready);

        // 4. BUSY_ON (transfer in progress)
        result = await _e84Machine.SetBusyAsync(true);
        AssertState(result, "transferring");

        // 5. COMPT_ON (transfer complete)
        result = await _e84Machine.SetCompleteAsync(true);
        AssertState(result, "transferComplete");
        Assert.False(_e84Machine.LoadRequest);

        // 6. TR_REQ_OFF (return to idle)
        result = await _e84Machine.SetTransferRequestAsync(false);
        AssertState(result, "idle");
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Transition_Through_Unload_Sequence()
    {
        // Arrange
        await _e84Machine.StartAsync();

        // Act - Complete unload sequence
        // 1. VALID_ON (valid carrier ready to unload)
        var result = await _e84Machine.SetValidAsync(true);
        AssertState(result, "waitingForTransfer");

        // 2. TR_REQ_ON (transport system ready to receive)
        result = await _e84Machine.SetTransferRequestAsync(true);
        AssertState(result, "readyToUnload");
        Assert.True(_e84Machine.UnloadRequest);

        // 3. BUSY_ON (unload in progress)
        result = await _e84Machine.SetBusyAsync(true);
        AssertState(result, "unloading");

        // 4. COMPT_ON (unload complete)
        result = await _e84Machine.SetCompleteAsync(true);
        AssertState(result, "unloadComplete");
        Assert.False(_e84Machine.UnloadRequest);

        // 5. VALID_OFF (return to idle)
        result = await _e84Machine.SetValidAsync(false);
        AssertState(result, "idle");
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Enter_TransferBlocked_On_Timeout()
    {
        // Arrange
        await _e84Machine.StartAsync();

        // Act - Enter readyToLoad but don't send TR_REQ (will timeout after 30s)
        var result = await _e84Machine.SetCS0Async(true);
        AssertState(result, "notReady");

        result = await _e84Machine.SetReadyAsync();
        AssertState(result, "readyToLoad");

        // For testing, manually trigger timeout event
        result = await _orchestrator.SendEventAsync("SYSTEM", _e84Machine.MachineId, "TIMEOUT", null);

        // Assert
        AssertState(result, "transferBlocked");
        Assert.True(_e84Machine.EsInterlock);
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Clear_Alarm_On_Reset()
    {
        // Arrange
        await _e84Machine.StartAsync();

        // Enter transferBlocked state
        var result = await _e84Machine.SetCS0Async(true);
        AssertState(result, "notReady");

        result = await _e84Machine.SetReadyAsync();
        AssertState(result, "readyToLoad");

        result = await _orchestrator.SendEventAsync("SYSTEM", _e84Machine.MachineId, "TIMEOUT", null);
        AssertState(result, "transferBlocked");
        Assert.True(_e84Machine.EsInterlock);

        // Act - Reset
        result = await _e84Machine.ResetAsync();

        // Assert
        AssertState(result, "idle");
        Assert.False(_e84Machine.EsInterlock);
        Assert.False(_e84Machine.LoadRequest);
        Assert.False(_e84Machine.UnloadRequest);
        Assert.False(_e84Machine.Ready);
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Return_To_Idle_On_CS0_Off_From_NotReady()
    {
        // Arrange
        await _e84Machine.StartAsync();

        var result = await _e84Machine.SetCS0Async(true);
        AssertState(result, "notReady");

        // Act
        result = await _e84Machine.SetCS0Async(false);

        // Assert
        AssertState(result, "idle");
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Clear_Ready_When_Exiting_TransferComplete()
    {
        // Arrange - Complete load sequence to transferComplete
        await _e84Machine.StartAsync();

        var result = await _e84Machine.SetCS0Async(true);
        AssertState(result, "notReady");

        result = await _e84Machine.SetReadyAsync();
        AssertState(result, "readyToLoad");

        result = await _e84Machine.SetTransferRequestAsync(true);
        AssertState(result, "transferReady");
        Assert.True(_e84Machine.Ready);

        result = await _e84Machine.SetBusyAsync(true);
        AssertState(result, "transferring");

        result = await _e84Machine.SetCompleteAsync(true);
        AssertState(result, "transferComplete");

        // Act - Exit transferComplete to idle
        result = await _e84Machine.SetTransferRequestAsync(false);
        AssertState(result, "idle");

        // Assert
        Assert.False(_e84Machine.Ready);
        Assert.False(_e84Machine.HoAvailable);
    }

    [Fact]
    public async Task E84HandoffMachine_Should_Have_Correct_MachineId()
    {
        // Arrange & Act
        var machineId = _e84Machine.MachineId;

        // Assert
        Assert.StartsWith("E84_HANDOFF_LP01_", machineId);
    }
}
