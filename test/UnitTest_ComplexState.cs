using NUnit.Framework;
using XStateNet;
using System.Collections.Generic;


namespace AdvancedFeatures
{
    [TestFixture]
    public class ComplexStateTests
    {
        StateMachine stateMachine = null;
        [SetUp]
        public void Setup()
        {
            stateMachine = StateMachine.CreateFromScript(script2,
                new System.Collections.Concurrent.ConcurrentDictionary<string, List<NamedAction>>(),
                new System.Collections.Concurrent.ConcurrentDictionary<string, NamedGuard>()).Start();
        }

        [Test]
        public void GetCurrentSubStatesTest1()
        {
            var currentState = stateMachine.GetSourceSubStateCollection(null).ToCsvString();
            Assert.AreEqual("#fsm.A.A1.A1a;#fsm.A.A2", currentState);
        }

        [Test]
        public void GetCurrentSubStatesTest2()
        {
            stateMachine.Send("TO_A1b");

            var currentState = stateMachine.GetSourceSubStateCollection(null).ToCsvString();
            Assert.AreEqual("#fsm.A.A1.A1b;#fsm.A.A2", currentState);
        }

        [Test]
        public void GetCurrentSubStatesTest3()
        {
            stateMachine.Send("TO_B");

            var currentState = stateMachine.GetSourceSubStateCollection(null).ToCsvString();
            Assert.AreEqual("#fsm.B.B1", currentState);
        }

        string script1 =
        @"{
			'id': 'fsm',
			'initial': 'A',
			states : {         
				A :{
					type : 'parallel',
					on : { 
						'TO_B' : 'B'
					},
					states : 
					{
						A1 : {
							initial : 'A1a',
							on : { 
								'TO_B1' : '#fsm.B.B1'
							},
							states : {
								A1a : {
									on : { 'TO_A1b' : 'A1b' }
								},
								A1b : {
									on : { 'TO_A1a' : 'A1a' }
								}
							}
						},
						A2 : {}
					},
				}
				,
				B :{
					on : { 'TO_A' : 'A' },
					initial : 'B1',
					states : {
						B1 : {
							on : { 'TO_B2' : 'B2' },
						},
						B2 : {
							on : { 'TO_B1' : 'B1' },
						}
					}
				}
			}
		};";

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
    }
}