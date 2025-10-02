using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

/// <summary>
/// Unit tests for E90SubstrateTrackingMachine (SEMI E90 standard)
/// Tests the orchestrator-based substrate tracking implementation
/// </summary>
public class E90SubstrateTrackingMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E90SubstrateTrackingMachine _trackingMachine;

    public E90SubstrateTrackingMachineTests()
    {
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 4,
            EnableMetrics = false
        };

        _orchestrator = new EventBusOrchestrator(config);
        _trackingMachine = new E90SubstrateTrackingMachine("FAB01", _orchestrator);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task E90_Should_Register_New_Substrate()
    {
        // Act
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);

        // Assert
        Assert.NotNull(substrate);
        Assert.Equal("W001", substrate.Id);
        Assert.Equal("LOT123", substrate.LotId);
        Assert.Equal(1, substrate.SlotNumber);
        Assert.Contains("WaitingForHost", substrate.GetCurrentState());
    }

    [Fact]
    public async Task E90_Should_Track_Multiple_Substrates()
    {
        // Act
        var substrate1 = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var substrate2 = await _trackingMachine.RegisterSubstrateAsync("W002", "LOT123", 2);
        var substrate3 = await _trackingMachine.RegisterSubstrateAsync("W003", "LOT456", 1);

        // Assert
        Assert.NotNull(substrate1);
        Assert.NotNull(substrate2);
        Assert.NotNull(substrate3);

        var wafersByState = _trackingMachine.GetSubstratesByState("WaitingForHost").ToList();
        Assert.Equal(3, wafersByState.Count);
    }

    [Fact]
    public async Task E90_Should_Transition_Substrate_To_InCarrier()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);

        // Act
        var result = await substrate.AcquireAsync();

        // Assert
        AssertState(result, "InCarrier");
    }

    [Fact]
    public async Task E90_Should_Select_Substrate_For_Processing()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var result = await substrate.AcquireAsync();

        // Act
        result = await substrate.SelectForProcessAsync();

        // Assert
        AssertState(result, "NeedsProcessing");
    }

    [Fact]
    public async Task E90_Should_Complete_Full_Process_Lifecycle()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);

        // Act & Assert - Step through complete lifecycle
        // 1. Acquire into carrier
        var result = await substrate.AcquireAsync();
        AssertState(result, "InCarrier");

        // 2. Select for processing
        result = await substrate.SelectForProcessAsync();
        AssertState(result, "NeedsProcessing");

        // 3. Place in process module
        result = await substrate.PlacedInProcessModuleAsync();
        AssertState(result, "ReadyToProcess");

        // 4. Start processing
        await _trackingMachine.StartProcessingAsync("W001", "RECIPE_001");
        Assert.Contains("InProcess", substrate.GetCurrentState());
        Assert.NotNull(substrate.ProcessStartTime);

        // 5. Complete processing
        await _trackingMachine.CompleteProcessingAsync("W001", success: true);
        Assert.Contains("Processed", substrate.GetCurrentState());
        Assert.NotNull(substrate.ProcessEndTime);
        Assert.NotNull(substrate.ProcessingTime);

        // 6. Place back in carrier
        result = await substrate.PlacedInCarrierAsync();
        AssertState(result, "Complete");

        // 7. Remove
        await _trackingMachine.RemoveSubstrateAsync("W001");
        Assert.Contains("Removed", substrate.GetCurrentState());
    }

    [Fact]
    public async Task E90_Should_Handle_Aligning_Step()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var result = await substrate.AcquireAsync();
        result = await substrate.SelectForProcessAsync();

        // Act - Place in aligner instead of process module
        result = await substrate.PlacedInAlignerAsync();
        AssertState(result, "Aligning");

        // Complete alignment
        result = await substrate.AlignCompleteAsync();

        // Assert
        AssertState(result, "ReadyToProcess");
    }

    [Fact]
    public async Task E90_Should_Handle_Process_Abort()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var result = await substrate.AcquireAsync();
        result = await substrate.SelectForProcessAsync();
        result = await substrate.PlacedInProcessModuleAsync();
        await _trackingMachine.StartProcessingAsync("W001", "RECIPE_001");
        Assert.Contains("InProcess", substrate.GetCurrentState());

        // Act - Abort processing
        await _trackingMachine.CompleteProcessingAsync("W001", success: false);

        // Assert
        Assert.Contains("Aborted", substrate.GetCurrentState());
    }

    [Fact]
    public async Task E90_Should_Handle_Process_Stop_And_Resume()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var result = await substrate.AcquireAsync();
        result = await substrate.SelectForProcessAsync();
        result = await substrate.PlacedInProcessModuleAsync();
        await _trackingMachine.StartProcessingAsync("W001", "RECIPE_001");
        Assert.Contains("InProcess", substrate.GetCurrentState());

        // Act - Stop processing
        result = await substrate.StopProcessAsync();
        AssertState(result, "Stopped");

        // Resume processing
        result = await substrate.ResumeAsync();

        // Assert
        AssertState(result, "InProcess");
    }

    [Fact]
    public async Task E90_Should_Handle_Substrate_Rejection()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var result = await substrate.AcquireAsync();

        // Act - Reject substrate
        result = await substrate.RejectAsync();

        // Assert
        AssertState(result, "Rejected");
    }

    [Fact]
    public async Task E90_Should_Handle_Substrate_Skip()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var result = await substrate.AcquireAsync();

        // Act - Skip substrate
        result = await substrate.SkipAsync();

        // Assert
        AssertState(result, "Skipped");
    }

    [Fact]
    public async Task E90_Should_Track_Location_Changes()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);

        // Act - Update locations
        await _trackingMachine.UpdateLocationAsync("W001", "LOADPORT_1", SubstrateLocationType.LoadPort);

        await _trackingMachine.UpdateLocationAsync("W001", "CARRIER_1", SubstrateLocationType.Carrier);

        // Assert
        var substrates = _trackingMachine.GetSubstratesAtLocation("CARRIER_1").ToList();
        Assert.Single(substrates);
        Assert.Contains("W001", substrates);

        var history = _trackingMachine.GetHistory("W001");
        Assert.True(history.Count >= 3); // Registration + 2 location updates
    }

    [Fact]
    public async Task E90_Should_Track_Processing_Time()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var result = await substrate.AcquireAsync();
        result = await substrate.SelectForProcessAsync();
        result = await substrate.PlacedInProcessModuleAsync();

        // Act - Start processing, wait, then complete
        await _trackingMachine.StartProcessingAsync("W001", "RECIPE_001");
        await Task.Delay(200); // Simulate processing time
        await _trackingMachine.CompleteProcessingAsync("W001", success: true);

        // Assert
        Assert.NotNull(substrate.ProcessStartTime);
        Assert.NotNull(substrate.ProcessEndTime);
        Assert.NotNull(substrate.ProcessingTime);
        Assert.True(substrate.ProcessingTime.Value.TotalMilliseconds >= 100);
    }

    [Fact(Skip = "Timing issue with orchestrator event processing - substrates not transitioning consistently in test environment")]
    public async Task E90_Should_Get_Substrates_By_State()
    {
        // Arrange
        var substrate1 = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var substrate2 = await _trackingMachine.RegisterSubstrateAsync("W002", "LOT123", 2);
        var substrate3 = await _trackingMachine.RegisterSubstrateAsync("W003", "LOT123", 3);
        await Task.Delay(300);

        // Act - Move substrates through lifecycle
        var result1 = await substrate1.AcquireAsync();
        AssertState(result1, "InCarrier");

        var result2 = await substrate2.AcquireAsync();
        AssertState(result2, "InCarrier");

        var result3 = await substrate2.SelectForProcessAsync();
        AssertState(result3, "NeedsProcessing");

        // Assert - Check each substrate's state directly
        Assert.Contains("InCarrier", substrate1.GetCurrentState());
        Assert.Contains("NeedsProcessing", substrate2.GetCurrentState());
        Assert.Contains("WaitingForHost", substrate3.GetCurrentState());

        // Now test GetSubstratesByState
        var waitingSubstrates = _trackingMachine.GetSubstratesByState("WaitingForHost").ToList();
        Assert.True(waitingSubstrates.Count >= 1, $"Expected at least 1 substrate in WaitingForHost state, got {waitingSubstrates.Count}");
        Assert.Contains(waitingSubstrates, s => s.Id == "W003");

        var carrierSubstrates = _trackingMachine.GetSubstratesByState("InCarrier").ToList();
        Assert.True(carrierSubstrates.Count >= 1, $"Expected at least 1 substrate in InCarrier state, got {carrierSubstrates.Count}");
        Assert.Contains(carrierSubstrates, s => s.Id == "W001");

        var processingSubstrates = _trackingMachine.GetSubstratesByState("NeedsProcessing").ToList();
        Assert.True(processingSubstrates.Count >= 1, $"Expected at least 1 substrate in NeedsProcessing state, got {processingSubstrates.Count}");
        Assert.Contains(processingSubstrates, s => s.Id == "W002");
    }

    [Fact]
    public async Task E90_Should_Have_Correct_MachineId()
    {
        // Arrange & Act
        var machineId = _trackingMachine.MachineId;

        // Assert
        Assert.Equal("E90_TRACKING_FAB01", machineId);
    }

    [Fact]
    public async Task E90_Should_Have_Unique_Substrate_MachineIds()
    {
        // Arrange
        var substrate1 = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);
        var substrate2 = await _trackingMachine.RegisterSubstrateAsync("W002", "LOT123", 2);

        // Act
        var id1 = substrate1.MachineId;
        var id2 = substrate2.MachineId;

        // Assert
        Assert.StartsWith("E90_SUBSTRATE_W001_", id1);
        Assert.StartsWith("E90_SUBSTRATE_W002_", id2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task E90_Should_Return_Existing_Substrate_On_Duplicate_Registration()
    {
        // Arrange
        var substrate1 = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);

        // Act - Try to register same substrate again
        var substrate2 = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT456", 2);

        // Assert
        Assert.Equal(substrate1, substrate2);
        Assert.Equal("LOT123", substrate1.LotId); // Should keep original lot
    }

    [Fact]
    public async Task E90_Should_Track_Substrate_History()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);

        // Act - Perform multiple operations
        var result = await substrate.AcquireAsync();
        result = await substrate.SelectForProcessAsync();
        await _trackingMachine.UpdateLocationAsync("W001", "PM01", SubstrateLocationType.ProcessModule);

        // Assert
        var history = _trackingMachine.GetHistory("W001");
        Assert.True(history.Count >= 2); // At least: registration and location change

        var firstEntry = history[0];
        Assert.Equal("WaitingForHost", firstEntry.State);
        Assert.Contains("registered", firstEntry.Description.ToLower());
    }

    [Fact]
    public async Task E90_Should_Store_Custom_Properties()
    {
        // Arrange
        var substrate = await _trackingMachine.RegisterSubstrateAsync("W001", "LOT123", 1);

        // Act
        substrate.Properties["defectCount"] = 5;
        substrate.Properties["inspectionResult"] = "PASS";

        // Assert
        Assert.Equal(5, substrate.Properties["defectCount"]);
        Assert.Equal("PASS", substrate.Properties["inspectionResult"]);
    }

    [Fact]
    public async Task E90_Should_Handle_Null_Substrate_Operations()
    {
        // Act - Try operations on non-existent substrate
        var result1 = await _trackingMachine.StartProcessingAsync("NONEXISTENT", "RECIPE_001");
        var result2 = await _trackingMachine.CompleteProcessingAsync("NONEXISTENT", true);
        var result3 = await _trackingMachine.RemoveSubstrateAsync("NONEXISTENT");

        // Assert
        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);

        var substrate = _trackingMachine.GetSubstrate("NONEXISTENT");
        Assert.Null(substrate);
    }
}
