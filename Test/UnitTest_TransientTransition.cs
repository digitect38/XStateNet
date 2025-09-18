using Xunit;
using XStateNet;
using System.Threading.Tasks;

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

            // Enable debug logging
            Logger.CurrentLevel = Logger.LogLevel.Debug;

            var stateMachine = StateMachine.CreateFromScript(stateMachineJson);
            stateMachine.Start();

            // The transition should happen immediately
            await Task.Delay(50);

            var currentState = stateMachine.GetActiveStateString();
            Assert.Equal("#transient.B", currentState);
        }
    }
}