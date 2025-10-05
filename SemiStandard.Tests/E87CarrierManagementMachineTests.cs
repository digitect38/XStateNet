using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

/// <summary>
/// Unit tests for E87CarrierManagementMachine (SEMI E87 standard)
/// Tests the orchestrator-based carrier and load port management implementation
/// </summary>
public class E87CarrierManagementMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E87CarrierManagementMachine _managementMachine;

    public E87CarrierManagementMachineTests()
    {
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 4,
            EnableMetrics = false
        };

        _orchestrator = new EventBusOrchestrator(config);
        _managementMachine = new E87CarrierManagementMachine("FAB01", _orchestrator);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task E87_Should_Register_Load_Port()
    {
        // Act
        var loadPort = await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);

        // Assert
        Assert.NotNull(loadPort);
        Assert.Equal("LP01", loadPort.Id);
        Assert.Equal("LoadPort1", loadPort.Name);
        Assert.Equal(25, loadPort.Capacity);
        Assert.Contains("Empty", loadPort.GetCurrentState());
    }

    [Fact]
    public async Task E87_Should_Handle_Carrier_Arrival()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);

        // Act
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);

        // Assert
        Assert.NotNull(carrier);
        Assert.Equal("CARRIER001", carrier.Id);
        Assert.Equal("LP01", carrier.LoadPortId);
        Assert.Contains("WaitingForHost", carrier.GetCurrentState());
    }

    [Fact]
    public async Task E87_Should_Complete_Full_Carrier_Lifecycle()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);

        // Act & Assert - Step through complete lifecycle
        // 1. Carrier arrives
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);
        Assert.Contains("WaitingForHost", carrier.GetCurrentState());

        // 2. Host proceeds
        await _managementMachine.StartCarrierProcessingAsync("CARRIER001");
        Assert.Contains("Mapping", carrier.GetCurrentState());

        // 3. Mapping complete
        var slotMap = new Dictionary<int, SlotState>
        {
            [1] = SlotState.Present,
            [2] = SlotState.Present,
            [3] = SlotState.Empty,
            [4] = SlotState.Present
        };
        await _managementMachine.UpdateSlotMapAsync("CARRIER001", slotMap);
        Assert.Contains("MappingVerification", carrier.GetCurrentState());
        Assert.Equal(3, carrier.SubstrateCount);

        // 4. Verify OK (wait for auto-transition after 500ms using event-driven approach)
        var readyStateReached = new TaskCompletionSource<string>();
        carrier.StateTransitioned += (sender, args) =>
        {
            if (args.newState.Contains("ReadyToAccess"))
                readyStateReached.TrySetResult(args.newState);
        };

        var readyTask = await Task.WhenAny(readyStateReached.Task, Task.Delay(1000));
        Assert.True(readyTask == readyStateReached.Task, "Carrier should transition to ReadyToAccess within 1000ms");

        // Small grace period for property update
        await Task.Delay(10);
        Assert.Contains("ReadyToAccess", carrier.GetCurrentState());

        // 5. Start access
        await _managementMachine.StartAccessAsync("CARRIER001");
        Assert.Contains("InAccess", carrier.GetCurrentState());

        // 6. Complete access
        await _managementMachine.CompleteAccessAsync("CARRIER001");
        Assert.Contains("Complete", carrier.GetCurrentState());

        // 7. Remove carrier
        await _managementMachine.CarrierDepartedAsync("CARRIER001");

        // Verify carrier removed from tracking
        var removed = _managementMachine.GetCarrier("CARRIER001");
        Assert.Null(removed);
    }

    [Fact]
    public async Task E87_Should_Track_Multiple_Carriers()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        await _managementMachine.RegisterLoadPortAsync("LP02", "LoadPort2", 25);

        // Act
        var carrier1 = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);
        var carrier2 = await _managementMachine.CarrierArrivedAsync("CARRIER002", "LP02", 25);

        // Assert
        var activeCarriers = _managementMachine.GetActiveCarriers().ToList();
        Assert.Equal(2, activeCarriers.Count);
        Assert.Contains(activeCarriers, c => c.Id == "CARRIER001");
        Assert.Contains(activeCarriers, c => c.Id == "CARRIER002");
    }

    [Fact]
    public async Task E87_Should_Update_Slot_Map()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);
        await _managementMachine.StartCarrierProcessingAsync("CARRIER001");

        // Act
        var slotMap = new Dictionary<int, SlotState>
        {
            [1] = SlotState.Present,
            [2] = SlotState.Present,
            [3] = SlotState.Present,
            [4] = SlotState.Empty,
            [5] = SlotState.Empty
        };
        await _managementMachine.UpdateSlotMapAsync("CARRIER001", slotMap);

        // Assert
        Assert.Equal(3, carrier.SubstrateCount);
        Assert.Equal(SlotState.Present, carrier.SlotMap[1]);
        Assert.Equal(SlotState.Empty, carrier.SlotMap[4]);
        Assert.NotNull(carrier.MappingCompleteTime);
    }

    [Fact]
    public async Task E87_Should_Associate_Substrates_With_Slots()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);
        await _managementMachine.StartCarrierProcessingAsync("CARRIER001");

        var slotMap = new Dictionary<int, SlotState>
        {
            [1] = SlotState.Present,
            [2] = SlotState.Present
        };
        await _managementMachine.UpdateSlotMapAsync("CARRIER001", slotMap);

        // Assert - Check that substrate IDs were created
        Assert.True(carrier.SubstrateIds.ContainsKey(1));
        Assert.True(carrier.SubstrateIds.ContainsKey(2));
        Assert.Equal("CARRIER001_SLOT1", carrier.SubstrateIds[1]);
        Assert.Equal("CARRIER001_SLOT2", carrier.SubstrateIds[2]);
    }

    [Fact]
    public async Task E87_Should_Get_Carrier_At_Port()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);

        // Act
        var carrierAtPort = _managementMachine.GetCarrierAtPort("LP01");

        // Assert
        Assert.NotNull(carrierAtPort);
        Assert.Equal("CARRIER001", carrierAtPort.Id);
    }

    [Fact]
    public async Task E87_Should_Track_Carrier_History()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);

        // Act
        await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);
        await _managementMachine.StartCarrierProcessingAsync("CARRIER001");

        // Assert
        var history = _managementMachine.GetCarrierHistory("CARRIER001");
        Assert.NotNull(history);
        Assert.True(history.Events.Count >= 2); // At least arrival and processing started
        Assert.Equal("CARRIER001", history.CarrierId);
    }

    [Fact]
    public async Task E87_Should_Have_Correct_MachineId()
    {
        // Act
        var machineId = _managementMachine.MachineId;

        // Assert
        Assert.Equal("E87_CARRIER_MGMT_FAB01", machineId);
    }

    [Fact]
    public async Task E87_Should_Have_Unique_Carrier_And_Port_MachineIds()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);

        var loadPort = _managementMachine.GetLoadPort("LP01");

        // Act
        var carrierId = carrier.MachineId;
        var portId = loadPort?.MachineId;

        // Assert
        Assert.StartsWith("E87_CARRIER_CARRIER001_", carrierId);
        Assert.StartsWith("E87_LOADPORT_LP01_", portId);
        Assert.NotEqual(carrierId, portId);
    }

    [Fact]
    public async Task E87_Should_Return_Existing_LoadPort_On_Duplicate_Registration()
    {
        // Arrange
        var loadPort1 = await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);

        // Act - Try to register same port again
        var loadPort2 = await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort2", 30);

        // Assert
        Assert.Equal(loadPort1, loadPort2);
        Assert.Equal("LoadPort1", loadPort1.Name); // Should keep original name
        Assert.Equal(25, loadPort1.Capacity); // Should keep original capacity
    }

    [Fact]
    public async Task E87_Should_Return_Existing_Carrier_On_Duplicate_Arrival()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        var carrier1 = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);

        // Act - Same carrier arrives again
        var carrier2 = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);

        // Assert
        Assert.Equal(carrier1, carrier2);
    }

    [Fact]
    public async Task E87_Should_Handle_Null_Operations_On_NonExistent_Carrier()
    {
        // Act
        var result1 = await _managementMachine.StartCarrierProcessingAsync("NONEXISTENT");
        var result2 = await _managementMachine.StartAccessAsync("NONEXISTENT");
        var result3 = await _managementMachine.CompleteAccessAsync("NONEXISTENT");
        var result4 = await _managementMachine.CarrierDepartedAsync("NONEXISTENT");

        // Assert
        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.False(result4);

        var carrier = _managementMachine.GetCarrier("NONEXISTENT");
        Assert.Null(carrier);
    }

    [Fact]
    public async Task E87_Should_Return_Null_For_Carrier_At_Empty_Port()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);

        // Act
        var carrier = _managementMachine.GetCarrierAtPort("LP01");

        // Assert
        Assert.Null(carrier);
    }

    [Fact]
    public async Task E87_Should_Clear_Port_When_Carrier_Departs()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);

        var loadPort = _managementMachine.GetLoadPort("LP01");
        Assert.Equal("CARRIER001", loadPort?.CurrentCarrierId);

        // Act
        await _managementMachine.CarrierDepartedAsync("CARRIER001");

        // Assert
        Assert.Null(loadPort?.CurrentCarrierId);
        var carrierAtPort = _managementMachine.GetCarrierAtPort("LP01");
        Assert.Null(carrierAtPort);
    }

    [Fact]
    public async Task E87_Should_Get_All_Load_Ports()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        await _managementMachine.RegisterLoadPortAsync("LP02", "LoadPort2", 25);
        await _managementMachine.RegisterLoadPortAsync("LP03", "LoadPort3", 25);

        // Act
        var loadPorts = _managementMachine.GetLoadPorts().ToList();

        // Assert
        Assert.Equal(3, loadPorts.Count);
        Assert.Contains(loadPorts, lp => lp.Id == "LP01");
        Assert.Contains(loadPorts, lp => lp.Id == "LP02");
        Assert.Contains(loadPorts, lp => lp.Id == "LP03");
    }

    [Fact]
    public async Task E87_LoadPort_Should_Reserve_And_Unreserve()
    {
        // Arrange
        var loadPort = await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);

        // Act - Reserve
        var result = await loadPort.ReserveAsync();
        AssertState(result, "Reserved");
        Assert.True(loadPort.IsReserved);

        // Act - Unreserve
        result = await loadPort.UnreserveAsync();
        AssertState(result, "Empty");

        // Assert
        Assert.False(loadPort.IsReserved);
    }

    [Fact]
    public async Task E87_Should_Track_Carrier_Departed_Time()
    {
        // Arrange
        await _managementMachine.RegisterLoadPortAsync("LP01", "LoadPort1", 25);
        var carrier = await _managementMachine.CarrierArrivedAsync("CARRIER001", "LP01", 25);

        Assert.Null(carrier.DepartedTime);

        // Act
        await _managementMachine.CarrierDepartedAsync("CARRIER001");

        // Assert
        Assert.NotNull(carrier.DepartedTime);
    }
}
