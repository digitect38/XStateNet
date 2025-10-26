using System.Collections.ObjectModel;
using Akka.Actor;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;
using CMPSimXS2.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace XStateNet2.Tests.CMPSimXS2.Schedulers;

public class WaferJourneySchedulerTests
{
    private readonly WaferJourneyScheduler _scheduler;
    private readonly RobotScheduler _robotScheduler; // Use real scheduler instead of mock
    private readonly ObservableCollection<Wafer> _wafers;
    private readonly Mock<StationViewModel> _mockPolisher;
    private readonly Mock<StationViewModel> _mockCleaner;
    private readonly Mock<StationViewModel> _mockBuffer;
    private readonly Mock<IActorRef> _mockRobot1;
    private readonly Mock<IActorRef> _mockRobot2;
    private readonly Mock<IActorRef> _mockRobot3;
    private readonly List<TransferRequest> _transferRequests = new(); // Track transfer requests

    public WaferJourneySchedulerTests()
    {
        // Use real RobotScheduler with mock robots
        _robotScheduler = new RobotScheduler();
        _mockRobot1 = new Mock<IActorRef>();
        _mockRobot2 = new Mock<IActorRef>();
        _mockRobot3 = new Mock<IActorRef>();

        // Register mock robots
        _robotScheduler.RegisterRobot("Robot 1", _mockRobot1.Object);
        _robotScheduler.RegisterRobot("Robot 2", _mockRobot2.Object);
        _robotScheduler.RegisterRobot("Robot 3", _mockRobot3.Object);

        // Set all robots to idle
        _robotScheduler.UpdateRobotState("Robot 1", "idle");
        _robotScheduler.UpdateRobotState("Robot 2", "idle");
        _robotScheduler.UpdateRobotState("Robot 3", "idle");

        _wafers = new ObservableCollection<Wafer>();

        // Add test wafers
        for (int i = 1; i <= 3; i++)
        {
            _wafers.Add(new Wafer(i)
            {
                CurrentStation = "Carrier",
                ProcessingState = "NotProcessed",
                JourneyStage = "InCarrier"
            });
        }

        _scheduler = new WaferJourneyScheduler(_robotScheduler, _wafers);

        // Create mock stations
        _mockPolisher = new Mock<StationViewModel>("Polisher");
        _mockCleaner = new Mock<StationViewModel>("Cleaner");
        _mockBuffer = new Mock<StationViewModel>("Buffer");

        // Setup default states
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns((int?)null);

        _mockCleaner.Setup(s => s.CurrentState).Returns("idle");
        _mockCleaner.Setup(s => s.CurrentWafer).Returns((int?)null);

        _mockBuffer.Setup(s => s.CurrentState).Returns("empty");
        _mockBuffer.Setup(s => s.CurrentWafer).Returns((int?)null);

        // Register stations
        _scheduler.RegisterStation("Polisher", _mockPolisher.Object);
        _scheduler.RegisterStation("Cleaner", _mockCleaner.Object);
        _scheduler.RegisterStation("Buffer", _mockBuffer.Object);
    }

    [Fact]
    public void RegisterStation_ShouldAllowStationToBeMonitored()
    {
        // Arrange
        var robotScheduler = new RobotScheduler();
        var scheduler = new WaferJourneyScheduler(robotScheduler, _wafers);
        var mockStation = new Mock<StationViewModel>("TestStation");

        // Act & Assert (should not throw)
        scheduler.RegisterStation("TestStation", mockStation.Object);
    }

    [Fact]
    public void ProcessWaferJourneys_WithPolisherIdle_ShouldStartFirstWafer()
    {
        // Arrange
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");

        // Act
        _scheduler.ProcessWaferJourneys();

        // Assert - Robot 1 should receive PICKUP event
        _mockRobot1.Verify(r => r.Tell(It.Is<XStateNet2.Core.Messages.SendEvent>(e =>
            e.Type == "PICKUP"), It.IsAny<IActorRef>()), Times.Once);

        _wafers[0].JourneyStage.Should().Be("ToPolisher");
        _wafers[0].CurrentStation.Should().Be("Robot 1");
    }

    [Fact]
    public void ProcessWaferJourneys_WithPolisherBusy_ShouldNotStartWafer()
    {
        // Arrange
        _mockPolisher.Setup(s => s.CurrentState).Returns("processing");

        // Act
        _scheduler.ProcessWaferJourneys();

        // Assert - No robot should be called
        _mockRobot1.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot2.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot3.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Never);
    }

    [Fact]
    public void ProcessWaferJourneys_PolisherDone_ShouldUnloadAndTransferToCleaner()
    {
        // Arrange
        var wafer = _wafers[0];
        wafer.JourneyStage = "Polishing";
        wafer.CurrentStation = "Polisher";
        wafer.ProcessingState = "NotProcessed";

        _mockPolisher.Setup(s => s.CurrentState).Returns("done");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns(1);
        _mockPolisher.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());

        // Act
        _scheduler.ProcessWaferJourneys();

        // Assert
        wafer.ProcessingState.Should().Be("Polished"); // Font should turn YELLOW
        wafer.JourneyStage.Should().Be("ToCleaner");

        // Robot 2 should receive PICKUP event
        _mockRobot2.Verify(r => r.Tell(It.Is<XStateNet2.Core.Messages.SendEvent>(e =>
            e.Type == "PICKUP"), It.IsAny<IActorRef>()), Times.Once);
    }

    [Fact]
    public void ProcessWaferJourneys_CleanerDone_ShouldUnloadAndTransferToBuffer()
    {
        // Arrange
        var wafer = _wafers[0];
        wafer.JourneyStage = "Cleaning";
        wafer.CurrentStation = "Cleaner";
        wafer.ProcessingState = "Polished";

        _mockCleaner.Setup(s => s.CurrentState).Returns("done");
        _mockCleaner.Setup(s => s.CurrentWafer).Returns(1);
        _mockCleaner.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());

        // Act
        _scheduler.ProcessWaferJourneys();

        // Assert
        wafer.ProcessingState.Should().Be("Cleaned"); // Font should turn WHITE
        wafer.JourneyStage.Should().Be("ToBuffer");

        // Robot 3 should receive PICKUP event
        _mockRobot3.Verify(r => r.Tell(It.Is<XStateNet2.Core.Messages.SendEvent>(e =>
            e.Type == "PICKUP"), It.IsAny<IActorRef>()), Times.Once);
    }

    [Fact]
    public void ProcessWaferJourneys_BufferOccupied_ShouldTransferToCarrier()
    {
        // Arrange
        var wafer = _wafers[0];
        wafer.JourneyStage = "InBuffer";
        wafer.CurrentStation = "Buffer";
        wafer.ProcessingState = "Cleaned";

        _mockBuffer.Setup(s => s.CurrentState).Returns("occupied");
        _mockBuffer.Setup(s => s.CurrentWafer).Returns(1);
        _mockBuffer.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());

        // Act
        _scheduler.ProcessWaferJourneys();

        // Assert
        wafer.JourneyStage.Should().Be("ToCarrier");

        // Robot 1 should receive PICKUP event
        _mockRobot1.Verify(r => r.Tell(It.Is<XStateNet2.Core.Messages.SendEvent>(e =>
            e.Type == "PICKUP"), It.IsAny<IActorRef>()), Times.Once);
    }

    [Fact]
    public void ProcessWaferJourneys_WaferInTransit_ShouldSkipWafer()
    {
        // Arrange
        var wafer = _wafers[0];
        wafer.JourneyStage = "InCarrier";
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");

        // Start first wafer (puts it in transit)
        _scheduler.ProcessWaferJourneys();
        _mockRobot1.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Once);

        // Act - Process again while wafer is in transit
        _scheduler.ProcessWaferJourneys();

        // Assert - Should not request transfer again for same wafer
        _mockRobot1.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Once); // Still once
    }

    [Fact]
    public void ProcessWaferJourneys_CompletedWafer_ShouldSkipWafer()
    {
        // Arrange - Mark all wafers as completed (leave them in various stages, not InCarrier)
        _wafers[0].IsCompleted = true;
        _wafers[0].JourneyStage = "Polishing"; // Completed wafers should not be in InCarrier stage
        _wafers[1].IsCompleted = true;
        _wafers[1].JourneyStage = "Cleaning";
        _wafers[2].IsCompleted = true;
        _wafers[2].JourneyStage = "InBuffer";
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");

        // Act
        _scheduler.ProcessWaferJourneys();

        // Assert - No robots should be called
        _mockRobot1.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot2.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Never);
        _mockRobot3.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Never);
    }

    [Fact]
    public void Reset_ShouldClearInternalState()
    {
        // Arrange
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");
        _scheduler.ProcessWaferJourneys(); // Start a wafer
        _mockRobot1.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Once);

        // Act
        _scheduler.Reset();
        _robotScheduler.UpdateRobotState("Robot 1", "idle"); // Set Robot 1 back to idle
        _wafers[0].JourneyStage = "InCarrier"; // Reset wafer back to carrier
        _wafers[0].CurrentStation = "Carrier";

        // Assert - Should be able to start wafer again after reset
        _scheduler.ProcessWaferJourneys();
        _mockRobot1.Verify(r => r.Tell(It.IsAny<object>(), It.IsAny<IActorRef>()), Times.Exactly(2)); // Once before reset, once after
    }

    [Fact]
    public void ProcessWaferJourneys_MultipleWafers_ShouldProcessSequentially()
    {
        // Arrange
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");

        // Act - Process first wafer
        _scheduler.ProcessWaferJourneys();

        // Assert - Only Robot 1 should be called once for wafer 1
        var robot1CallsBefore = _mockRobot1.Invocations.Count;
        robot1CallsBefore.Should().Be(1);

        // Arrange - Simulate first wafer moving to Polisher
        _wafers[0].JourneyStage = "Polishing";
        _wafers[0].CurrentStation = "Polisher";
        _mockPolisher.Setup(s => s.CurrentState).Returns("processing");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns(1);

        // Act - Process again (Polisher busy, should not start wafer 2)
        _scheduler.ProcessWaferJourneys();

        // Assert - No new robot calls
        _mockRobot1.Invocations.Count.Should().Be(robot1CallsBefore);

        // Arrange - Simulate Polisher becoming idle again
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns((int?)null);
        _robotScheduler.UpdateRobotState("Robot 1", "idle"); // Robot 1 completes first transfer

        // Act - Process again (Polisher idle, should start wafer 2)
        _scheduler.ProcessWaferJourneys();

        // Assert - Robot 1 called again for wafer 2
        _mockRobot1.Invocations.Count.Should().Be(robot1CallsBefore + 1);
    }

    [Fact]
    public void ProcessWaferJourneys_AllStages_ShouldTrackJourneyCorrectly()
    {
        // Test complete journey progression for one wafer
        var wafer = _wafers[0];

        // Stage 1: InCarrier → ToPolisher
        wafer.JourneyStage = "InCarrier";
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");
        _scheduler.ProcessWaferJourneys();
        wafer.JourneyStage.Should().Be("ToPolisher");

        // Stage 2: ToPolisher → Polishing (simulated by transfer completion)
        _scheduler.Reset(); // Clear transit tracking
        wafer.JourneyStage = "Polishing";
        wafer.CurrentStation = "Polisher";
        _robotScheduler.UpdateRobotState("Robot 1", "idle"); // Robot 1 completes transfer

        // Stage 3: Polishing → ToCleaner
        _mockPolisher.Setup(s => s.CurrentState).Returns("done");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns(1);
        _mockPolisher.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _scheduler.ProcessWaferJourneys();
        wafer.JourneyStage.Should().Be("ToCleaner");
        wafer.ProcessingState.Should().Be("Polished");

        // Stage 4: ToCleaner → Cleaning (simulated by transfer completion)
        _scheduler.Reset(); // Clear transit tracking
        wafer.JourneyStage = "Cleaning";
        wafer.CurrentStation = "Cleaner";
        _robotScheduler.UpdateRobotState("Robot 2", "idle"); // Robot 2 completes transfer

        // Stage 5: Cleaning → ToBuffer
        _mockCleaner.Setup(s => s.CurrentState).Returns("done");
        _mockCleaner.Setup(s => s.CurrentWafer).Returns(1);
        _mockCleaner.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _scheduler.ProcessWaferJourneys();
        wafer.JourneyStage.Should().Be("ToBuffer");
        wafer.ProcessingState.Should().Be("Cleaned");

        // Stage 6: ToBuffer → InBuffer (simulated by transfer completion)
        _scheduler.Reset(); // Clear transit tracking
        wafer.JourneyStage = "InBuffer";
        wafer.CurrentStation = "Buffer";
        _robotScheduler.UpdateRobotState("Robot 3", "idle"); // Robot 3 completes transfer

        // Stage 7: InBuffer → ToCarrier
        _mockBuffer.Setup(s => s.CurrentState).Returns("occupied");
        _mockBuffer.Setup(s => s.CurrentWafer).Returns(1);
        _mockBuffer.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _robotScheduler.UpdateRobotState("Robot 1", "idle"); // Robot 1 available for return journey
        _scheduler.ProcessWaferJourneys();
        wafer.JourneyStage.Should().Be("ToCarrier");

        // Stage 8: ToCarrier → Complete (simulated by transfer completion callback)
        // This would be handled by OnWaferCompleted callback in actual implementation
    }
}
