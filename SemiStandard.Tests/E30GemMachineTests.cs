using XStateNet.Orchestration;
using XStateNet.Semi.Standards;
using Xunit;
using static SemiStandard.Tests.StateMachineTestHelpers;

namespace SemiStandard.Tests;

/// <summary>
/// Unit tests for E30GemMachine (SEMI E30 standard - GEM)
/// Tests the orchestrator-based implementation
/// </summary>
public class E30GemMachineTests : IDisposable
{
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E30GemMachine _gemMachine;

    public E30GemMachineTests()
    {
        var config = new OrchestratorConfig
        {
            EnableLogging = true,
            PoolSize = 4,
            EnableMetrics = false
        };

        _orchestrator = new EventBusOrchestrator(config);
        _gemMachine = new E30GemMachine("EQ001", _orchestrator);
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task E30GemMachine_Should_Start_In_Disabled_State()
    {
        // Act
        var machineId = await _gemMachine.StartAsync();

        // Assert
        var currentState = _gemMachine.GetCurrentState();
        Assert.Contains("disabled", currentState);
    }

    [Fact]
    public async Task E30GemMachine_Should_Transition_To_WaitDelay_On_Enable()
    {
        // Arrange
        await _gemMachine.StartAsync();

        // Act
        var result = await _gemMachine.EnableAsync();

        // Assert
        AssertState(result, "waitDelay");
    }

    [Fact]
    public async Task E30GemMachine_Should_Transition_To_WaitCRA_Immediately()
    {
        // Arrange
        await _gemMachine.StartAsync();

        // Act
        var result = await _gemMachine.EnableImmediateAsync();

        // Assert
        AssertState(result, "waitCRA");
    }

    [Fact]
    public async Task E30GemMachine_Should_Transition_Through_Communication_Establishment()
    {
        // Arrange
        await _gemMachine.StartAsync();

        // Act - Complete communication establishment sequence
        // 1. Enable immediately (skip T1 delay)
        var result1 = await _gemMachine.EnableImmediateAsync();
        AssertState(result1, "waitCRA");

        // 2. Receive S1F13 from host
        var result2 = await _gemMachine.ReceiveS1F13Async();
        AssertState(result2, "waitCRFromHost");

        // 3. Send S1F14 response
        var result3 = await _gemMachine.SendS1F14Async();

        // Assert - Should be in communicating state
        AssertState(result3, "communicating");
        Assert.Contains("notSelected", result3.NewState);
    }

    [Fact]
    public async Task E30GemMachine_Should_Transition_To_CommFail_On_Timeout()
    {
        // Arrange
        await _gemMachine.StartAsync();

        // Act - Enable but don't send S1F13 (will timeout after 10s)
        var result1 = await _gemMachine.EnableImmediateAsync();
        AssertState(result1, "waitCRA");

        // Manually trigger timeout
        var result2 = await _orchestrator.SendEventAsync("SYSTEM", _gemMachine.MachineId, "TIMEOUT", null);

        // Assert
        AssertState(result2, "commFail");
    }

    [Fact]
    public async Task E30GemMachine_Should_Select_Equipment()
    {
        // Arrange - Establish communication first
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        await _gemMachine.ReceiveS1F13Async();
        await _gemMachine.SendS1F14Async();

        // Act - Select equipment
        var result = await _gemMachine.SelectAsync();

        // Assert
        AssertState(result, "selected");
        Assert.Contains("hostOffline", result.NewState);
    }

    [Fact]
    public async Task E30GemMachine_Should_Deselect_Equipment()
    {
        // Arrange - Select equipment first
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        await _gemMachine.ReceiveS1F13Async();
        await _gemMachine.SendS1F14Async();
        await _gemMachine.SelectAsync();

        // Act - Deselect equipment
        var result = await _gemMachine.DeselectAsync();

        // Assert
        AssertState(result, "communicating");
        Assert.Contains("notSelected", result.NewState);
    }

    [Fact]
    public async Task E30GemMachine_Should_Go_Online_Remote()
    {
        // Arrange - Select equipment first
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        await _gemMachine.ReceiveS1F13Async();
        await _gemMachine.SendS1F14Async();
        await _gemMachine.SelectAsync();

        // Act - Go online remote (host control)
        var result = await _gemMachine.GoOnlineRemoteAsync();

        // Assert
        AssertState(result, "remote");
    }

    [Fact]
    public async Task E30GemMachine_Should_Go_Online_Local()
    {
        // Arrange - Select equipment first
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        await _gemMachine.ReceiveS1F13Async();
        await _gemMachine.SendS1F14Async();
        await _gemMachine.SelectAsync();

        // Act - Go online local (operator control)
        var result = await _gemMachine.GoOnlineLocalAsync();

        // Assert
        AssertState(result, "local");
    }

    [Fact]
    public async Task E30GemMachine_Should_Switch_Between_Local_And_Remote()
    {
        // Arrange - Go online local first
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        await _gemMachine.ReceiveS1F13Async();
        await _gemMachine.SendS1F14Async();
        await _gemMachine.SelectAsync();
        var result1 = await _gemMachine.GoOnlineLocalAsync();
        AssertState(result1, "local");

        // Act - Switch to remote
        var result2 = await _gemMachine.SwitchToRemoteAsync();
        AssertState(result2, "remote");

        // Act - Switch back to local
        var result3 = await _gemMachine.SwitchToLocalAsync();

        // Assert
        AssertState(result3, "local");
    }

    [Fact]
    public async Task E30GemMachine_Should_Go_Offline_From_Online()
    {
        // Arrange - Go online remote first
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        await _gemMachine.ReceiveS1F13Async();
        await _gemMachine.SendS1F14Async();
        await _gemMachine.SelectAsync();
        var result1 = await _gemMachine.GoOnlineRemoteAsync();
        AssertState(result1, "remote");

        // Act - Go offline
        var result = await _gemMachine.GoOfflineAsync();

        // Assert
        AssertState(result, "equipmentOffline");
    }

    [Fact]
    public async Task E30GemMachine_Should_Disable_From_Communicating()
    {
        // Arrange - Establish communication
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        await _gemMachine.ReceiveS1F13Async();
        var result1 = await _gemMachine.SendS1F14Async();
        AssertState(result1, "communicating");

        // Act - Disable
        var result = await _gemMachine.DisableAsync();

        // Assert
        AssertState(result, "disabled");
    }

    [Fact]
    public async Task E30GemMachine_Should_Have_Correct_MachineId()
    {
        // Arrange & Act
        var machineId = _gemMachine.MachineId;

        // Assert
        Assert.StartsWith("E30_GEM_EQ001_", machineId);
    }

    [Fact]
    public async Task E30GemMachine_Should_Recover_From_CommFail()
    {
        // Arrange - Force communication failure
        await _gemMachine.StartAsync();
        await _gemMachine.EnableImmediateAsync();
        var result1 = await _orchestrator.SendEventAsync("SYSTEM", _gemMachine.MachineId, "TIMEOUT", null);
        AssertState(result1, "commFail");

        // Act - Re-enable communication
        var result = await _gemMachine.EnableAsync();

        // Assert - Should be in waitDelay or have progressed beyond
        // The state should no longer be commFail
        Assert.DoesNotContain("commFail", result.NewState);
    }
}
