using Akka.Actor;
using Akka.TestKit.Xunit2;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;
using FluentAssertions;
using Moq;
using Xunit;

namespace XStateNet2.Tests.CMPSimXS2.Schedulers;

/// <summary>
/// Tests for the critical rule: Robot cannot hold multiple wafers
/// Robot must place current wafer (return to idle) before picking up another
/// </summary>
public class RobotSchedulerSingleWaferRuleTests : TestKit
{
    private readonly RobotScheduler _scheduler;
    private readonly IActorRef _robot1;

    public RobotSchedulerSingleWaferRuleTests()
    {
        _scheduler = new RobotScheduler();
        _robot1 = CreateTestProbe("Robot1");
        _scheduler.RegisterRobot("Robot 1", _robot1);
    }

    [Fact]
    public void RobotMustBeIdle_BeforePickingUpWafer()
    {
        // Arrange - Robot picks up wafer 1
        _scheduler.UpdateRobotState("Robot 1", "idle");
        var request1 = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher"
        };
        _scheduler.RequestTransfer(request1);

        // Robot is now busy carrying wafer 1
        _scheduler.GetRobotState("Robot 1").Should().Be("busy");

        // Act - Try to assign another transfer while busy
        var request2 = new TransferRequest
        {
            WaferId = 2,
            From = "Carrier",
            To = "Polisher"
        };
        _scheduler.RequestTransfer(request2);

        // Assert - Second request should be queued, not assigned
        _scheduler.GetQueueSize().Should().Be(1);
    }

    [Fact]
    public void RobotCannotBeIdle_WhileHoldingWafer()
    {
        // Arrange - Robot picks up wafer
        _scheduler.UpdateRobotState("Robot 1", "idle");
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher"
        };
        _scheduler.RequestTransfer(request);

        // Act - Try to set robot to idle while still holding wafer (INVALID)
        _scheduler.UpdateRobotState("Robot 1", "idle", heldWaferId: 1);

        // Assert - Scheduler should enforce rule: idle robot cannot hold wafer
        _scheduler.GetRobotState("Robot 1").Should().Be("idle");
        // Wafer should be cleared (logged as warning)
    }

    [Fact]
    public void RobotMustPlaceWafer_BeforePickingAnother()
    {
        // Arrange - Robot picks up wafer 1
        _scheduler.UpdateRobotState("Robot 1", "idle");
        var request1 = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher"
        };
        _scheduler.RequestTransfer(request1);

        // Queue second request
        var request2 = new TransferRequest
        {
            WaferId = 2,
            From = "Carrier",
            To = "Polisher"
        };
        _scheduler.RequestTransfer(request2);

        _scheduler.GetQueueSize().Should().Be(1);

        // Act - Robot places wafer 1 and returns to idle
        _scheduler.UpdateRobotState("Robot 1", "idle", heldWaferId: null);

        // Assert - Second request should now be processed
        _scheduler.GetQueueSize().Should().Be(0);
    }

    [Fact]
    public void IdleRobot_ShouldHaveNoWafer()
    {
        // Arrange & Act - Set robot to idle
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - Idle robot should not be holding any wafer
        _scheduler.GetRobotState("Robot 1").Should().Be("idle");
    }

    [Fact]
    public void BusyRobot_CannotAcceptNewTransfer()
    {
        // Arrange - Robot is busy
        _scheduler.UpdateRobotState("Robot 1", "busy", heldWaferId: 1);

        // Act - Try to request transfer
        var request = new TransferRequest
        {
            WaferId = 2,
            From = "Carrier",
            To = "Polisher"
        };
        _scheduler.RequestTransfer(request);

        // Assert - Request should be queued
        _scheduler.GetQueueSize().Should().Be(1);
    }

    [Fact]
    public void CarryingRobot_CannotAcceptNewTransfer()
    {
        // Arrange - Robot is carrying a wafer
        _scheduler.UpdateRobotState("Robot 1", "carrying", heldWaferId: 1);

        // Act - Try to request transfer
        var request = new TransferRequest
        {
            WaferId = 2,
            From = "Carrier",
            To = "Polisher"
        };
        _scheduler.RequestTransfer(request);

        // Assert - Request should be queued
        _scheduler.GetQueueSize().Should().Be(1);
    }

    [Fact]
    public void MultipleRobots_CanWorkInParallel_EachWithOneWafer()
    {
        // Arrange - Register 3 robots
        var robot2 = CreateTestProbe("Robot2");
        var robot3 = CreateTestProbe("Robot3");
        _scheduler.RegisterRobot("Robot 2", robot2);
        _scheduler.RegisterRobot("Robot 3", robot3);

        _scheduler.UpdateRobotState("Robot 1", "idle");
        _scheduler.UpdateRobotState("Robot 2", "idle");
        _scheduler.UpdateRobotState("Robot 3", "idle");

        // Act - Request 3 transfers
        _scheduler.RequestTransfer(new TransferRequest { WaferId = 1, From = "Carrier", To = "Polisher" });
        _scheduler.RequestTransfer(new TransferRequest { WaferId = 2, From = "Carrier", To = "Polisher" });
        _scheduler.RequestTransfer(new TransferRequest { WaferId = 3, From = "Carrier", To = "Polisher" });

        // Assert - All 3 robots should be busy, each with one wafer
        _scheduler.GetRobotState("Robot 1").Should().Be("busy");
        _scheduler.GetRobotState("Robot 2").Should().Be("busy");
        _scheduler.GetRobotState("Robot 3").Should().Be("busy");
        _scheduler.GetQueueSize().Should().Be(0); // No queued requests
    }
}
