using System.Collections.ObjectModel;
using Akka.Actor;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;
using CMPSimXS2.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests.CMPSimXS2.Integration;

/// <summary>
/// Integration tests for the complete 8-step wafer journey
/// Tests the full chain: Carrier → R1 → Polisher → R2 → Cleaner → R3 → Buffer → R1 → Carrier
/// </summary>
public class WaferJourneyIntegrationTests
{
    private readonly RobotScheduler _robotScheduler;
    private readonly WaferJourneyScheduler _journeyScheduler;
    private readonly ObservableCollection<Wafer> _wafers;
    private readonly Mock<IActorRef> _mockRobot1;
    private readonly Mock<IActorRef> _mockRobot2;
    private readonly Mock<IActorRef> _mockRobot3;
    private readonly Mock<StationViewModel> _mockPolisher;
    private readonly Mock<StationViewModel> _mockCleaner;
    private readonly Mock<StationViewModel> _mockBuffer;

    public WaferJourneyIntegrationTests()
    {
        // Setup Robot Scheduler with 3 robots
        _robotScheduler = new RobotScheduler();
        _mockRobot1 = new Mock<IActorRef>();
        _mockRobot2 = new Mock<IActorRef>();
        _mockRobot3 = new Mock<IActorRef>();

        _robotScheduler.RegisterRobot("Robot 1", _mockRobot1.Object);
        _robotScheduler.RegisterRobot("Robot 2", _mockRobot2.Object);
        _robotScheduler.RegisterRobot("Robot 3", _mockRobot3.Object);

        _robotScheduler.UpdateRobotState("Robot 1", "idle");
        _robotScheduler.UpdateRobotState("Robot 2", "idle");
        _robotScheduler.UpdateRobotState("Robot 3", "idle");

        // Setup Wafers
        _wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1)
            {
                CurrentStation = "Carrier",
                ProcessingState = "NotProcessed",
                JourneyStage = "InCarrier"
            }
        };

        // Setup Journey Scheduler
        _journeyScheduler = new WaferJourneyScheduler(_robotScheduler, _wafers);

        // Setup Stations
        _mockPolisher = new Mock<StationViewModel>("Polisher");
        _mockCleaner = new Mock<StationViewModel>("Cleaner");
        _mockBuffer = new Mock<StationViewModel>("Buffer");

        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");
        _mockCleaner.Setup(s => s.CurrentState).Returns("idle");
        _mockBuffer.Setup(s => s.CurrentState).Returns("empty");

        _journeyScheduler.RegisterStation("Polisher", _mockPolisher.Object);
        _journeyScheduler.RegisterStation("Cleaner", _mockCleaner.Object);
        _journeyScheduler.RegisterStation("Buffer", _mockBuffer.Object);
    }

    [Fact]
    public void CompleteJourney_SingleWafer_ShouldGoThroughAll8Steps()
    {
        var wafer = _wafers[0];

        // ===== STEP 1-2: Carrier → R1 → Polisher =====
        _journeyScheduler.ProcessWaferJourneys();

        // Assert: Transfer requested to Robot 1
        _mockRobot1.Verify(r => r.Tell(It.Is<SendEvent>(e =>
            e.Type == "PICKUP" &&
            ((Dictionary<string, object>)e.Data!)["wafer"].Equals(1) &&
            ((Dictionary<string, object>)e.Data!)["from"].Equals("Carrier") &&
            ((Dictionary<string, object>)e.Data!)["to"].Equals("Polisher")),
            It.IsAny<IActorRef>()), Times.Once);

        wafer.JourneyStage.Should().Be("ToPolisher");
        wafer.CurrentStation.Should().Be("Robot 1");

        // Simulate transfer completion
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Polisher", "Polishing");
        _robotScheduler.UpdateRobotState("Robot 1", "idle"); // Robot 1 completes first transfer

        // Simulate Polisher processing
        _mockPolisher.Setup(s => s.CurrentState).Returns("processing");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns(1);
        _mockPolisher.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());

        // ===== STEP 3-4: Polisher done → R2 → Cleaner =====
        _mockPolisher.Setup(s => s.CurrentState).Returns("done");
        _robotScheduler.UpdateRobotState("Robot 2", "idle");

        _journeyScheduler.ProcessWaferJourneys();

        // Assert: Wafer marked as Polished (YELLOW font)
        wafer.ProcessingState.Should().Be("Polished");
        wafer.JourneyStage.Should().Be("ToCleaner");

        // Assert: Transfer requested to Robot 2
        _mockRobot2.Verify(r => r.Tell(It.Is<SendEvent>(e =>
            e.Type == "PICKUP" &&
            ((Dictionary<string, object>)e.Data!)["wafer"].Equals(1) &&
            ((Dictionary<string, object>)e.Data!)["from"].Equals("Polisher") &&
            ((Dictionary<string, object>)e.Data!)["to"].Equals("Cleaner")),
            It.IsAny<IActorRef>()), Times.Once);

        // Simulate transfer completion
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Cleaner", "Cleaning");

        // Simulate Cleaner processing
        _mockCleaner.Setup(s => s.CurrentState).Returns("cleaning");
        _mockCleaner.Setup(s => s.CurrentWafer).Returns(1);
        _mockCleaner.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());

        // ===== STEP 5-6: Cleaner done → R3 → Buffer =====
        _mockCleaner.Setup(s => s.CurrentState).Returns("done");
        _robotScheduler.UpdateRobotState("Robot 3", "idle");

        _journeyScheduler.ProcessWaferJourneys();

        // Assert: Wafer marked as Cleaned (WHITE font)
        wafer.ProcessingState.Should().Be("Cleaned");
        wafer.JourneyStage.Should().Be("ToBuffer");

        // Assert: Transfer requested to Robot 3
        _mockRobot3.Verify(r => r.Tell(It.Is<SendEvent>(e =>
            e.Type == "PICKUP" &&
            ((Dictionary<string, object>)e.Data!)["wafer"].Equals(1) &&
            ((Dictionary<string, object>)e.Data!)["from"].Equals("Cleaner") &&
            ((Dictionary<string, object>)e.Data!)["to"].Equals("Buffer")),
            It.IsAny<IActorRef>()), Times.Once);

        // Simulate transfer completion
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Buffer", "InBuffer");

        // Simulate Buffer storage
        _mockBuffer.Setup(s => s.CurrentState).Returns("occupied");
        _mockBuffer.Setup(s => s.CurrentWafer).Returns(1);
        _mockBuffer.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());

        // ===== STEP 7-8: Buffer → R1 → Carrier =====
        _robotScheduler.UpdateRobotState("Robot 1", "idle");

        _journeyScheduler.ProcessWaferJourneys();

        // Assert: Transfer requested to Robot 1 (return to Carrier)
        _mockRobot1.Verify(r => r.Tell(It.Is<SendEvent>(e =>
            e.Type == "PICKUP" &&
            ((Dictionary<string, object>)e.Data!)["wafer"].Equals(1) &&
            ((Dictionary<string, object>)e.Data!)["from"].Equals("Buffer") &&
            ((Dictionary<string, object>)e.Data!)["to"].Equals("Carrier")),
            It.IsAny<IActorRef>()), Times.Once);

        wafer.JourneyStage.Should().Be("ToCarrier");

        // Simulate transfer completion (marks wafer as completed)
        SimulateWaferCompletion(wafer);

        // Assert: Wafer journey complete
        wafer.IsCompleted.Should().BeTrue();
        wafer.CurrentStation.Should().Be("Carrier");
        wafer.ProcessingState.Should().Be("Cleaned");
    }

    [Fact]
    public void CompleteJourney_ThreeWafers_ShouldProcessSequentially()
    {
        // Add 2 more wafers
        _wafers.Add(new Wafer(2) { CurrentStation = "Carrier", ProcessingState = "NotProcessed", JourneyStage = "InCarrier" });
        _wafers.Add(new Wafer(3) { CurrentStation = "Carrier", ProcessingState = "NotProcessed", JourneyStage = "InCarrier" });

        // Process first wafer to Polisher
        _journeyScheduler.ProcessWaferJourneys();
        _wafers[0].JourneyStage.Should().Be("ToPolisher");

        // Simulate first wafer moving to Polishing stage
        SimulateTransferComplete(_wafers[0], "Polisher", "Polishing");
        _mockPolisher.Setup(s => s.CurrentState).Returns("processing");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns(1);

        // Try to process again - second wafer should NOT start (Polisher busy)
        _journeyScheduler.ProcessWaferJourneys();
        _wafers[1].JourneyStage.Should().Be("InCarrier"); // Still in carrier

        // Simulate Polisher becoming idle
        _mockPolisher.Setup(s => s.CurrentState).Returns("idle");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns((int?)null);

        // Now second wafer should start
        _journeyScheduler.ProcessWaferJourneys();
        _wafers[1].JourneyStage.Should().Be("ToPolisher");

        // Third wafer still waiting
        _wafers[2].JourneyStage.Should().Be("InCarrier");
    }

    [Fact]
    public void CompleteJourney_FontColorProgression_ShouldChangeCorrectly()
    {
        var wafer = _wafers[0];

        // Initial: NotProcessed (BLACK font)
        wafer.ProcessingState.Should().Be("NotProcessed");
        wafer.TextColor.Should().Be(System.Windows.Media.Brushes.Black);

        // After Polisher
        _journeyScheduler.ProcessWaferJourneys();
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Polisher", "Polishing");
        _robotScheduler.UpdateRobotState("Robot 1", "idle"); // Robot 1 completes transfer
        _mockPolisher.Setup(s => s.CurrentState).Returns("done");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns(1);
        _mockPolisher.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _robotScheduler.UpdateRobotState("Robot 2", "idle"); // Robot 2 ready for next transfer

        _journeyScheduler.ProcessWaferJourneys();

        // After Polisher: Polished, waiting for cleaner (GREEN font)
        wafer.ProcessingState.Should().Be("Polished");
        wafer.JourneyStage.Should().Be("ToCleaner");
        wafer.TextColor.Should().Be(System.Windows.Media.Brushes.LimeGreen);

        // After Cleaner
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Cleaner", "Cleaning");
        _robotScheduler.UpdateRobotState("Robot 2", "idle"); // Robot 2 completes transfer
        _mockCleaner.Setup(s => s.CurrentState).Returns("done");
        _mockCleaner.Setup(s => s.CurrentWafer).Returns(1);
        _mockCleaner.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _robotScheduler.UpdateRobotState("Robot 3", "idle"); // Robot 3 ready for next transfer

        _journeyScheduler.ProcessWaferJourneys();

        // After Cleaner: Cleaned, ready to return (WHITE font)
        wafer.ProcessingState.Should().Be("Cleaned");
        wafer.JourneyStage.Should().Be("ToBuffer");
        wafer.TextColor.Should().Be(System.Windows.Media.Brushes.White);
    }

    [Fact]
    public void CompleteJourney_RobotSelection_ShouldUseCorrectRobots()
    {
        var wafer = _wafers[0];

        // Step 1-2: Carrier → R1 → Polisher
        _journeyScheduler.ProcessWaferJourneys();
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);

        // Step 3-4: Polisher → R2 → Cleaner
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Polisher", "Polishing");
        _robotScheduler.UpdateRobotState("Robot 1", "idle"); // Robot 1 completes transfer
        _mockPolisher.Setup(s => s.CurrentState).Returns("done");
        _mockPolisher.Setup(s => s.CurrentWafer).Returns(1);
        _mockPolisher.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _robotScheduler.UpdateRobotState("Robot 2", "idle");

        _journeyScheduler.ProcessWaferJourneys();
        _mockRobot2.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);

        // Step 5-6: Cleaner → R3 → Buffer
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Cleaner", "Cleaning");
        _robotScheduler.UpdateRobotState("Robot 2", "idle"); // Robot 2 completes transfer
        _mockCleaner.Setup(s => s.CurrentState).Returns("done");
        _mockCleaner.Setup(s => s.CurrentWafer).Returns(1);
        _mockCleaner.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _robotScheduler.UpdateRobotState("Robot 3", "idle");

        _journeyScheduler.ProcessWaferJourneys();
        _mockRobot3.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Once);

        // Step 7-8: Buffer → R1 → Carrier
        _journeyScheduler.Reset(); // Clear transit tracking
        SimulateTransferComplete(wafer, "Buffer", "InBuffer");
        _mockBuffer.Setup(s => s.CurrentState).Returns("occupied");
        _mockBuffer.Setup(s => s.CurrentWafer).Returns(1);
        _mockBuffer.Setup(s => s.StateMachine).Returns(Mock.Of<IActorRef>());
        _robotScheduler.UpdateRobotState("Robot 1", "idle");

        _journeyScheduler.ProcessWaferJourneys();
        _mockRobot1.Verify(r => r.Tell(It.IsAny<SendEvent>(), It.IsAny<IActorRef>()), Times.Exactly(2)); // Carrier→Polisher + Buffer→Carrier

        // Verify correct robot usage
        _mockRobot1.Invocations.Count.Should().Be(2); // R1 used twice
        _mockRobot2.Invocations.Count.Should().Be(1); // R2 used once
        _mockRobot3.Invocations.Count.Should().Be(1); // R3 used once
    }

    /// <summary>
    /// Helper method to simulate transfer completion
    /// </summary>
    private void SimulateTransferComplete(Wafer wafer, string arrivedAt, string nextStage)
    {
        wafer.CurrentStation = arrivedAt;
        wafer.JourneyStage = nextStage;
    }

    /// <summary>
    /// Helper method to simulate wafer completion
    /// </summary>
    private void SimulateWaferCompletion(Wafer wafer)
    {
        wafer.CurrentStation = "Carrier";
        wafer.JourneyStage = "InCarrier";
        wafer.IsCompleted = true;
    }
}
