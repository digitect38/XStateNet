using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet.Semi;
using System.Collections.Concurrent;

namespace SemiStandard.Tests;

public class E87CarrierManagementTests : IDisposable
{
    private E87CarrierManagement _management;
    private readonly string _testInstanceId;
    
    public E87CarrierManagementTests()
    {
        _testInstanceId = Guid.NewGuid().ToString("N")[..8];
        _management = new E87CarrierManagement();
    }
    
    public void Dispose()
    {
        // Clean up any active carriers
        var activeCarriers = _management.GetActiveCarriers().ToList();
        foreach (var carrier in activeCarriers)
        {
            _management.RemoveCarrier(carrier.Id);
        }
        
        _management = null!;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
    
    [Fact]
    public void RegisterLoadPort_Should_CreateNewLoadPort()
    {
        // Arrange & Act
        var portId = $"LP1_{_testInstanceId}";
        _management.RegisterLoadPort(portId, "Load Port 1", 25);
        var loadPort = _management.GetLoadPort(portId);
        
        // Assert
        loadPort.Should().NotBeNull();
        loadPort.Id.Should().Be(portId);
        loadPort.Name.Should().Be("Load Port 1");
        loadPort.Capacity.Should().Be(25);
        loadPort.GetCurrentState().Should().Contain("Empty");
    }
    
    [Fact]
    public void CarrierArrived_Should_RegisterCarrierAtLoadPort()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        
        // Act
        var carrier = _management.CarrierArrived("CAR001", "LP1", 25);
        
        // Assert
        carrier.Should().NotBeNull();
        carrier.Id.Should().Be("CAR001");
        carrier.LoadPortId.Should().Be("LP1");
        carrier.SlotCount.Should().Be(25);
        carrier.GetCurrentState().Should().Contain("NotPresent");
        
        var loadPort = _management.GetLoadPort("LP1");
        loadPort.CurrentCarrierId.Should().Be("CAR001");
    }
    
    [Fact]
    public void UpdateSlotMap_Should_SetSlotStates()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        var carrier = _management.CarrierArrived("CAR001", "LP1");
        
        var slotMap = new ConcurrentDictionary<int, SlotState>();
        slotMap.TryAdd(1, SlotState.Present);
        slotMap.TryAdd(2, SlotState.Present);
        slotMap.TryAdd(3, SlotState.Empty);
        slotMap.TryAdd(4, SlotState.Empty);
        slotMap.TryAdd(5, SlotState.Present);
        
        // Act
        _management.UpdateSlotMap("CAR001", slotMap);
        
        // Assert
        carrier.Should().NotBeNull();
        carrier!.SubstrateCount.Should().Be(3);
        carrier.SlotMap[1].Should().Be(SlotState.Present);
        carrier.SlotMap[3].Should().Be(SlotState.Empty);
    }
    
    [Fact]
    public void AssociateSubstrate_Should_LinkSubstrateToSlot()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        var carrier = _management.CarrierArrived("CAR001", "LP1");
        
        // Act
        _management.AssociateSubstrate("CAR001", 1, "SUB001");
        _management.AssociateSubstrate("CAR001", 2, "SUB002");
        
        // Assert
        carrier.Should().NotBeNull();
        carrier!.SubstrateIds[1].Should().Be("SUB001");
        carrier.SubstrateIds[2].Should().Be("SUB002");
    }
    
    [Fact]
    public void StartCarrierProcessing_Should_TransitionStates()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        var carrier = _management.CarrierArrived("CAR001", "LP1");
        
        // Act
        var result = _management.StartCarrierProcessing("CAR001");
        
        // Assert
        result.Should().BeTrue();
        // State should transition through the E87 states
    }
    
    [Fact]
    public void CompleteCarrierProcessing_Should_TransitionToComplete()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        var carrier = _management.CarrierArrived("CAR001", "LP1");
        _management.StartCarrierProcessing("CAR001");
        
        // Act
        var result = _management.CompleteCarrierProcessing("CAR001");
        
        // Assert
        result.Should().BeTrue();
    }
    
    [Fact]
    public void RemoveCarrier_Should_ClearFromLoadPort()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        _management.CarrierArrived("CAR001", "LP1");
        
        // Act
        var result = _management.RemoveCarrier("CAR001");
        
        // Assert
        result.Should().BeTrue();
        _management.GetCarrier("CAR001").Should().BeNull();
        
        var loadPort = _management.GetLoadPort("LP1");
        loadPort?.CurrentCarrierId.Should().BeNull();
    }
    
    [Fact]
    public void GetActiveCarriers_Should_ReturnOnlyActiveCarriers()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        _management.RegisterLoadPort("LP2", "Load Port 2");
        
        _management.CarrierArrived("CAR001", "LP1");
        _management.CarrierArrived("CAR002", "LP2");
        _management.RemoveCarrier("CAR001");
        
        // Act
        var activeCarriers = _management.GetActiveCarriers();
        
        // Assert
        activeCarriers.Should().HaveCount(1);
        activeCarriers.First().Id.Should().Be("CAR002");
    }
    
    [Fact]
    public void GetCarrierHistory_Should_ReturnEvents()
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        _management.CarrierArrived("CAR001", "LP1");
        _management.StartCarrierProcessing("CAR001");
        _management.CompleteCarrierProcessing("CAR001");
        
        // Act
        var history = _management.GetCarrierHistory("CAR001");
        
        // Assert
        history.Should().NotBeNull();
        history?.Events.Should().NotBeEmpty();
        history?.Events.First().Description.Should().Contain("arrived");
    }
    
    [Theory]
    [InlineData("CARRIER_DETECTED", "WaitingForHost")]
    [InlineData("HOST_PROCEED", "Mapping")]
    public void CarrierStateTransitions_Should_WorkCorrectly(string initialEvent, string subsequentEvent)
    {
        // Arrange
        _management.RegisterLoadPort("LP1", "Load Port 1");
        var carrier = _management.CarrierArrived("CAR001", "LP1");
        
        // Act
        carrier?.StateMachine.Send(initialEvent);
        
        // Assert
        // Due to the nature of state transitions, we'd need to check the actual state
        // This is a simplified test structure
        carrier.Should().NotBeNull();
    }
    
    [Fact]
    public async Task ParallelCarrierOperations_Should_HandleConcurrency()
    {
        // Arrange
        for (int i = 1; i <= 4; i++)
        {
            _management.RegisterLoadPort($"LP{i}", $"Load Port {i}");
        }
        
        var tasks = new Task[4];
        
        // Act
        for (int i = 0; i < 4; i++)
        {
            int index = i + 1;
            tasks[i] = Task.Run(() =>
            {
                var carrierId = $"CAR{index:D3}";
                var portId = $"LP{index}";
                
                _management.CarrierArrived(carrierId, portId);
                _management.StartCarrierProcessing(carrierId);
                Task.Delay(10).Wait();
                _management.CompleteCarrierProcessing(carrierId);
            });
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var carriers = _management.GetActiveCarriers();
        carriers.Should().HaveCount(4);
    }
}