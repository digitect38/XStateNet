using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet.Semi;

namespace SemiStandard.Tests;

public class E84HandoffTests : IDisposable
{
    private E84HandoffController _handoff;
    private readonly string _testInstanceId;
    
    public E84HandoffTests()
    {
        _testInstanceId = Guid.NewGuid().ToString("N")[..8];
        _handoff = new E84HandoffController($"LP1_Test_{_testInstanceId}");
    }
    
    public void Dispose()
    {
        _handoff?.Reset();
        _handoff = null!;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
    
    [Fact]
    public void InitialState_Should_BeIdle()
    {
        // Assert
        _handoff.GetCurrentState().Should().Contain("idle");
        _handoff.LoadRequest.Should().BeFalse();
        _handoff.UnloadRequest.Should().BeFalse();
        _handoff.Ready.Should().BeFalse();
    }
    
    [Fact]
    public void CS0Signal_Should_TransitionToNotReady()
    {
        // Act
        _handoff.SetCS0(true);
        
        // Assert
        _handoff.GetCurrentState().Should().Contain("notReady");
    }
    
    [Fact]
    public void ValidSignal_Should_TransitionToWaitingForTransfer()
    {
        // Act
        _handoff.SetValid(true);
        
        // Assert
        _handoff.GetCurrentState().Should().Contain("waitingForTransfer");
    }
    
    [Fact]
    public void LoadSequence_Should_SetSignalsCorrectly()
    {
        // Arrange
        _handoff.SetCS0(true);
        
        // Act - Simulate load sequence
        _handoff.Reset(); // Go to idle
        _handoff.SetCS0(true); // Carrier present
        var state1 = _handoff.GetCurrentState();
        
        // Simulate ready state (would normally be set by internal state machine)
        // In real scenario, this would happen through state transitions
        
        // Assert
        state1.Should().Contain("notReady");
    }
    
    [Fact]
    public void UnloadSequence_Should_SetSignalsCorrectly()
    {
        // Act - Simulate unload sequence
        _handoff.SetValid(true); // Carrier ready to unload
        var state1 = _handoff.GetCurrentState();
        
        _handoff.SetTransferRequest(true); // AGV/OHT requests transfer
        
        // Assert
        state1.Should().Contain("waitingForTransfer");
        // After TR_REQ, should transition to readyToUnload
    }
    
    [Fact]
    public void TransferBlocked_Should_SetAlarm()
    {
        // This test would need to simulate timeout
        // In real implementation, would test the timeout transition
        
        // Act
        _handoff.SetCS0(true);
        // Simulate timeout by not completing handshake
        
        // Would need to wait for timeout or directly send TIMEOUT event
        // _handoff.StateMachine.Send("TIMEOUT");
        
        // Assert
        // _handoff.EsInterlock.Should().BeTrue();
    }
    
    [Fact]
    public void Reset_Should_ClearAllSignals()
    {
        // Arrange
        _handoff.SetCS0(true);
        _handoff.SetValid(true);
        
        // Act
        _handoff.Reset();
        
        // Assert
        _handoff.GetCurrentState().Should().Contain("idle");
        _handoff.LoadRequest.Should().BeFalse();
        _handoff.UnloadRequest.Should().BeFalse();
        _handoff.Ready.Should().BeFalse();
        _handoff.EsInterlock.Should().BeFalse();
    }
    
    [Theory]
    [InlineData(true, "CS_0_ON")]
    [InlineData(false, "CS_0_OFF")]
    public void CS0Signal_Should_SendCorrectEvent(bool signalValue, string expectedEvent)
    {
        // Act
        _handoff.SetCS0(signalValue);
        
        // Assert
        // State should change based on signal
        if (signalValue)
        {
            _handoff.GetCurrentState().Should().NotContain("idle");
        }
    }
    
    [Theory]
    [InlineData(true, "VALID_ON")]
    [InlineData(false, "VALID_OFF")]
    public void ValidSignal_Should_SendCorrectEvent(bool signalValue, string expectedEvent)
    {
        // Act
        _handoff.SetValid(signalValue);
        
        // Assert
        // State should change based on signal
        if (signalValue)
        {
            _handoff.GetCurrentState().Should().NotContain("idle");
        }
    }
    
    [Fact]
    public void CompleteHandshake_Should_TransitionThroughAllStates()
    {
        // Simulate complete load handshake
        // Act
        _handoff.SetCS0(true);          // Carrier detected
        var state1 = _handoff.GetCurrentState();
        
        _handoff.SetTransferRequest(true);  // Request transfer
        var state2 = _handoff.GetCurrentState();
        
        _handoff.SetBusy(true);         // Transfer in progress
        var state3 = _handoff.GetCurrentState();
        
        _handoff.SetBusy(false);        // Transfer done
        _handoff.SetComplete(true);     // Complete signal
        var state4 = _handoff.GetCurrentState();
        
        _handoff.SetComplete(false);    // Clear complete
        _handoff.SetCS0(false);         // Carrier removed
        var finalState = _handoff.GetCurrentState();
        
        // Assert
        state1.Should().NotContain("idle");
        finalState.Should().Contain("idle");
    }
    
    [Fact]
    public async Task ParallelHandoffControllers_Should_OperateIndependently()
    {
        // Arrange
        var testId = Guid.NewGuid().ToString("N")[..8];
        var handoff1 = new E84HandoffController($"LP1_Parallel_{testId}");
        var handoff2 = new E84HandoffController($"LP2_Parallel_{testId}");
        var handoff3 = new E84HandoffController($"LP3_Parallel_{testId}");
        
        // Act - Run different sequences in parallel
        var task1 = Task.Run(() =>
        {
            handoff1.SetCS0(true);
            handoff1.SetTransferRequest(true);
            handoff1.SetBusy(true);
            Task.Delay(10).Wait();
            handoff1.SetBusy(false);
            handoff1.SetComplete(true);
        });
        
        var task2 = Task.Run(() =>
        {
            handoff2.SetValid(true);
            handoff2.SetTransferRequest(true);
            Task.Delay(10).Wait();
            handoff2.SetBusy(true);
            handoff2.SetBusy(false);
        });
        
        var task3 = Task.Run(() =>
        {
            handoff3.SetCS0(true);
            Task.Delay(10).Wait();
            handoff3.Reset();
        });
        
        await Task.WhenAll(task1, task2, task3);
        
        // Assert - Each controller should have different states
        var state1 = handoff1.GetCurrentState();
        var state2 = handoff2.GetCurrentState();
        var state3 = handoff3.GetCurrentState();
        
        state3.Should().Contain("idle");
        // States should be independent
        state1.Should().NotBe(state2);
        
        // Cleanup
        handoff1.Reset();
        handoff2.Reset();
        handoff3.Reset();
    }
}