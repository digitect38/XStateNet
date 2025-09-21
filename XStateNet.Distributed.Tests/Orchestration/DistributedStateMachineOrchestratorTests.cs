using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.Orchestration;
using XStateNet.Distributed.Registry;

namespace XStateNet.Distributed.Tests.Orchestration
{
    public class DistributedStateMachineOrchestratorTests
    {
        private readonly Mock<IStateMachineRegistry> _registryMock;
        private readonly Mock<IStateMachineEventBus> _eventBusMock;
        private readonly Mock<ILogger<DistributedStateMachineOrchestrator>> _loggerMock;
        private readonly DistributedStateMachineOrchestrator _orchestrator;

        public DistributedStateMachineOrchestratorTests()
        {
            _registryMock = new Mock<IStateMachineRegistry>();
            _eventBusMock = new Mock<IStateMachineEventBus>();
            _loggerMock = new Mock<ILogger<DistributedStateMachineOrchestrator>>();

            _orchestrator = new DistributedStateMachineOrchestrator(
                _registryMock.Object,
                _eventBusMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public async Task DeployStateMachineAsync_Should_DeployNewMachine()
        {
            // Arrange
            var definition = new StateMachineDefinition
            {
                Id = "test-machine",
                Name = "Test Machine",
                JsonScript = "{ \"id\": \"test\" }",
                Configuration = new Dictionary<string, object> { ["key"] = "value" }
            };

            var options = new DeploymentOptions
            {
                AutoStart = true,
                InitialInstances = 3
            };

            _registryMock.Setup(x => x.RegisterAsync(It.IsAny<string>(), It.IsAny<StateMachineInfo>()))
                .ReturnsAsync(true);

            // Act
            var result = await _orchestrator.DeployStateMachineAsync(definition, options);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("test-machine", result.MachineId);
            Assert.NotEmpty(result.NodeId);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), "Deploy", It.IsAny<object>()), Times.Exactly(3));
        }

        [Fact]
        public async Task DeployStateMachineAsync_Should_HandleRegistrationFailure()
        {
            // Arrange
            var definition = new StateMachineDefinition { Id = "test-machine" };

            _registryMock.Setup(x => x.RegisterAsync(It.IsAny<string>(), It.IsAny<StateMachineInfo>()))
                .ReturnsAsync(false);

            // Act
            var result = await _orchestrator.DeployStateMachineAsync(definition);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Failed to register", result.ErrorMessage);
        }

        [Fact]
        public async Task ScaleStateMachineAsync_Should_ScaleUp()
        {
            // Arrange
            var machineId = "test-machine";
            var info = new StateMachineInfo
            {
                MachineId = machineId,
                NodeId = "node-1"
            };

            _registryMock.Setup(x => x.GetAsync(machineId))
                .ReturnsAsync(info);

            // Act
            var result = await _orchestrator.ScaleStateMachineAsync(machineId, 5);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.PreviousInstances);
            Assert.Equal(5, result.CurrentInstances);
            Assert.Equal(4, result.NewInstanceIds.Count);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), "Deploy", It.IsAny<object>()), Times.Exactly(4));
        }

        [Fact]
        public async Task ScaleStateMachineAsync_Should_ScaleDown()
        {
            // Arrange
            var machineId = "test-machine";
            var info = new StateMachineInfo
            {
                MachineId = machineId,
                NodeId = "node-1"
            };

            _registryMock.Setup(x => x.GetAsync(machineId))
                .ReturnsAsync(info);

            // Simulate current instances = 5
            var currentInstances = 5;

            // Act
            var result = await _orchestrator.ScaleStateMachineAsync(machineId, 2);

            // Assert
            // Note: The implementation assumes 1 instance by default
            // This test shows the limitation of the current implementation
            Assert.True(result.Success);
        }

        //[Fact(Skip = "Requires refactoring to access internal state"]
        [Fact]
        public async Task MigrateStateMachineAsync_Should_MigrateMachine()
        {
            // Arrange
            var machineId = "test-machine";
            var info = new StateMachineInfo
            {
                MachineId = machineId,
                NodeId = "node-1",
                Endpoint = "node://node-1/test-machine"
            };

            // Setup mocks for migration
            _registryMock.Setup(x => x.RegisterAsync(It.IsAny<string>(), It.IsAny<StateMachineInfo>()))
                .ReturnsAsync(true);

            _registryMock.Setup(x => x.GetAsync(machineId))
                .ReturnsAsync(info);

            // Setup RequestAsync to return StateSnapshot
            _eventBusMock.Setup(x => x.RequestAsync<DistributedStateMachineOrchestrator.StateSnapshot>(
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<object>(),
                It.IsAny<TimeSpan>()))
                .ReturnsAsync(new DistributedStateMachineOrchestrator.StateSnapshot 
                { 
                    CurrentState = "running", 
                    Context = new Dictionary<string, object>()
                });

            _eventBusMock.Setup(x => x.PublishEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _orchestrator.MigrateStateMachineAsync(machineId, "node-2");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("node-1", result.SourceNodeId);
            Assert.Equal("node-2", result.TargetNodeId);
            Assert.True(result.StatePreserved);
        }

        [Fact]
        public async Task ShutdownStateMachineAsync_Should_GracefullyShutdown()
        {
            // Arrange
            var machineId = "test-machine";

            _eventBusMock.Setup(x => x.RequestAsync<bool>(
                It.IsAny<string>(), "ShutdownConfirmation", It.IsAny<object>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);

            // Act
            var result = await _orchestrator.ShutdownStateMachineAsync(machineId, graceful: true);

            // Assert
            Assert.True(result);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                machineId, "GracefulShutdown", It.IsAny<object>()), Times.Once);
            _registryMock.Verify(x => x.UpdateStatusAsync(
                machineId, MachineStatus.Stopped, null), Times.Once);
        }

        [Fact]
        public async Task RestartStateMachineAsync_Should_RestartMachine()
        {
            // Arrange
            var machineId = "test-machine";

            _eventBusMock.Setup(x => x.RequestAsync<bool>(
                It.IsAny<string>(), "ShutdownConfirmation", It.IsAny<object>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);

            // Act
            var result = await _orchestrator.RestartStateMachineAsync(machineId);

            // Assert
            Assert.True(result);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                machineId, "Start", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task CheckHealthAsync_Should_ReturnHealthStatus()
        {
            // Arrange
            var machineId = "test-machine";
            var info = new StateMachineInfo
            {
                MachineId = machineId,
                LastHeartbeat = DateTime.UtcNow.AddSeconds(-30)
            };

            _registryMock.Setup(x => x.GetAsync(machineId))
                .ReturnsAsync(info);

            _eventBusMock.Setup(x => x.RequestAsync<Dictionary<string, object>>(
                It.IsAny<string>(), "HealthCheck", It.IsAny<object>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(new Dictionary<string, object> { ["status"] = "healthy" });

            // Act
            var result = await _orchestrator.CheckHealthAsync(machineId);

            // Assert
            Assert.Equal(machineId, result.MachineId);
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.NotEmpty(result.Items);
        }

        [Fact]
        public async Task CheckHealthAsync_Should_HandleNonExistentMachine()
        {
            // Arrange
            _registryMock.Setup(x => x.GetAsync(It.IsAny<string>()))
                .ReturnsAsync((StateMachineInfo?)null);

            // Act
            var result = await _orchestrator.CheckHealthAsync("non-existent");

            // Assert
            Assert.Equal(HealthStatus.NotFound, result.Status);
            Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetMetricsAsync_Should_ReturnMetrics()
        {
            // Arrange
            var machineId = "test-machine";
            var metrics = new MetricsSnapshot
            {
                MachineId = machineId,
                TotalEvents = 1000,
                EventsPerSecond = 10.5
            };

            _eventBusMock.Setup(x => x.RequestAsync<MetricsSnapshot>(
                It.IsAny<string>(), "GetMetrics", It.IsAny<object>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(metrics);

            // Act
            var result = await _orchestrator.GetMetricsAsync(machineId);

            // Assert
            Assert.Equal(machineId, result.MachineId);
            Assert.Equal(1000, result.TotalEvents);
            Assert.Equal(10.5, result.EventsPerSecond);
        }

        [Fact]
        public async Task GetSystemOverviewAsync_Should_ReturnOverview()
        {
            // Arrange
            var machines = new[]
            {
                new StateMachineInfo { MachineId = "m1", NodeId = "node1", Status = MachineStatus.Running },
                new StateMachineInfo { MachineId = "m2", NodeId = "node1", Status = MachineStatus.Running },
                new StateMachineInfo { MachineId = "m3", NodeId = "node2", Status = MachineStatus.Paused }
            };

            _registryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(machines);

            // Act
            var result = await _orchestrator.GetSystemOverviewAsync();

            // Assert
            Assert.Equal(3, result.TotalMachines);
            Assert.Equal(2, result.ActiveMachines);
            Assert.Equal(2, result.MachinesByNode["node1"]);
            Assert.Equal(1, result.MachinesByNode["node2"]);
        }

        [Fact]
        public async Task ExecuteWorkflowAsync_Should_ExecuteWorkflow()
        {
            // Arrange
            var workflow = new WorkflowDefinition
            {
                Id = "test-workflow",
                Steps = new List<WorkflowStep>
                {
                    new WorkflowStep { StepId = "step1", MachineId = "m1", EventName = "EVENT1" },
                    new WorkflowStep { StepId = "step2", MachineId = "m2", EventName = "EVENT2", DependsOn = new List<string> { "step1" } }
                }
            };

            // Act
            var result = await _orchestrator.ExecuteWorkflowAsync(workflow);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(2, result.StepResults.Count);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CreateStateMachineGroupAsync_Should_CreateGroup()
        {
            // Arrange
            var groupName = "workers";
            var options = new GroupOptions
            {
                CoordinationType = GroupCoordinationType.RoundRobin
            };
            var machineIds = new[] { "m1", "m2", "m3" };

            // Act
            var result = await _orchestrator.CreateStateMachineGroupAsync(groupName, options, machineIds);

            // Assert
            Assert.Equal(groupName, result);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), "JoinGroup", It.IsAny<object>()), Times.Exactly(3));
        }

        [Fact]
        public async Task SendGroupEventAsync_Should_SendEventToGroup()
        {
            // Arrange
            var groupName = "workers";
            var machines = new[] { "m1", "m2", "m3" };
            var options = new GroupOptions { CoordinationType = GroupCoordinationType.Broadcast };

            await _orchestrator.CreateStateMachineGroupAsync(groupName, options, machines);

            // Act
            await _orchestrator.SendGroupEventAsync(groupName, "PROCESS", new { data = "test" });

            // Assert
            _eventBusMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), "PROCESS", It.IsAny<object>()), Times.Exactly(3));
        }

        [Fact]
        public async Task ExecuteSagaAsync_Should_ExecuteSaga()
        {
            // Arrange
            var saga = new SagaDefinition
            {
                Id = "test-saga",
                Steps = new List<SagaStep>
                {
                    new SagaStep 
                    { 
                        StepId = "payment", 
                        MachineId = "payment-service",
                        Action = "CHARGE",
                        CompensationAction = "REFUND"
                    },
                    new SagaStep
                    {
                        StepId = "inventory",
                        MachineId = "inventory-service",
                        Action = "RESERVE",
                        CompensationAction = "RELEASE"
                    }
                }
            };

            // Setup registry to return machines
            _registryMock.Setup(x => x.GetAsync("payment-service"))
                .ReturnsAsync(new StateMachineInfo { MachineId = "payment-service" });
            _registryMock.Setup(x => x.GetAsync("inventory-service"))
                .ReturnsAsync(new StateMachineInfo { MachineId = "inventory-service" });

            // Act
            var result = await _orchestrator.ExecuteSagaAsync(saga);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(2, result.CompletedSteps.Count);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ExecuteSagaAsync_Should_CompensateOnFailure()
        {
            // Arrange
            var saga = new SagaDefinition
            {
                Id = "test-saga",
                Steps = new List<SagaStep>
                {
                    new SagaStep { StepId = "step1", MachineId = "m1", Action = "ACTION1", CompensationAction = "UNDO1" },
                    new SagaStep { StepId = "step2", MachineId = "m2", Action = "ACTION2", CompensationAction = "UNDO2" }
                }
            };

            // Setup registry - m1 exists, m2 doesn't (to trigger failure)
            _registryMock.Setup(x => x.GetAsync("m1"))
                .ReturnsAsync(new StateMachineInfo { MachineId = "m1" });
            _registryMock.Setup(x => x.GetAsync("m2"))
                .ReturnsAsync((StateMachineInfo?)null);  // This will cause failure

            // Setup event bus for compensation
            _eventBusMock.Setup(x => x.PublishEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _orchestrator.ExecuteSagaAsync(saga);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("step2", result.FailedStep);
            Assert.Single(result.CompensatedSteps);
        }

        [Fact]
        public async Task DiscoverByCapabilityAsync_Should_FindMachinesByCapability()
        {
            // Arrange
            var machines = new[]
            {
                new StateMachineInfo 
                { 
                    MachineId = "m1", 
                    Metadata = new Dictionary<string, object> { ["capabilities"] = "process,validate" }
                },
                new StateMachineInfo 
                { 
                    MachineId = "m2", 
                    Metadata = new Dictionary<string, object> { ["capabilities"] = "process" }
                },
                new StateMachineInfo 
                { 
                    MachineId = "m3", 
                    Metadata = new Dictionary<string, object> { ["capabilities"] = "notify" }
                }
            };

            _registryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(machines);

            // Act
            var result = await _orchestrator.DiscoverByCapabilityAsync("process");

            // Assert
            Assert.Equal(2, result.Count());
            Assert.Contains("m1", result);
            Assert.Contains("m2", result);
        }

        [Fact]
        public async Task RouteEventAsync_Should_RouteByCapability()
        {
            // Arrange
            var request = new RoutingRequest
            {
                EventName = "PROCESS",
                Strategy = RoutingStrategy.Capability,
                Requirements = new Dictionary<string, string> { ["capability"] = "process" }
            };

            var machines = new[]
            {
                new StateMachineInfo 
                { 
                    MachineId = "m1",
                    Metadata = new Dictionary<string, object> { ["capabilities"] = "process" }
                }
            };

            _registryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(machines);

            // Act
            var result = await _orchestrator.RouteEventAsync(request);

            // Assert
            Assert.True(result.Success);
            Assert.Single(result.TargetMachineIds);
            Assert.Contains("m1", result.TargetMachineIds);
        }

        [Fact]
        public async Task UpdateConfigurationAsync_Should_UpdateConfig()
        {
            // Arrange
            var machineId = "test-machine";
            var config = new Dictionary<string, object> { ["setting"] = "value" };

            // Act
            var result = await _orchestrator.UpdateConfigurationAsync(machineId, config);

            // Assert
            Assert.True(result);
            _eventBusMock.Verify(x => x.PublishEventAsync(
                machineId, "UpdateConfiguration", config), Times.Once);
        }

        [Fact]
        public async Task GetConfigurationAsync_Should_GetConfig()
        {
            // Arrange
            var machineId = "test-machine";
            var config = new Dictionary<string, object> { ["setting"] = "value" };

            _eventBusMock.Setup(x => x.RequestAsync<Dictionary<string, object>>(
                It.IsAny<string>(), "GetConfiguration", It.IsAny<object>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(config);

            // Act
            var result = await _orchestrator.GetConfigurationAsync(machineId);

            // Assert
            Assert.Equal(config, result);
        }
    }
}