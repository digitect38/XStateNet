using XStateNet.GPU.Core;

namespace XStateNet.GPU.Tests
{
    public class GPUStateMachinePoolTests : IDisposable
    {
        private GPUStateMachinePool _pool;

        public GPUStateMachinePoolTests()
        {
            _pool = new GPUStateMachinePool();
        }

        public void Dispose()
        {
            _pool?.Dispose();
        }

        [Fact]
        public async Task InitializeAsync_CreatesPoolWithCorrectInstanceCount()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            int instanceCount = 100;

            // Act
            await _pool.InitializeAsync(instanceCount, definition);

            // Assert
            Assert.Equal(instanceCount, _pool.InstanceCount);
            Assert.NotNull(_pool.AcceleratorName);
            Assert.True(_pool.AvailableMemory > 0);
        }

        [Fact]
        public async Task SendEvent_ProcessesEventCorrectly()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            await _pool.InitializeAsync(10, definition);

            // Act
            _pool.SendEvent(0, "START", 0, 0);
            await _pool.ProcessEventsAsync();

            // Assert
            var state = _pool.GetState(0);
            Assert.Equal("Running", state);
        }

        [Fact]
        public async Task SendEventBatch_ProcessesMultipleEventsInParallel()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            int instanceCount = 100;
            await _pool.InitializeAsync(instanceCount, definition);

            var instanceIds = Enumerable.Range(0, instanceCount).ToArray();
            var eventNames = Enumerable.Repeat("START", instanceCount).ToArray();

            // Act
            await _pool.SendEventBatchAsync(instanceIds, eventNames);

            // Assert
            for (int i = 0; i < instanceCount; i++)
            {
                var state = _pool.GetState(i);
                Assert.Equal("Running", state);
            }
        }

        [Fact]
        public async Task GetStateDistribution_ReturnsCorrectDistribution()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            await _pool.InitializeAsync(100, definition);

            // Send different events to different instances
            for (int i = 0; i < 50; i++)
            {
                _pool.SendEvent(i, "START");
            }
            for (int i = 50; i < 80; i++)
            {
                _pool.SendEvent(i, "ERROR");
            }
            await _pool.ProcessEventsAsync();

            // Act
            var distribution = await _pool.GetStateDistributionAsync();

            // Assert
            Assert.Equal(50, distribution["Running"]);
            Assert.Equal(30, distribution["Failed"]);
            Assert.Equal(20, distribution["Idle"]);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task ProcessEventsAsync_HandlesVariousInstanceCounts(int instanceCount)
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            await _pool.InitializeAsync(instanceCount, definition);

            // Act
            for (int i = 0; i < instanceCount; i++)
            {
                _pool.SendEvent(i, "START");
            }
            await _pool.ProcessEventsAsync();

            // Assert
            var distribution = await _pool.GetStateDistributionAsync();
            Assert.Equal(instanceCount, distribution["Running"]);
        }

        [Fact]
        public async Task GetMetrics_ReturnsValidPerformanceMetrics()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            await _pool.InitializeAsync(100, definition);

            // Act
            var metrics = _pool.GetMetrics();

            // Assert
            Assert.Equal(100, metrics.InstanceCount);
            Assert.Equal(3, metrics.StateCount);
            Assert.Equal(3, metrics.EventTypeCount);
            Assert.True(metrics.MemoryUsed > 0);
            Assert.NotNull(metrics.AcceleratorType);
            Assert.True(metrics.MaxParallelism > 0);
        }

        [Fact]
        public async Task SendEvent_ThrowsOnInvalidInstanceId()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            await _pool.InitializeAsync(10, definition);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _pool.SendEvent(-1, "START"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _pool.SendEvent(10, "START"));
        }

        [Fact]
        public async Task SendEvent_ThrowsOnInvalidEventName()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            await _pool.InitializeAsync(10, definition);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                _pool.SendEvent(0, "INVALID_EVENT"));
        }

        [Fact]
        public async Task ComplexStateTransitions_WorkCorrectly()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            await _pool.InitializeAsync(10, definition);

            // Act - Complex state transition sequence
            _pool.SendEvent(0, "START");
            await _pool.ProcessEventsAsync();
            Assert.Equal("Running", _pool.GetState(0));

            _pool.SendEvent(0, "COMPLETE");
            await _pool.ProcessEventsAsync();
            Assert.Equal("Idle", _pool.GetState(0));

            _pool.SendEvent(0, "ERROR");
            await _pool.ProcessEventsAsync();
            Assert.Equal("Failed", _pool.GetState(0));
        }

        [Fact]
        public async Task ParallelProcessing_MaintainsStateConsistency()
        {
            // Arrange
            var definition = CreateSimpleStateMachineDefinition();
            int instanceCount = 1000;
            await _pool.InitializeAsync(instanceCount, definition);

            // Act - Send events in parallel
            var tasks = Enumerable.Range(0, instanceCount).Select(i =>
                Task.Run(() => _pool.SendEvent(i, i % 2 == 0 ? "START" : "ERROR"))
            ).ToArray();

            await Task.WhenAll(tasks);
            await _pool.ProcessEventsAsync();

            // Assert
            var distribution = await _pool.GetStateDistributionAsync();
            Assert.Equal(500, distribution["Running"]);
            Assert.Equal(500, distribution["Failed"]);
        }

        private GPUStateMachineDefinition CreateSimpleStateMachineDefinition()
        {
            var definition = new GPUStateMachineDefinition("TestMachine", 3, 3);

            // Define states
            definition.StateNames[0] = "Idle";
            definition.StateNames[1] = "Running";
            definition.StateNames[2] = "Failed";

            // Define events
            definition.EventNames[0] = "START";
            definition.EventNames[1] = "COMPLETE";
            definition.EventNames[2] = "ERROR";

            // Define transition table
            definition.TransitionTable = new[]
            {
                // From Idle
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1 }, // START -> Running
                new TransitionEntry { FromState = 0, EventType = 2, ToState = 2 }, // ERROR -> Failed

                // From Running
                new TransitionEntry { FromState = 1, EventType = 1, ToState = 0 }, // COMPLETE -> Idle
                new TransitionEntry { FromState = 1, EventType = 2, ToState = 2 }, // ERROR -> Failed

                // From Failed
                new TransitionEntry { FromState = 2, EventType = 0, ToState = 1 }, // START -> Running
            };

            return definition;
        }
    }
}