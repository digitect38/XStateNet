using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet.Semi;

namespace SemiStandard.Tests;

public class E90SubstrateTrackingTests
{
    private readonly E90SubstrateTracking _tracking;
    
    public E90SubstrateTrackingTests()
    {
        _tracking = new E90SubstrateTracking();
    }
    
    [Fact]
    public void RegisterSubstrate_Should_CreateNewSubstrate()
    {
        // Arrange
        var substrateid = "SUB001";
        var lotId = "LOT001";
        var slotNumber = 1;
        
        // Act
        var substrate = _tracking.RegisterSubstrate(substrateid, lotId, slotNumber);
        
        // Assert
        substrate.Should().NotBeNull();
        substrate.Id.Should().Be(substrateid);
        substrate.LotId.Should().Be(lotId);
        substrate.SlotNumber.Should().Be(slotNumber);
        substrate.GetCurrentState().Should().Contain("WaitingForHost");
    }
    
    [Fact]
    public void UpdateLocation_Should_ChangeSubstrateLocation()
    {
        // Arrange
        var substrateid = "SUB001";
        var substrate = _tracking.RegisterSubstrate(substrateid);
        
        // Act
        _tracking.UpdateLocation(substrateid, "PM1", SubstrateLocationType.ProcessModule);
        
        // Assert
        var history = _tracking.GetHistory(substrateid);
        history.Should().HaveCountGreaterThan(1);
        history.Last().Description.Should().Contain("PM1");
    }
    
    [Fact]
    public void StartProcessing_Should_TransitionToInProcess()
    {
        // Arrange
        var substrateid = "SUB001";
        var substrate = _tracking.RegisterSubstrate(substrateid);
        substrate.StateMachine.Send("ACQUIRE");
        substrate.StateMachine.Send("SELECT_FOR_PROCESS");
        substrate.StateMachine.Send("PLACED_IN_PROCESS_MODULE");
        
        // Act
        var result = _tracking.StartProcessing(substrateid, "RECIPE001");
        
        // Assert
        result.Should().BeTrue();
        substrate.RecipeId.Should().Be("RECIPE001");
        substrate.GetCurrentState().Should().Contain("InProcess");
        substrate.ProcessStartTime.Should().NotBeNull();
    }
    
    [Fact]
    public void CompleteProcessing_Should_TransitionToProcessed()
    {
        // Arrange
        var substrateid = "SUB001";
        var substrate = _tracking.RegisterSubstrate(substrateid);
        substrate.StateMachine.Send("ACQUIRE");
        substrate.StateMachine.Send("SELECT_FOR_PROCESS");
        substrate.StateMachine.Send("PLACED_IN_PROCESS_MODULE");
        _tracking.StartProcessing(substrateid, "RECIPE001");
        
        // Act
        var result = _tracking.CompleteProcessing(substrateid, true);
        
        // Assert
        result.Should().BeTrue();
        substrate.GetCurrentState().Should().Contain("Processed");
        substrate.ProcessEndTime.Should().NotBeNull();
        substrate.ProcessingTime.Should().NotBeNull();
    }
    
    [Fact]
    public void RemoveSubstrate_Should_TransitionToRemoved()
    {
        // Arrange
        var substrateid = "SUB001";
        var substrate = _tracking.RegisterSubstrate(substrateid);
        
        // Act
        var result = _tracking.RemoveSubstrate(substrateid);
        
        // Assert
        result.Should().BeTrue();
        substrate.GetCurrentState().Should().Contain("Removed");
        _tracking.GetSubstrate(substrateid).Should().BeNull();
    }
    
    [Fact]
    public void GetSubstratesByState_Should_ReturnCorrectSubstrates()
    {
        // Arrange
        var sub1 = _tracking.RegisterSubstrate("SUB001");
        var sub2 = _tracking.RegisterSubstrate("SUB002");
        var sub3 = _tracking.RegisterSubstrate("SUB003");
        
        sub2.StateMachine.Send("ACQUIRE");
        sub3.StateMachine.Send("ACQUIRE");
        
        // Act
        var waitingSubstrates = _tracking.GetSubstratesByState("WaitingForHost");
        var inCarrierSubstrates = _tracking.GetSubstratesByState("InCarrier");
        
        // Assert
        waitingSubstrates.Should().HaveCount(1);
        inCarrierSubstrates.Should().HaveCount(2);
    }
    
    [Fact]
    public void GetSubstratesAtLocation_Should_ReturnCorrectSubstrates()
    {
        // Arrange
        _tracking.RegisterSubstrate("SUB001");
        _tracking.RegisterSubstrate("SUB002");
        _tracking.RegisterSubstrate("SUB003");
        
        _tracking.UpdateLocation("SUB001", "PM1", SubstrateLocationType.ProcessModule);
        _tracking.UpdateLocation("SUB002", "PM1", SubstrateLocationType.ProcessModule);
        _tracking.UpdateLocation("SUB003", "LP1", SubstrateLocationType.Carrier);
        
        // Act
        var substratesAtPM1 = _tracking.GetSubstratesAtLocation("PM1");
        var substratesAtLP1 = _tracking.GetSubstratesAtLocation("LP1");
        
        // Assert
        substratesAtPM1.Should().HaveCount(2);
        substratesAtLP1.Should().HaveCount(1);
    }
    
    [Theory]
    [InlineData("ACQUIRE", "InCarrier")]
    [InlineData("PLACED_IN_CARRIER", "InCarrier")]
    public void StateTransitions_Should_WorkCorrectly(string event_, string expectedState)
    {
        // Arrange
        var substrate = _tracking.RegisterSubstrate("SUB001");
        
        // Act
        substrate.StateMachine.Send(event_);
        
        // Assert
        substrate.GetCurrentState().Should().Contain(expectedState);
    }
    
    [Fact]
    public async Task ParallelSubstrateProcessing_Should_HandleConcurrency()
    {
        // Arrange
        var tasks = new Task[10];
        
        // Act
        for (int i = 0; i < 10; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                var substrateid = $"SUB{index:D3}";
                var substrate = _tracking.RegisterSubstrate(substrateid);
                substrate.StateMachine.Send("ACQUIRE");
                substrate.StateMachine.Send("SELECT_FOR_PROCESS");
                substrate.StateMachine.Send("PLACED_IN_PROCESS_MODULE");
                _tracking.StartProcessing(substrateid, "RECIPE001");
                _tracking.CompleteProcessing(substrateid, true);
            });
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var allSubstrates = _tracking.GetSubstratesByState("Processed");
        allSubstrates.Should().HaveCount(10);
    }
}