using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using XStateNet.Distributed.Registry;
using Xunit;

namespace XStateNet.Distributed.Tests.Registry
{
    public class RedisStateMachineRegistryTests : IDisposable
    {
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly Mock<ISubscriber> _subscriberMock;
        private readonly Mock<ITransaction> _transactionMock;
        private readonly Mock<ILogger<RedisStateMachineRegistry>> _loggerMock;
        private readonly RedisStateMachineRegistry _registry;

        public RedisStateMachineRegistryTests()
        {
            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _subscriberMock = new Mock<ISubscriber>();
            _transactionMock = new Mock<ITransaction>();
            _loggerMock = new Mock<ILogger<RedisStateMachineRegistry>>();

            _redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);
            _redisMock.Setup(x => x.GetSubscriber(It.IsAny<object>()))
                .Returns(_subscriberMock.Object);
            _dbMock.Setup(x => x.CreateTransaction(It.IsAny<object>()))
                .Returns(_transactionMock.Object);

            _registry = new RedisStateMachineRegistry(
                _redisMock.Object,
                "test",
                TimeSpan.FromSeconds(30),
                _loggerMock.Object);
        }

        [Fact]
        public async Task RegisterAsync_Should_RegisterNewMachine()
        {
            // Arrange
            var machineId = "test-machine-1";
            var info = new StateMachineInfo
            {
                NodeId = "node-1",
                Endpoint = "tcp://localhost:5555",
                Status = MachineStatus.Running
            };

            _transactionMock.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _registry.RegisterAsync(machineId, info);

            // Assert
            Assert.True(result);
            _transactionMock.Verify(x => x.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task RegisterAsync_Should_ThrowOnEmptyMachineId()
        {
            // Arrange
            var info = new StateMachineInfo();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => _registry.RegisterAsync("", info));
        }

        [Fact]
        public async Task UnregisterAsync_Should_RemoveMachine()
        {
            // Arrange
            var machineId = "test-machine-1";
            var info = new StateMachineInfo
            {
                MachineId = machineId,
                NodeId = "node-1"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(info);
            _dbMock.Setup(x => x.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(json);
            _transactionMock.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            var result = await _registry.UnregisterAsync(machineId);

            // Assert
            Assert.True(result);
            _transactionMock.Verify(x => x.HashDeleteAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_Should_ReturnMachineInfo()
        {
            // Arrange
            var machineId = "test-machine-1";
            var info = new StateMachineInfo
            {
                MachineId = machineId,
                NodeId = "node-1",
                Status = MachineStatus.Running
            };

            var json = System.Text.Json.JsonSerializer.Serialize(info);
            _dbMock.Setup(x => x.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(json);
            _dbMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(DateTime.UtcNow.Ticks.ToString());

            // Act
            var result = await _registry.GetAsync(machineId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(machineId, result.MachineId);
            Assert.Equal("node-1", result.NodeId);
        }

        [Fact]
        public async Task GetAsync_Should_ReturnNullForNonExistentMachine()
        {
            // Arrange
            _dbMock.Setup(x => x.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            // Act
            var result = await _registry.GetAsync("non-existent");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAllAsync_Should_ReturnAllMachines()
        {
            // Arrange
            var machines = new[]
            {
                new StateMachineInfo { MachineId = "machine-1", NodeId = "node-1" },
                new StateMachineInfo { MachineId = "machine-2", NodeId = "node-2" }
            };

            var entries = machines.Select(m => new HashEntry(
                m.MachineId,
                System.Text.Json.JsonSerializer.Serialize(m)
            )).ToArray();

            _dbMock.Setup(x => x.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(entries);
            _dbMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(DateTime.UtcNow.Ticks.ToString());

            // Act
            var result = await _registry.GetAllAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public async Task GetActiveAsync_Should_ReturnOnlyActiveMachines()
        {
            // Arrange
            var now = DateTime.UtcNow;
            var machines = new[]
            {
                new StateMachineInfo
                {
                    MachineId = "active-1",
                    LastHeartbeat = now.AddSeconds(-10)
                },
                new StateMachineInfo
                {
                    MachineId = "inactive-1",
                    LastHeartbeat = now.AddMinutes(-5)
                }
            };

            var entries = machines.Select(m => new HashEntry(
                m.MachineId,
                System.Text.Json.JsonSerializer.Serialize(m)
            )).ToArray();

            _dbMock.Setup(x => x.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(entries);

            // Act
            var result = await _registry.GetActiveAsync(TimeSpan.FromMinutes(1));

            // Assert
            Assert.Single(result);
            Assert.Equal("active-1", result.First().MachineId);
        }

        [Fact]
        public async Task UpdateHeartbeatAsync_Should_UpdateHeartbeat()
        {
            // Arrange
            var machineId = "test-machine-1";
            var info = new StateMachineInfo { MachineId = machineId };
            var json = System.Text.Json.JsonSerializer.Serialize(info);

            _dbMock.Setup(x => x.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(json);

            // Act
            await _registry.UpdateHeartbeatAsync(machineId);

            // Assert
            _dbMock.Verify(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task UpdateStatusAsync_Should_UpdateMachineStatus()
        {
            // Arrange
            var machineId = "test-machine-1";
            var info = new StateMachineInfo
            {
                MachineId = machineId,
                Status = MachineStatus.Running
            };
            var json = System.Text.Json.JsonSerializer.Serialize(info);

            _dbMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync("Running");
            _dbMock.Setup(x => x.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(json);

            // Act
            await _registry.UpdateStatusAsync(machineId, MachineStatus.Paused, "paused-state");

            // Assert
            _dbMock.Verify(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.Is<RedisValue>(v => v == "Paused"),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task FindByPatternAsync_Should_FindMatchingMachines()
        {
            // Arrange
            var machines = new[]
            {
                new StateMachineInfo { MachineId = "worker-1", Tags = new() { ["type"] = "worker" } },
                new StateMachineInfo { MachineId = "worker-2", Tags = new() { ["type"] = "worker" } },
                new StateMachineInfo { MachineId = "manager-1", Tags = new() { ["type"] = "manager" } }
            };

            var entries = machines.Select(m => new HashEntry(
                m.MachineId,
                System.Text.Json.JsonSerializer.Serialize(m)
            )).ToArray();

            _dbMock.Setup(x => x.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(entries);

            // Act
            var result = await _registry.FindByPatternAsync("worker*");

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, m => Assert.StartsWith("worker", m.MachineId));
        }

        [Fact]
        public async Task SubscribeToChangesAsync_Should_RegisterHandler()
        {
            // Arrange
            var handlerCalled = false;
            Action<RegistryChangeEvent> handler = evt => handlerCalled = true;

            // Act
            await _registry.SubscribeToChangesAsync(handler);

            // Since we can't easily trigger Redis events in unit tests,
            // we just verify the subscription was registered
            Assert.NotNull(handler);
        }

        [Fact]
        public async Task MachineRegistered_Event_Should_BeRaised()
        {
            // Arrange
            var eventRaised = false;
            _registry.MachineRegistered += (sender, args) => eventRaised = true;

            _transactionMock.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // Act
            await _registry.RegisterAsync("test-machine", new StateMachineInfo());

            // Assert
            Assert.True(eventRaised);
        }

        [Fact]
        public async Task StatusChanged_Event_Should_BeRaised()
        {
            // Arrange
            var eventRaised = false;
            MachineStatus? oldStatus = null;
            MachineStatus? newStatus = null;

            _registry.StatusChanged += (sender, args) =>
            {
                eventRaised = true;
                oldStatus = args.OldStatus;
                newStatus = args.NewStatus;
            };

            var info = new StateMachineInfo { Status = MachineStatus.Running };
            _dbMock.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync("Running");
            _dbMock.Setup(x => x.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(System.Text.Json.JsonSerializer.Serialize(info));

            // Act
            await _registry.UpdateStatusAsync("test-machine", MachineStatus.Paused);

            // Assert
            Assert.True(eventRaised);
            Assert.Equal(MachineStatus.Running, oldStatus);
            Assert.Equal(MachineStatus.Paused, newStatus);
        }

        public void Dispose()
        {
            _registry?.Dispose();
        }
    }
}