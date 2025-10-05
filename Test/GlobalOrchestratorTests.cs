using XStateNet.Orchestration;
using Xunit;

namespace XStateNet.Tests;

/// <summary>
/// Tests for GlobalOrchestratorManager singleton with channel group isolation
/// </summary>
public class GlobalOrchestratorTests : IDisposable
{
    private readonly List<ChannelGroupToken> _createdGroups = new();

    [Fact]
    public void GlobalOrchestrator_IsSingleton()
    {
        // Arrange & Act
        var instance1 = GlobalOrchestratorManager.Instance;
        var instance2 = GlobalOrchestratorManager.Instance;

        // Assert
        Assert.Same(instance1, instance2);
        Assert.Same(instance1.Orchestrator, instance2.Orchestrator);
    }

    [Fact]
    public void ChannelGroupToken_HasUniqueGroupId()
    {
        // Arrange & Act
        var token1 = CreateGroup("Test1");
        var token2 = CreateGroup("Test2");

        // Assert
        Assert.NotEqual(token1.GroupId, token2.GroupId);
        Assert.NotEqual(token1.Id, token2.Id);
    }

    [Fact]
    public void CreateScopedMachineId_IncludesGroupId()
    {
        // Arrange
        var token = CreateGroup("TestGroup");

        // Act
        var machineId = GlobalOrchestratorManager.Instance.CreateScopedMachineId(token, "counter");

        // Assert - Format is #counter_groupId_guid
        Assert.Contains($"#counter_{token.GroupId}_", machineId);
    }

    [Fact]
    public void ReleaseChannelGroup_MarksTokenAsReleased()
    {
        // Arrange
        var token = CreateGroup("TestGroup");

        // Act
        GlobalOrchestratorManager.Instance.ReleaseChannelGroup(token);

        // Assert
        Assert.True(token.IsReleased);
    }

    [Fact]
    public void ChannelGroupDispose_AutomaticallyReleasesGroup()
    {
        // Arrange
        var token = CreateGroup("TestGroup");

        // Act
        token.Dispose();

        // Assert
        Assert.True(token.IsReleased);
    }

    [Fact]
    public async Task ChannelGroups_IsolateMachines()
    {
        // Arrange
        var orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
        var group1 = CreateGroup("Group1");
        var group2 = CreateGroup("Group2");

        var json = @"{
            id: 'counter',
            initial: 'idle',
            context: { count: 0 },
            states: {
                idle: {
                    on: { INC: 'counting' }
                },
                counting: {}
            }
        }";

        // Act - Create machines with same base ID but different channel groups
        var machine1 = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            "counter", json, orchestrator, group1);
        var machine2 = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            "counter", json, orchestrator, group2);

        await orchestrator.StartMachineAsync(machine1.Id);
        await orchestrator.StartMachineAsync(machine2.Id);

        // Assert - Machine IDs are different due to channel group scoping
        Assert.NotEqual(machine1.Id, machine2.Id);
        // Format is counter_groupId_guid (normalized, without # prefix)
        Assert.Contains($"_{group1.GroupId}_", machine1.Id);
        Assert.Contains($"_{group2.GroupId}_", machine2.Id);
    }

    [Fact]
    public async Task ReleaseChannelGroup_UnregistersAllGroupMachines()
    {
        // Arrange
        var orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
        var group = CreateGroup("TestGroup");

        var json = @"{
            id: 'simple',
            initial: 'idle',
            states: {
                idle: {}
            }
        }";

        var machine1 = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            "machine1", json, orchestrator, group);
        var machine2 = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
            "machine2", json, orchestrator, group);

        await orchestrator.StartMachineAsync(machine1.Id);
        await orchestrator.StartMachineAsync(machine2.Id);

        var statsBefore = orchestrator.GetStats();
        var registeredBefore = statsBefore.RegisteredMachines;

        // Act - Release channel group
        GlobalOrchestratorManager.Instance.ReleaseChannelGroup(group);

        // Assert - Machines unregistered
        var statsAfter = orchestrator.GetStats();
        Assert.Equal(registeredBefore - 2, statsAfter.RegisteredMachines);
    }

    [Fact]
    public async Task ParallelChannelGroups_NoInterference()
    {
        // Arrange
        var orchestrator = GlobalOrchestratorManager.Instance.Orchestrator;
        var tasks = new List<Task>();

        var json = @"{
            id: 'counter',
            initial: 'active',
            context: { count: 0 },
            states: {
                active: {
                    on: { TICK: 'active' }
                }
            }
        }";

        // Act - Create 10 parallel channel groups with machines
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var group = CreateGroup($"ParallelGroup_{i}");
                var machine = ExtendedPureStateMachineFactory.CreateWithChannelGroup(
                    "counter", json, orchestrator, group);

                await orchestrator.StartMachineAsync(machine.Id);
                await Task.Delay(10);

                group.Dispose();
            }));
        }

        // Assert - No exceptions, all complete successfully
        await Task.WhenAll(tasks);
        Assert.True(true); // All tasks completed without exceptions
    }

    [Fact]
    public void Metrics_TrackActiveChannelGroups()
    {
        // Arrange
        var countBefore = GlobalOrchestratorManager.Instance.ActiveChannelGroupCount;

        // Act
        var group1 = CreateGroup("Test1");
        var group2 = CreateGroup("Test2");
        var countWith2 = GlobalOrchestratorManager.Instance.ActiveChannelGroupCount;

        group1.Dispose();
        var countWith1 = GlobalOrchestratorManager.Instance.ActiveChannelGroupCount;

        // Assert
        Assert.Equal(countBefore + 2, countWith2);
        Assert.Equal(countBefore + 1, countWith1);
    }

    private ChannelGroupToken CreateGroup(string name)
    {
        var token = GlobalOrchestratorManager.Instance.CreateChannelGroup(name);
        _createdGroups.Add(token);
        return token;
    }

    public void Dispose()
    {
        // Cleanup all created groups
        foreach (var group in _createdGroups)
        {
            try
            {
                if (!group.IsReleased)
                {
                    group.Dispose();
                }
            }
            catch { }
        }
    }
}
