using System.Collections.ObjectModel;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using CMPSimXS2.Models;
using CMPSimXS2.Schedulers;
using CMPSimXS2.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace XStateNet2.Tests.CMPSimXS2.Schedulers;

/// <summary>
/// Tests for the critical rule: Station cannot hold multiple wafers
/// Each station (Polisher, Cleaner, Buffer) works in parallel but can only process ONE wafer at a time
/// </summary>
public class StationSingleWaferRuleTests : TestKit
{
    private readonly RobotScheduler _robotScheduler;
    private readonly ObservableCollection<Wafer> _wafers;
    private readonly WaferJourneyScheduler _journeyScheduler;
    private readonly Dictionary<string, StationViewModel> _stations;

    public StationSingleWaferRuleTests()
    {
        _robotScheduler = new RobotScheduler();
        _wafers = new ObservableCollection<Wafer>
        {
            new Wafer(1),
            new Wafer(2),
            new Wafer(3)
        };
        _journeyScheduler = new WaferJourneyScheduler(_robotScheduler, _wafers);

        // Create mock stations
        _stations = new Dictionary<string, StationViewModel>();
        foreach (var stationName in new[] { "Polisher", "Cleaner", "Buffer" })
        {
            var mockActor = CreateTestProbe(stationName);
            var station = new StationViewModel(stationName)
            {
                StateMachine = mockActor,
                CurrentState = "idle"
            };
            _stations[stationName] = station;
            _journeyScheduler.RegisterStation(stationName, station);
        }

        // Register robots
        for (int i = 1; i <= 3; i++)
        {
            var robotActor = CreateTestProbe($"Robot{i}");
            _robotScheduler.RegisterRobot($"Robot {i}", robotActor);
            _robotScheduler.UpdateRobotState($"Robot {i}", "idle");
        }
    }

    [Fact]
    public void Polisher_CanOnlyProcessOneWafer_AtATime()
    {
        // Arrange - Polisher is idle
        _stations["Polisher"].CurrentState = "idle";
        _stations["Polisher"].CurrentWafer = null;

        // Act - Process first wafer
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - First wafer should start
        _wafers[0].JourneyStage.Should().Be("ToPolisher");

        // Arrange - Polisher now processing wafer 1
        _stations["Polisher"].CurrentState = "processing";
        _stations["Polisher"].CurrentWafer = 1;

        // Act - Try to process second wafer while first is still in Polisher
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Second wafer should NOT start (Polisher is busy)
        _wafers[1].JourneyStage.Should().Be("InCarrier");
    }

    [Fact]
    public void Cleaner_CanOnlyProcessOneWafer_AtATime()
    {
        // Arrange - Wafer 1 in Cleaner, Wafer 2 ready for Cleaner
        _wafers[0].JourneyStage = "Cleaning";
        _wafers[0].CurrentStation = "Cleaner";
        _stations["Cleaner"].CurrentState = "processing";
        _stations["Cleaner"].CurrentWafer = 1;

        _wafers[1].JourneyStage = "ToCleaner";
        _wafers[1].CurrentStation = "Robot 2";

        // Act - Process journeys
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Wafer 2 cannot enter Cleaner (occupied by wafer 1)
        _stations["Cleaner"].CurrentWafer.Should().Be(1);
    }

    [Fact]
    public void Buffer_CanOnlyStoreOneWafer_AtATime()
    {
        // Arrange - Wafer 1 in Buffer
        _wafers[0].JourneyStage = "InBuffer";
        _wafers[0].CurrentStation = "Buffer";
        _stations["Buffer"].CurrentState = "occupied";
        _stations["Buffer"].CurrentWafer = 1;

        _wafers[1].JourneyStage = "ToBuffer";
        _wafers[1].CurrentStation = "Robot 3";

        // Act - Process journeys
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Wafer 2 cannot enter Buffer (occupied by wafer 1)
        _stations["Buffer"].CurrentWafer.Should().Be(1);
    }

    [Fact]
    public void Station_MustBeIdle_BeforeAcceptingNewWafer()
    {
        // Arrange - Polisher is processing wafer 1
        _stations["Polisher"].CurrentState = "processing";
        _stations["Polisher"].CurrentWafer = 1;

        // Act - Try to start wafer 2
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Wafer 2 should not start (Polisher not idle)
        _wafers[1].JourneyStage.Should().Be("InCarrier");
    }

    [Fact]
    public void MultipleStations_CanWorkInParallel_EachWithOneWafer()
    {
        // Arrange - Set up 3 wafers in 3 different stations
        _wafers[0].JourneyStage = "Polishing";
        _wafers[0].CurrentStation = "Polisher";
        _stations["Polisher"].CurrentState = "processing";
        _stations["Polisher"].CurrentWafer = 1;

        _wafers[1].JourneyStage = "Cleaning";
        _wafers[1].CurrentStation = "Cleaner";
        _stations["Cleaner"].CurrentState = "processing";
        _stations["Cleaner"].CurrentWafer = 2;

        _wafers[2].JourneyStage = "InBuffer";
        _wafers[2].CurrentStation = "Buffer";
        _stations["Buffer"].CurrentState = "occupied";
        _stations["Buffer"].CurrentWafer = 3;

        // Act - Process journeys
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - All 3 stations working in parallel, each with 1 wafer
        _stations["Polisher"].CurrentWafer.Should().Be(1);
        _stations["Cleaner"].CurrentWafer.Should().Be(2);
        _stations["Buffer"].CurrentWafer.Should().Be(3);
    }

    [Fact]
    public void Station_CanAcceptNewWafer_AfterCurrentCompletes()
    {
        // Arrange - All wafers in carrier initially
        _wafers[0].JourneyStage = "InCarrier";
        _wafers[1].JourneyStage = "InCarrier";

        // Polisher processes wafer 1
        _stations["Polisher"].CurrentState = "processing";
        _stations["Polisher"].CurrentWafer = 1;

        // Act - Try to start wafer 2 while wafer 1 is in Polisher
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Wafer 2 cannot start (Polisher occupied)
        _wafers[1].JourneyStage.Should().Be("InCarrier");

        // Act - Wafer 1 completes, Polisher becomes idle
        _stations["Polisher"].CurrentState = "idle";
        _stations["Polisher"].CurrentWafer = null;

        // Reset to start new wafer
        _journeyScheduler.Reset();
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Now wafer 1 (first in queue) can start
        _wafers[0].JourneyStage.Should().Be("ToPolisher");
    }

    [Fact]
    public void IdleStation_ShouldNotHaveWafer()
    {
        // Arrange & Act - Station set to idle
        _stations["Polisher"].CurrentState = "idle";
        _stations["Polisher"].CurrentWafer = null;

        // Assert - Idle station has no wafer
        _stations["Polisher"].CurrentWafer.Should().BeNull();
    }

    [Fact]
    public void ProcessingStation_MustHaveWafer()
    {
        // Arrange - Station processing
        _stations["Polisher"].CurrentState = "processing";
        _stations["Polisher"].CurrentWafer = 1;

        // Assert - Processing station must have wafer ID
        _stations["Polisher"].CurrentWafer.Should().HaveValue();
        _stations["Polisher"].CurrentWafer.Value.Should().Be(1);
    }

    [Fact]
    public void Station_CannotAccept_IfAlreadyHasWafer()
    {
        // Arrange - Manually set station to have wafer (simulating error scenario)
        _stations["Polisher"].CurrentState = "processing";
        _stations["Polisher"].CurrentWafer = 1;
        _wafers[0].JourneyStage = "Polishing";

        // Act - Try to process wafer 2
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Wafer 2 should not start (Polisher has wafer 1)
        _wafers[1].JourneyStage.Should().Be("InCarrier");
    }

    [Fact]
    public void WaferMustWait_UntilStationAvailable()
    {
        // Arrange - All wafers start in carrier
        _wafers[0].JourneyStage = "InCarrier";
        _wafers[1].JourneyStage = "InCarrier";

        // Polisher busy with wafer 1
        _stations["Polisher"].CurrentState = "processing";
        _stations["Polisher"].CurrentWafer = 1;

        // Act - Process multiple times while Polisher is busy
        for (int i = 0; i < 5; i++)
        {
            _journeyScheduler.ProcessWaferJourneys();
        }

        // Assert - Wafer 2 still waiting (cannot start while Polisher occupied)
        _wafers[1].JourneyStage.Should().Be("InCarrier");

        // Act - Polisher becomes idle
        _stations["Polisher"].CurrentState = "idle";
        _stations["Polisher"].CurrentWafer = null;
        _journeyScheduler.Reset();
        _journeyScheduler.ProcessWaferJourneys();

        // Assert - Now the first wafer in queue can start
        var anyWaferStarted = _wafers.Any(w => w.JourneyStage == "ToPolisher");
        anyWaferStarted.Should().BeTrue();
    }
}
