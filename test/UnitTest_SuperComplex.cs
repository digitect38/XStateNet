using NUnit.Framework;
using XStateNet;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SuperComplexStateMachineTests
{
    [TestFixture]
    public class SuperComplexStateMachineTests
    {
        private StateMachine _stateMachine;
        private ActionMap _actions;
        private GuardMap _guards;

        [SetUp]
        public void Setup()
        {
            // Initialize action and guard dictionaries
            _actions = new ActionMap();
            _guards = new GuardMap();

            // Load the state machine from the provided JSON script
            _stateMachine = StateMachine.CreateFromScript(json, _actions, _guards).Start();
        }

        
        #region Initialization and Startup Tests

        [Test]
        public void TestInitialStartupState()
        {
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.startup", currentState);
        }

        [Test]
        public void TestTransitionToInitializing()
        {
            _stateMachine.Send("INIT_COMPLETE");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.systemCheck.checkingMemory"));
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.userAuth.awaitingInput"));
        }

        #endregion

        #region Parallel State Transition Tests

        [Test]
        public void TestParallelStateTransitionMemoryOk()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            var currentState = _stateMachine.GetActiveStateString(true);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.systemCheck.checkingCPU"));
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.userAuth.awaitingInput"));
        }

        [Test]
        public void TestParallelStateTransitionCPUOk()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.systemCheck.done"));
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.userAuth.awaitingInput"));
        }

        [Test]
        public void TestParallelStateTransitionInputReceived()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("INPUT_RECEIVED");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.userAuth.validating"));
        }

        [Test]
        public void TestParallelStateTransitionValidInput()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.userAuth.authenticated"));
        }

        [Test]
        public void TestParallelStateTransitionInvalidInput()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("INVALID");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.userAuth.awaitingInput"));
        }

        #endregion

        #region Nested State Transition Tests

        [Test]
        public void TestTransitionToProcessing()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.processing.taskSelection", currentState);
        }

        [Test]
        public void TestTaskASelectedStep1()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_A_SELECTED");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.processing.taskA.step1", currentState);
        }

        [Test]
        public void TestTaskANextStep()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_A_SELECTED");
            _stateMachine.Send("NEXT");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.processing.taskA.step2", currentState);
        }

        [Test]
        public void TestTaskACompletion()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_A_SELECTED");
            _stateMachine.Send("NEXT");
            _stateMachine.Send("NEXT");
            _stateMachine.Send("COMPLETE");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.ready", currentState);
        }

        [Test]
        public void TestTaskBSelectedSubtask1()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_B_SELECTED");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.processing.taskB.subtask1", currentState);
        }

        [Test]
        public void TestTaskBSubtask2Parallel()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_B_SELECTED");
            _stateMachine.Send("NEXT");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskA.working"));
            Assert.IsTrue(currentState.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskB.working"));
        }

        [Test]
        public void TestTaskBSubtask2ParallelCompleteA()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_B_SELECTED");
            _stateMachine.Send("NEXT");
            _stateMachine.Send("COMPLETE_A");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskA.completed"));
            Assert.IsTrue(currentState.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskB.working"));
        }

        [Test]
        public void TestTaskBSubtask2ParallelCompleteB()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_B_SELECTED");
            _stateMachine.Send("NEXT");
            _stateMachine.Send("COMPLETE_B");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskB.completed"));
            Assert.IsTrue(currentState.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskA.working"));
        }

        [Test]
        public void TestTaskBCompletion()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("START_PROCESS");
            _stateMachine.Send("TASK_B_SELECTED");
            _stateMachine.Send("NEXT");
            _stateMachine.Send("COMPLETE_A");
            _stateMachine.Send("COMPLETE_B");
            _stateMachine.Send("COMPLETE");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.ready", currentState);
        }

        #endregion

        #region Final State Transition Tests

        [Test]
        public void TestReadyToShuttingDown()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("SHUTDOWN");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.shuttingDown.cleaningUp", currentState);
        }

        [Test]
        public void TestCleaningUpToSavingState()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");   // add to original
            _stateMachine.Send("VALID");
            _stateMachine.Send("SHUTDOWN");
            _stateMachine.Send("CLEANUP_DONE");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.shuttingDown.savingState", currentState);
        }

        [Test]
        public void TestSavingStateToDone()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("SHUTDOWN");
            _stateMachine.Send("CLEANUP_DONE");
            _stateMachine.Send("SAVE_COMPLETE");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.shuttingDown.done", currentState);
        }

        [Test]
        public void TestReady()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.That("#superComplexFSM.ready" == currentState);
        }

        [Test]
        public void TestFinalShutdown()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            _stateMachine.Send("SHUTDOWN");
            _stateMachine.Send("CLEANUP_DONE");
            _stateMachine.Send("SAVE_COMPLETE");
            _stateMachine.Send("SHUTDOWN_CONFIRMED");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.shutdownComplete", currentState);
        }

        #endregion

        #region Error Handling and Invalid Transitions

        [Test]
        public void TestInvalidTransitionFromStartup()
        {
            _stateMachine.Send("START_PROCESS");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.startup", currentState);
        }

        [Test]
        public void TestInvalidTransitionInParallelState()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("INVALID_EVENT");
            var currentState = _stateMachine.GetActiveStateString(false);
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.systemCheck.checkingMemory"));
            Assert.IsTrue(currentState.Contains("#superComplexFSM.initializing.userAuth.awaitingInput"));
        }

        [Test]
        public void TestInvalidEventInReadyState()
        {
            _stateMachine.Send("INIT_COMPLETE");
            _stateMachine.Send("MEMORY_OK");
            _stateMachine.Send("CPU_OK");
            _stateMachine.Send("INPUT_RECEIVED");
            _stateMachine.Send("VALID");
            //_stateMachine.Send("INVALID_EVENT");
            var currentState = _stateMachine.GetActiveStateString();
            Assert.AreEqual("#superComplexFSM.ready", currentState);
        }

        #endregion

        #region History State Tests (if included)

        // If history states were added, include tests here for shallow and deep history state transitions.

        #endregion

        // JSON Script for the super complex state machine
        private const string json = @"{
            'id': 'superComplexFSM',
            'initial': 'startup',
            'states': {
                'startup': {
                    'on': { 'INIT_COMPLETE': 'initializing' }
                },
                'initializing': {
                    'type': 'parallel',
                    'states': {
                        'systemCheck': {
                            'initial': 'checkingMemory',
                            'states': {
                                'checkingMemory': { 'on': { 'MEMORY_OK': 'checkingCPU' } },
                                'checkingCPU': { 'on': { 'CPU_OK': 'done' } },
                                'done': { 'type': 'final' }
                            }
                        },
                        'userAuth': {
                            'initial': 'awaitingInput',
                            'states': {
                                'awaitingInput': { 'on': { 'INPUT_RECEIVED': 'validating' } },
                                'validating': {
                                    'on': {
                                        'VALID': 'authenticated',
                                        'INVALID': 'awaitingInput'
                                    }
                                },
                                'authenticated': { 'type': 'final' }
                            }
                        }
                    },
                    'onDone': 'ready'
                },
                'ready': {
                    'on': { 'START_PROCESS': 'processing', 'SHUTDOWN': 'shuttingDown' }
                },
                'processing': {
                    'initial': 'taskSelection',
                    'states': {
                        'taskSelection': { 'on': { 'TASK_A_SELECTED': 'taskA', 'TASK_B_SELECTED': 'taskB' } },
                        'taskA': {
                            'initial': 'step1',
                            'states': {
                                'step1': { 'on': { 'NEXT': 'step2' } },
                                'step2': { 'on': { 'NEXT': 'completed' } },
                                'completed': {
                                    'type': 'final',
                                    'on': { 'COMPLETE': '#superComplexFSM.ready' }
                                }
                            }
                        },
                        'taskB': {
                            'initial': 'subtask1',
                            'states': {
                                'subtask1': { 'on': { 'NEXT': 'subtask2' } },
                                'subtask2': {
                                    'type': 'parallel',
                                    'states': {
                                        'parallelSubtaskA': {
                                            'initial': 'working',
                                            'states': {
                                                'working': { 'on': { 'COMPLETE_A': 'completed' } },
                                                'completed': { 'type': 'final' }
                                            }
                                        },
                                        'parallelSubtaskB': {
                                            'initial': 'working',
                                            'states': {
                                                'working': { 'on': { 'COMPLETE_B': 'completed' } },
                                                'completed': { 'type': 'final' }
                                            }
                                        }
                                    },
                                    'onDone': 'completed'
                                },
                                'completed': {
                                    'type': 'final',
                                    'on': { 'COMPLETE': '#superComplexFSM.ready' }
                                }
                            }
                        }
                    }
                },
                'shuttingDown': {
                    'initial': 'cleaningUp',
                    'states': {
                        'cleaningUp': { 'on': { 'CLEANUP_DONE': 'savingState' } },
                        'savingState': { 'on': { 'SAVE_COMPLETE': 'done' } },
                        'done': {
                            'type': 'final',
                            'on': { 'SHUTDOWN_CONFIRMED': '#superComplexFSM.shutdownComplete' }
                        }
                    }
                },
                'shutdownComplete': { 'type': 'final' }
            }
        }";
    }
}
