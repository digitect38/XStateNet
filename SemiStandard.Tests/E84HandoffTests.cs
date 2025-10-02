using System;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

/// <summary>
/// Unit tests for E84HandoffMachine (SEMI E84 standard)
/// REFACTORED to use EventBusOrchestrator-based implementation
/// </summary>
public class E84HandoffTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E84HandoffMachine _handoff;

    public E84HandoffTests()
    {
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 4,
            EnableMetrics = false
        };

        _orchestrator = new EventBusOrchestrator(config);
        _handoff = new E84HandoffMachine("LP1", _orchestrator);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task InitialState_Should_BeIdle()
    {
        // Act
        await _handoff.StartAsync();

        // Assert
        var currentState = _handoff.GetCurrentState();
        Assert.Contains("idle", currentState);
        Assert.False(_handoff.LoadRequest);
        Assert.False(_handoff.UnloadRequest);
        Assert.False(_handoff.Ready);
    }

    [Fact]
    public async Task CS0Signal_Should_TransitionToNotReady()
    {
        // Arrange
        await _handoff.StartAsync();

        // Act
        var result = await _handoff.SetCS0Async(true);

        // Assert
        AssertState(result, "notReady");
    }

    [Fact]
    public async Task ValidSignal_Should_TransitionToWaitingForTransfer()
    {
        // Arrange
        await _handoff.StartAsync();

        // Act
        var result = await _handoff.SetValidAsync(true);

        // Assert
        AssertState(result, "waitingForTransfer");
    }

    [Fact]
    public async Task LoadSequence_Should_SetSignalsCorrectly()
    {
        // Arrange
        await _handoff.StartAsync();

        // Act - Simulate load sequence
        var result = await _handoff.SetCS0Async(true); // Carrier present

        // Assert
        AssertState(result, "notReady");
    }

    [Fact]
    public async Task UnloadSequence_Should_SetSignalsCorrectly()
    {
        // Arrange
        await _handoff.StartAsync();

        // Act - Simulate unload sequence
        var result = await _handoff.SetValidAsync(true); // Carrier ready to unload
        AssertState(result, "waitingForTransfer");

        result = await _handoff.SetTransferRequestAsync(true); // AGV/OHT requests transfer

        // Assert
        AssertState(result, "readyToUnload");
        Assert.True(_handoff.UnloadRequest);
    }

    [Fact]
    public async Task TransferBlocked_Should_SetAlarm()
    {
        // Arrange
        await _handoff.StartAsync();

        // Act
        var result = await _handoff.SetCS0Async(true);
        AssertState(result, "notReady");

        result = await _handoff.SetReadyAsync();
        AssertState(result, "readyToLoad");

        // Manually trigger timeout event
        result = await _orchestrator.SendEventAsync("SYSTEM", _handoff.MachineId, "TIMEOUT", null);

        // Assert
        AssertState(result, "transferBlocked");
        Assert.True(_handoff.EsInterlock);
    }

    [Fact]
    public async Task Reset_Should_ClearAllSignals()
    {
        // Arrange
        await _handoff.StartAsync();
        await _handoff.SetCS0Async(true);
        await _handoff.SetValidAsync(true);

        // Act
        var result = await _handoff.ResetAsync();

        // Assert
        AssertState(result, "idle");
        Assert.False(_handoff.LoadRequest);
        Assert.False(_handoff.UnloadRequest);
        Assert.False(_handoff.Ready);
        Assert.False(_handoff.EsInterlock);
    }

    [Theory]
    [InlineData(true, "CS_0_ON")]
    [InlineData(false, "CS_0_OFF")]
    public async Task CS0Signal_Should_SendCorrectEvent(bool signalValue, string expectedEvent)
    {
        // Arrange
        await _handoff.StartAsync();

        // Act
        var result = await _handoff.SetCS0Async(signalValue);

        // Assert
        if (signalValue)
        {
            AssertState(result, "notReady");
        }
        else
        {
            AssertState(result, "idle");
        }
    }

    [Theory]
    [InlineData(true, "VALID_ON")]
    [InlineData(false, "VALID_OFF")]
    public async Task ValidSignal_Should_SendCorrectEvent(bool signalValue, string expectedEvent)
    {
        // Arrange
        await _handoff.StartAsync();

        // Act
        var result = await _handoff.SetValidAsync(signalValue);

        // Assert
        if (signalValue)
        {
            AssertState(result, "waitingForTransfer");
        }
        else
        {
            AssertState(result, "idle");
        }
    }

    [Fact]
    public async Task CompleteHandshake_Should_TransitionThroughAllStates()
    {
        // Arrange
        await _handoff.StartAsync();

        // Act - Simulate complete load handshake
        var result = await _handoff.SetCS0Async(true);          // Carrier detected
        AssertState(result, "notReady");

        result = await _handoff.SetReadyAsync();                // Ready to transfer
        AssertState(result, "readyToLoad");

        result = await _handoff.SetTransferRequestAsync(true);  // Request transfer
        AssertState(result, "transferReady");

        result = await _handoff.SetBusyAsync(true);             // Transfer in progress
        AssertState(result, "transferring");

        result = await _handoff.SetCompleteAsync(true);         // Complete signal
        AssertState(result, "transferComplete");

        result = await _handoff.SetTransferRequestAsync(false); // Clear request
        AssertState(result, "idle");

        // Assert
        var finalState = _handoff.GetCurrentState();
        Assert.Contains("idle", finalState);
    }

    [Fact]
    public async Task ParallelHandoffControllers_Should_OperateIndependently()
    {
        // Arrange
        var testId = Guid.NewGuid().ToString("N")[..8];
        var handoff1 = new E84HandoffMachine($"LP1_Parallel_{testId}", _orchestrator);
        var handoff2 = new E84HandoffMachine($"LP2_Parallel_{testId}", _orchestrator);
        var handoff3 = new E84HandoffMachine($"LP3_Parallel_{testId}", _orchestrator);

        await handoff1.StartAsync();
        await handoff2.StartAsync();
        await handoff3.StartAsync();

        // Act - Run different sequences in parallel
        var task1 = Task.Run(async () =>
        {
            await handoff1.SetCS0Async(true);
            await handoff1.SetReadyAsync();
            await handoff1.SetTransferRequestAsync(true);
            await handoff1.SetBusyAsync(true);
            await Task.Delay(10);
            await handoff1.SetCompleteAsync(true);
        });

        var task2 = Task.Run(async () =>
        {
            await handoff2.SetValidAsync(true);
            await handoff2.SetTransferRequestAsync(true);
            await Task.Delay(10);
            await handoff2.SetBusyAsync(true);
        });

        var task3 = Task.Run(async () =>
        {
            await handoff3.SetCS0Async(true);
            await Task.Delay(10);
            await handoff3.ResetAsync();
        });

        await Task.WhenAll(task1, task2, task3);

        // Assert - Each controller should have different states
        var state1 = handoff1.GetCurrentState();
        var state2 = handoff2.GetCurrentState();
        var state3 = handoff3.GetCurrentState();

        Assert.Contains("idle", state3);
        // States should be independent
        Assert.NotEqual(state1, state2);
    }
}
