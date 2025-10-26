using Akka.Actor;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;
using FluentAssertions;
using Moq;
using Xunit;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests.CMPSimXS2.Schedulers;

public class RobotSchedulerTests
{
    private readonly RobotScheduler _scheduler;
    private readonly Mock<IActorRef> _mockRobot1;
    private readonly Mock<IActorRef> _mockRobot2;
    private readonly Mock<IActorRef> _mockRobot3;

    public RobotSchedulerTests()
    {
        _scheduler = new RobotScheduler();
        _mockRobot1 = new Mock<IActorRef>();
        _mockRobot2 = new Mock<IActorRef>();
        _mockRobot3 = new Mock<IActorRef>();

        _scheduler.RegisterRobot("Robot 1", _mockRobot1.Object);
        _scheduler.RegisterRobot("Robot 2", _mockRobot2.Object);
        _scheduler.RegisterRobot("Robot 3", _mockRobot3.Object);

        // Set all robots to idle initially
        _scheduler.UpdateRobotState("Robot 1", "idle");
        _scheduler.UpdateRobotState("Robot 2", "idle");
        _scheduler.UpdateRobotState("Robot 3", "idle");
    }

    [Fact]
    public void RegisterRobot_ShouldAllowRobotToBeUsed()
    {
        // Arrange
        var scheduler = new RobotScheduler();
        var mockRobot = new Mock<IActorRef>();

        // Act
        scheduler.RegisterRobot("TestRobot", mockRobot.Object);
        scheduler.UpdateRobotState("TestRobot", "idle");

        // Assert
        scheduler.GetRobotState("TestRobot").Should().Be("idle");
    }

    [Fact]
    public void UpdateRobotState_ShouldUpdateState()
    {
        // Act
        _scheduler.UpdateRobotState("Robot 1", "busy");

        // Assert
        _scheduler.GetRobotState("Robot 1").Should().Be("busy");
    }

    [Fact]
    public void RequestTransfer_WithIdleRobot_ShouldAssignImmediately()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);
        _scheduler.GetQueueSize().Should().Be(0);
    }

    [Fact]
    public void RequestTransfer_WithBusyRobot_ShouldUseFallbackRobot()
    {
        // Arrange
        _scheduler.UpdateRobotState("Robot 1", "busy");
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert - Should use fallback robot (Robot 2 or 3) instead of queuing
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
        // Either Robot 2 or Robot 3 should be used as fallback
        var totalCalls = _mockRobot2.Invocations.Count + _mockRobot3.Invocations.Count;
        totalCalls.Should().Be(1, "a fallback robot should be used when preferred robot is busy");
        _scheduler.GetQueueSize().Should().Be(0, "request should not be queued when fallback robot is available");
    }

    [Fact]
    public void RequestTransfer_CarrierToPolisher_ShouldSelectRobot1()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);
        _mockRobot2.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot3.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
    }

    [Fact]
    public void RequestTransfer_PolisherToCleaner_ShouldSelectRobot2()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Polisher",
            To = "Cleaner"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot2.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);
        _mockRobot3.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
    }

    [Fact]
    public void RequestTransfer_CleanerToBuffer_ShouldSelectRobot3()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Cleaner",
            To = "Buffer"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot2.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot3.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);
    }

    [Fact]
    public void RequestTransfer_BufferToCarrier_ShouldSelectRobot1()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Buffer",
            To = "Carrier"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);
        _mockRobot2.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot3.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
    }

    [Fact]
    public void UpdateRobotState_ToIdle_ShouldProcessPendingRequests()
    {
        // Arrange - Make ALL robots busy so request must be queued
        _scheduler.UpdateRobotState("Robot 1", "busy");
        _scheduler.UpdateRobotState("Robot 2", "busy");
        _scheduler.UpdateRobotState("Robot 3", "busy");

        var request = new TransferRequest
        {
            WaferId = 1,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };
        _scheduler.RequestTransfer(request);
        _scheduler.GetQueueSize().Should().Be(1, "request should be queued when all robots are busy");

        // Act - Make Robot 1 idle
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - Pending request should be processed
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);
        _scheduler.GetQueueSize().Should().Be(0);
    }

    [Fact]
    public void RequestTransfer_WithInvalidRequest_ShouldNotQueue()
    {
        // Arrange
        var request = new TransferRequest
        {
            WaferId = -1, // Invalid
            From = "Carrier",
            To = "Polisher"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        _scheduler.GetQueueSize().Should().Be(0);
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Never);
    }

    [Fact]
    public void RequestTransfer_MultipleRequests_ShouldProcessInFIFOOrder()
    {
        // Arrange - Make ALL robots busy to force queuing
        _scheduler.UpdateRobotState("Robot 1", "busy");
        _scheduler.UpdateRobotState("Robot 2", "busy");
        _scheduler.UpdateRobotState("Robot 3", "busy");

        var request1 = new TransferRequest { WaferId = 1, From = "Carrier", To = "Polisher", PreferredRobotId = "Robot 1" };
        var request2 = new TransferRequest { WaferId = 2, From = "Carrier", To = "Polisher", PreferredRobotId = "Robot 1" };
        var request3 = new TransferRequest { WaferId = 3, From = "Carrier", To = "Polisher", PreferredRobotId = "Robot 1" };

        // Act - Request with all robots busy
        _scheduler.RequestTransfer(request1);
        _scheduler.RequestTransfer(request2);
        _scheduler.RequestTransfer(request3);

        // Assert - All queued because no robots available
        _scheduler.GetQueueSize().Should().Be(3, "all requests should be queued when all robots are busy");

        // Act - Robot 1 becomes idle, should process first request
        _scheduler.UpdateRobotState("Robot 1", "idle");

        // Assert - First request processed, 2 remaining
        _scheduler.GetQueueSize().Should().Be(2, "first request should be dequeued and processed");
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);
    }

    [Fact]
    public void GetRobotState_UnregisteredRobot_ShouldReturnUnknown()
    {
        // Act
        var state = _scheduler.GetRobotState("NonExistentRobot");

        // Assert
        state.Should().Be("unknown");
    }

    [Fact]
    public void RequestTransfer_ShouldSendPickupEventWithCorrectData()
    {
        // Arrange
        SendEvent? capturedEvent = null;
        _mockRobot1.Setup(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()))
            .Callback<object, IActorRef>((msg, sender) =>
            {
                if (msg is SendEvent evt)
                    capturedEvent = evt;
            });

        var request = new TransferRequest
        {
            WaferId = 5,
            From = "Carrier",
            To = "Polisher",
            PreferredRobotId = "Robot 1"
        };

        // Act
        _scheduler.RequestTransfer(request);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Type.Should().Be("PICKUP");
        var data = capturedEvent.Data as Dictionary<string, object>;
        data.Should().NotBeNull();
        data!["wafer"].Should().Be(5);
        data["from"].Should().Be("Carrier");
        data["to"].Should().Be("Polisher");
    }
}
