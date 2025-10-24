using Xunit;
using Xunit.Abstractions;
using CMPSimulator.StateMachines;
using XStateNet.Orchestration;
using Newtonsoft.Json.Linq;

namespace CMPSimulator.Tests;

/// <summary>
/// Tests for ParallelSchedulerMachine using XState Parallel states
/// Validates concurrent scheduling behavior and robot coordination
/// </summary>
public class ParallelSchedulerTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _logs = new();

    public ParallelSchedulerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void Log(string message)
    {
        _logs.Add(message);
        _output.WriteLine(message);
    }

    [Fact]
    public async Task ParallelScheduler_StartsInParallelMode()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);

        // Act
        var initialState = await scheduler.StartAsync();

        // Assert
        Assert.NotNull(initialState);
        _output.WriteLine($"Initial state: {initialState}");

        // In parallel states, current state shows all active parallel regions
        // Should contain something like: r1Manager.monitoring, r2Manager.monitoring, r3Manager.monitoring, globalCoordinator.coordinating
        Assert.Contains("monitoring", initialState);
        Assert.Contains("coordinating", initialState);
    }

    [Fact]
    public async Task ParallelScheduler_R1Manager_HandlesBufferToLoadPort()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        // Simulate buffer has completed wafer
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "buffer",
            ["state"] = "occupied",
            ["wafer"] = 1
        });

        // Simulate R1 is idle
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R1",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        // Give time for event processing
        await Task.Delay(100);

        // Assert - Check logs for R1Manager handling B→L transfer
        var r1Logs = _logs.Where(l => l.Contains("R1Manager") || l.Contains("B→L")).ToList();
        Assert.NotEmpty(r1Logs);
        _output.WriteLine($"R1 logs count: {r1Logs.Count}");

        // Should have commanded R1 to transfer from buffer to LoadPort
        Assert.Contains(_logs, l => l.Contains("B→L") && l.Contains("R1"));
    }

    [Fact]
    public async Task ParallelScheduler_R2Manager_HandlesPolisherToCleaner()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        // Simulate polisher done
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "polisher",
            ["state"] = "COMPLETE",
            ["wafer"] = 2
        });

        // Simulate cleaner empty
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "cleaner",
            ["state"] = "empty",
            ["wafer"] = (int?)null
        });

        // Simulate R2 is idle
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R2",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await Task.Delay(100);

        // Assert
        var r2Logs = _logs.Where(l => l.Contains("R2Manager") || l.Contains("P→C")).ToList();
        Assert.NotEmpty(r2Logs);
        Assert.Contains(_logs, l => l.Contains("P→C") && l.Contains("R2"));
    }

    [Fact]
    public async Task ParallelScheduler_R3Manager_HandlesCleanerToBuffer()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        // Simulate cleaner done
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "cleaner",
            ["state"] = "done",
            ["wafer"] = 3
        });

        // Simulate R3 is idle
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R3",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await Task.Delay(100);

        // Assert
        var r3Logs = _logs.Where(l => l.Contains("R3Manager") || l.Contains("C→B")).ToList();
        Assert.NotEmpty(r3Logs);
        Assert.Contains(_logs, l => l.Contains("C→B") && l.Contains("R3"));
    }

    [Fact]
    public async Task ParallelScheduler_R1Manager_ProactivelyPicksFromLoadPort()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        // Simulate R1 is idle (wafers are already in queue from initialization)
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R1",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await Task.Delay(100);

        // Assert - R1 should proactively pick from LoadPort
        var pickLogs = _logs.Where(l => l.Contains("L→HOLD") || l.Contains("LoadPort")).ToList();
        Assert.NotEmpty(pickLogs);
        Assert.Contains(_logs, l => l.Contains("L→HOLD") && l.Contains("R1"));
    }

    [Fact]
    public async Task ParallelScheduler_MultipleRobots_WorkConcurrently()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        // Setup: All robots idle, stations in different states
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "polisher",
            ["state"] = "COMPLETE",
            ["wafer"] = 1
        });

        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "cleaner",
            ["state"] = "done",
            ["wafer"] = 2
        });

        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R1",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R2",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R3",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await Task.Delay(200);

        // Assert - Multiple robots should receive commands
        var r1Commands = _logs.Count(l => l.Contains("R1") && l.Contains("Commanding"));
        var r2Commands = _logs.Count(l => l.Contains("R2") && l.Contains("Commanding"));
        var r3Commands = _logs.Count(l => l.Contains("R3") && l.Contains("Commanding"));

        _output.WriteLine($"R1 commands: {r1Commands}, R2 commands: {r2Commands}, R3 commands: {r3Commands}");

        // At least 2 robots should have received commands
        var totalCommands = r1Commands + r2Commands + r3Commands;
        Assert.True(totalCommands >= 2, "Expected at least 2 robots to receive commands concurrently");
    }

    [Fact]
    public async Task ParallelScheduler_R1Manager_PrioritizesBufferReturn()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        // Setup: Buffer occupied (should be highest priority for R1)
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "buffer",
            ["state"] = "occupied",
            ["wafer"] = 5
        });

        // R1 becomes idle
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R1",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await Task.Delay(100);

        // Assert - B→L should execute before L→HOLD
        var firstR1Command = _logs.FirstOrDefault(l => l.Contains("Commanding R1"));
        Assert.NotNull(firstR1Command);
        Assert.Contains("B→L", firstR1Command);
    }

    [Fact]
    public async Task ParallelScheduler_DestinationReady_HandledByCorrectManager()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        // R2 is holding wafer, waiting for cleaner
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R2",
            ["state"] = "holding",
            ["wafer"] = 3,
            ["waitingFor"] = "cleaner"
        });

        await Task.Delay(100);
        _logs.Clear(); // Clear logs to focus on next action

        // Cleaner becomes ready
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
        {
            ["station"] = "cleaner",
            ["state"] = "empty",
            ["wafer"] = (int?)null
        });

        await Task.Delay(100);

        // Assert - R2Manager should send DESTINATION_READY
        Assert.Contains(_logs, l => l.Contains("R2Manager") && l.Contains("DESTINATION_READY"));
    }

    [Fact]
    public async Task ParallelScheduler_AllWafersCompleted_EventTriggered()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        bool completionEventTriggered = false;

        scheduler.AllWafersCompleted += (s, e) =>
        {
            completionEventTriggered = true;
        };

        await scheduler.StartAsync();

        // Simulate completing all 10 wafers by returning them from buffer
        for (int i = 1; i <= 10; i++)
        {
            await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "STATION_STATUS", new JObject
            {
                ["station"] = "buffer",
                ["state"] = "occupied",
                ["wafer"] = i
            });

            await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
            {
                ["robot"] = "R1",
                ["state"] = "idle",
                ["wafer"] = (int?)null
            });

            await Task.Delay(50);
        }

        // Assert
        Assert.True(completionEventTriggered, "AllWafersCompleted event should have been triggered");
        Assert.Equal(10, scheduler.CompletedCount);
    }

    [Fact]
    public async Task ParallelScheduler_Guards_FilterRobotStatusCorrectly()
    {
        // Arrange
        var orchestrator = new EventBusOrchestrator(new OrchestratorConfig { EnableLogging = false });
        var scheduler = new ParallelSchedulerMachine(orchestrator);
        await scheduler.StartAsync();

        _logs.Clear();

        // Send R1 status - should be handled by R1Manager only
        await orchestrator.SendEventAsync("SYSTEM", "parallelScheduler", "ROBOT_STATUS", new JObject
        {
            ["robot"] = "R1",
            ["state"] = "idle",
            ["wafer"] = (int?)null
        });

        await Task.Delay(100);

        // Assert - Only R1Manager should handle this
        var r1ManagerLogs = _logs.Count(l => l.Contains("R1Manager"));
        var r2ManagerLogs = _logs.Count(l => l.Contains("R2Manager") && l.Contains("ROBOT_STATUS"));
        var r3ManagerLogs = _logs.Count(l => l.Contains("R3Manager") && l.Contains("ROBOT_STATUS"));

        Assert.True(r1ManagerLogs > 0, "R1Manager should handle R1 status");
        Assert.Equal(0, r2ManagerLogs);
        Assert.Equal(0, r3ManagerLogs);
    }
}
