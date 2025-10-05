using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.StateMachines;
using XStateNet.Orchestration;
using Xunit;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Tests for TimeoutProtectedStateMachine wrapper
    /// Verifies that state machine timeout protection works correctly
    /// </summary>
    public class TimeoutProtectedStateMachineTests : IDisposable
    {
        private readonly EventBusOrchestrator _orchestrator;
        private readonly ITimeoutProtection _timeoutProtection;

        public TimeoutProtectedStateMachineTests()
        {
            _orchestrator = new EventBusOrchestrator(new OrchestratorConfig());
            _timeoutProtection = new TimeoutProtection(
                new TimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(5) });
        }

        public void Dispose()
        {
            _orchestrator?.Dispose();
            _timeoutProtection?.Dispose();
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_StartsSuccessfully()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection,
                dlq: null,
                options: null,
                logger: null);

            // Act
            await protectedMachine.StartAsync();

            // Assert
            Assert.True(protectedMachine.IsRunning);
            Assert.Contains("idle", protectedMachine.CurrentState);
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_TransitionCompletesWithinTimeout()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection,
                options: new TimeoutProtectedStateMachineOptions
                {
                    DefaultTransitionTimeout = TimeSpan.FromSeconds(5)
                });

            await protectedMachine.StartAsync();

            // Act
            var result = await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);

            // Assert
            Assert.True(result);
            Assert.Contains("active", protectedMachine.CurrentState);
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_ConfiguresStateTimeout()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection);

            // Act
            protectedMachine.SetStateTimeout("active", TimeSpan.FromSeconds(10));
            await protectedMachine.StartAsync();
            await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);

            // Assert - No exception thrown, timeout configured
            Assert.Contains("active", protectedMachine.CurrentState);
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_ConfiguresTransitionTimeout()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection);

            // Act
            protectedMachine.SetTransitionTimeout("idle", "START", TimeSpan.FromSeconds(3));
            await protectedMachine.StartAsync();
            var result = await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);

            // Assert
            Assert.True(result);
            Assert.Contains("active", protectedMachine.CurrentState);
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_CollectsStatistics()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection);

            await protectedMachine.StartAsync();

            // Act
            await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);
            await protectedMachine.SendAsync("STOP", payload: null, CancellationToken.None);
            await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);

            var stats = protectedMachine.GetStatistics();

            // Assert
            Assert.NotNull(stats);
            Assert.True(stats.TotalTransitions >= 3);
            Assert.Equal(protectedMachine.Id, stats.MachineId);
            Assert.NotNull(stats.CurrentState);
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_MultipleTransitions()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection);

            await protectedMachine.StartAsync();

            // Act - Perform multiple transitions
            await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);
            Assert.Contains("active", protectedMachine.CurrentState);

            await protectedMachine.SendAsync("STOP", payload: null, CancellationToken.None);
            Assert.Contains("idle", protectedMachine.CurrentState);

            await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);
            Assert.Contains("active", protectedMachine.CurrentState);

            // Assert
            var stats = protectedMachine.GetStatistics();
            Assert.True(stats.TotalTransitions >= 3);
            Assert.Equal(0, stats.TotalTimeouts);  // No timeouts occurred
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_WithOptions()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var options = new TimeoutProtectedStateMachineOptions
            {
                DefaultStateTimeout = TimeSpan.FromMinutes(1),
                DefaultTransitionTimeout = TimeSpan.FromSeconds(10),
                EnableTimeoutRecovery = true,
                SendTimeoutEventsToDLQ = false
            };

            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection,
                options: options);

            // Act
            await protectedMachine.StartAsync();
            await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);

            // Assert
            Assert.Contains("active", protectedMachine.CurrentState);
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_WrapsExistingMachine()
        {
            // Arrange - Use real PureStateMachine from orchestrator
            var definition = @"{
                'id': 'test_machine',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': { 'GO': 'running' }
                    },
                    'running': {
                        'on': { 'STOP': 'idle' }
                    }
                }
            }";

            var pureMachine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "test_machine",
                json: definition,
                orchestrator: _orchestrator,
                enableGuidIsolation: true);

            // Extract underlying IStateMachine from PureStateMachineAdapter
            var innerMachine = ((XStateNet.Orchestration.PureStateMachineAdapter)pureMachine).GetUnderlying();

            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection);

            // Act
            await protectedMachine.StartAsync();
            await protectedMachine.SendAsync("GO", payload: null, CancellationToken.None);

            // Assert
            Assert.Contains("running", protectedMachine.CurrentState);
            Assert.True(protectedMachine.IsRunning);
        }

        [Fact]
        public async Task TimeoutProtectedStateMachine_StopsCleanly()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();
            var protectedMachine = new TimeoutProtectedStateMachine(
                innerMachine,
                _timeoutProtection);

            await protectedMachine.StartAsync();
            await protectedMachine.SendAsync("START", payload: null, CancellationToken.None);

            // Act
            protectedMachine.Stop();

            // Assert - Should stop without exceptions
            Assert.False(protectedMachine.IsRunning);
        }

        [Fact]
        public void TimeoutProtectedStateMachine_RequiresInnerMachine()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new TimeoutProtectedStateMachine(
                    null!,
                    _timeoutProtection));
        }

        [Fact]
        public void TimeoutProtectedStateMachine_RequiresTimeoutProtection()
        {
            // Arrange
            var innerMachine = CreateSimpleStateMachine();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new TimeoutProtectedStateMachine(
                    innerMachine,
                    null!));
        }

        // Helper: Create a simple state machine for testing
        private IStateMachine CreateSimpleStateMachine()
        {
            var definition = @"{
                'id': 'simple_test',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': { 'START': 'active' }
                    },
                    'active': {
                        'on': { 'STOP': 'idle' }
                    }
                }
            }";

            var pureMachine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
                id: "simple_test",
                json: definition,
                orchestrator: _orchestrator,
                enableGuidIsolation: true);

            // PureStateMachineAdapter has GetUnderlying() to access the IStateMachine
            return ((XStateNet.Orchestration.PureStateMachineAdapter)pureMachine).GetUnderlying();
        }
    }
}
