using Xunit;
using FluentAssertions;
using XStateNet;
using System.Collections.Generic;


namespace AdvancedFeatures
{
    public class ComplexStateTests : IDisposable
    {
        StateMachine? stateMachine = null;
        public ComplexStateTests()
        {
            stateMachine = StateMachine.CreateFromScript(script2, new ActionMap(), new GuardMap());
            stateMachine.Start();
        }

        [Fact]
        public void GetCurrentSubStatesTest1()
        {
            var currentState = stateMachine!.GetSourceSubStateCollection(null).ToCsvString(stateMachine, true);
            currentState.Should().Be("#fsm.A.A1.A1a;#fsm.A.A2");
        }

        [Fact]
        public void GetCurrentSubStatesTest2()
        {
            stateMachine!.Send("TO_A1b");

            var currentState = stateMachine!.GetSourceSubStateCollection(null).ToCsvString(stateMachine);
            currentState.Should().Be("#fsm.A.A1.A1b;#fsm.A.A2");
        }

        [Fact]
        public void GetCurrentSubStatesTest3()
        {
            stateMachine!.Send("TO_B");

            var currentState = stateMachine!.GetSourceSubStateCollection(null).ToCsvString(stateMachine);
            currentState.Should().Be("#fsm.B.B1");
        }



        static string script2 = @"
            {
                'id': 'fsm',
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
                                    'TO_B1': '#fsm.B.B1'
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

