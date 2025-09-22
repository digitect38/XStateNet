using Xunit;

using XStateNet;
using System.Collections.Generic;
using System;


namespace AdvancedFeatures
{
    public class ComplexStateTests : IDisposable
    {
        private StateMachine CreateStateMachine(string uniqueId)
        {
            var script = GetScript(uniqueId);
            var stateMachine = StateMachine.CreateFromScript(script, new ActionMap(), new GuardMap());
            stateMachine.Start();
            return stateMachine;
        }

        [Fact]
        public void GetCurrentSubStatesTest1()
        {
            var uniqueId = "GetCurrentSubStatesTest1_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            var currentState = stateMachine!.GetSourceSubStateCollection(null).ToCsvString(stateMachine, true);
            Assert.Equal($"#{uniqueId}.A.A1.A1a;#{uniqueId}.A.A2", currentState);
        }

        [Fact]
        public void GetCurrentSubStatesTest2()
        {
            var uniqueId = "GetCurrentSubStatesTest2_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("TO_A1b");

            var currentState = stateMachine!.GetSourceSubStateCollection(null).ToCsvString(stateMachine);
            Assert.Equal($"#{uniqueId}.A.A1.A1b;#{uniqueId}.A.A2", currentState);
        }

        [Fact]
        public void GetCurrentSubStatesTest3()
        {
            var uniqueId = "GetCurrentSubStatesTest3_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("TO_B");

            var currentState = stateMachine!.GetSourceSubStateCollection(null).ToCsvString(stateMachine);
            Assert.Equal($"#{uniqueId}.B.B1", currentState);
        }



        private string GetScript(string uniqueId) => @"
            {
                'id': '" + uniqueId + @"',
                'initial': 'A',
                'states': {
                    'A': {
                        'type': 'parallel',
                        'on': {
                            'TO_B': 'B'
                        },
                        'states': {
                            'A1': {
                                'initial': 'A1a',
                                'on': {
                                    'TO_B1': '#{uniqueId}.B.B1'
                                },
                                'states': {
                                    'A1a': {
                                        'on': { 'TO_A1b': 'A1b' }
                                    },
                                    'A1b': {
                                        'on': { 'TO_A1a': 'A1a' }
                                    }
                                }
                            },
                            'A2': {}
                        }
                    },
                    'B': {
                        'on': { 'TO_A': 'A' },
                        'initial': 'B1',
                        'states': {
                            'B1': {
                                'on': { 'TO_B2': 'B2' }
                            },
                            'B2': {
                                'on': { 'TO_B1': 'B1' }
                            }
                        }
                    }
                }
            }";


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}

