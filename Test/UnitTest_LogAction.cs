
using Xunit;

// Suppress obsolete warning - standalone log action test with no inter-machine communication
// For tests with inter-machine communication, use OrchestratorTestBase with EventBusOrchestrator
#pragma warning disable CS0618

namespace XStateNet.UnitTest
{
    public class UnitTest_LogAction : IDisposable
    {
        //private readonly StringWriter _stringWriter;
        //private readonly TextWriter _originalOutput;

        public UnitTest_LogAction()
        {
            //_originalOutput = Console.Out;
            //_stringWriter = new StringWriter();
            //Console.SetOut(_stringWriter);
        }

        public void Dispose()
        {
            //Console.SetOut(_originalOutput);
            //_stringWriter.Dispose();
        }

        [Fact]
        public async Task TestLogActionWithString()
        {
            var actions = new ActionMap
            {
                ["logAction"] = new List<NamedAction> {
                    new NamedAction("logAction", (sm) => {
                        Logger.Info("Hello from the log action!");
                    })
                }
            };

            var script = @"{
                'id': 'logTest',
                'initial': 'start',
                'states': {
                    'start': {
                        'entry': 'logAction'
                    }
                }
            }";

            var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, actions, new GuardMap());
            await stateMachine.StartAsync();

            //var output = _stringWriter.ToString();
            //Assert.Contains("Hello from the log action!", output);
        }

        [Fact]
        public async Task TestLogActionWithExpression()
        {
            var actions = new ActionMap
            {
                ["logExpressionAction"] = new List<NamedAction> {
                    new NamedAction("logExpressionAction", (sm) => {
                        var value = sm.ContextMap?["value"];
                        Logger.Info($"The value is {value}");
                    })
                }
            };

            var script = @"{
                'id': 'logExprTest',
                'context': {
                    'value': 42
                },
                'initial': 'start',
                'states': {
                    'start': {
                        'entry': 'logExpressionAction'
                    }
                }
            }";

            var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, actions, new GuardMap());
            await stateMachine.StartAsync();

            //var output = _stringWriter.ToString();
            //Assert.Contains("The value is 42", output);
        }

        [Fact]
        public async Task TestLogActionOnTransition()
        {
            var actions = new ActionMap
            {
                ["logTransitionAction"] = new List<NamedAction> {
                    new NamedAction("logTransitionAction", (sm) => {
                        Logger.Info("Transitioning to active");
                    })
                }
            };

            var script = @"{
                'id': 'logTransitionTest',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'GO': {
                                'target': 'active',
                                'actions': ['logTransitionAction']
                            }
                        }
                    },
                    'active': {}
                }
            }";

            var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe: false, true, actions, new GuardMap());
            await stateMachine.StartAsync();
            stateMachine.Send("GO");

            //var output = _stringWriter.ToString();
            //Assert.Contains("Transitioning to active", output);
        }
    }
}
