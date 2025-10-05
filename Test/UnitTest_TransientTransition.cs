using Xunit;

// Suppress obsolete warning - standalone transient transition test with no inter-machine communication
#pragma warning disable CS0618

namespace XStateNet.UnitTest
{
    public class UnitTest_TransientTransition
    {
        [Fact]
        public async Task TestTransientTransition()
        {
            var stateMachineJson = @"{
                'id': 'transient',
                'initial': 'A',
                'states': {
                    'A': {
                        'on': {
                            '': 'B'
                        }
                    },
                    'B': {
                        'type': 'final'
                    }
                }
            }";

            // Set log level to Warning
            Logger.CurrentLevel = Logger.LogLevel.Warning;

            var stateMachine = StateMachineFactory.CreateFromScript(stateMachineJson, false, true);
            await stateMachine.StartAsync();

            // The transition should happen immediately
            await Task.Delay(50);

            var currentState = stateMachine.GetActiveStateNames();
            Assert.Equal($"{stateMachine.machineId}.B", currentState);
        }
    }
}