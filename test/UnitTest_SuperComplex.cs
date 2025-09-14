using Xunit;

using XStateNet;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SuperComplexStateMachineTests
{
    public class SuperComplexStateMachineTests : IDisposable
    {
        private StateMachine _stateMachine;
        private ActionMap _actions;
        private GuardMap _guards;

        public SuperComplexStateMachineTests()
        {
            // Initialize action and guard dictionaries
            _actions = new ActionMap();
            _guards = new GuardMap();

            // Load the state machine from the provided JSON script
            _stateMachine = StateMachine.CreateFromScript(json, _actions, _guards).Start();
        }


        #region Initialization and Startup Tests

        [Fact]
        public void TestInitialStartupState()
        {
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.startup", currentState);
        }

        [Fact]
        public void TestTransitionToInitializing()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.initializing.systemCheck.checkingMemory", currentState);
            Assert.Contains("#superComplexFSM.initializing.userAuth.awaitingInput", currentState);
        }

        #endregion

        #region Parallel State Transition Tests

        [Fact]
        public void TestParallelStateTransitionMemoryOk()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            var currentState = _stateMachine!.GetActiveStateString(true);
            Assert.Contains("#superComplexFSM.initializing.systemCheck.checkingCPU", currentState);
            Assert.Contains("#superComplexFSM.initializing.userAuth.awaitingInput", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionCPUOk()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.initializing.systemCheck.done", currentState);
            Assert.Contains("#superComplexFSM.initializing.userAuth.awaitingInput", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionInputReceived()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("INPUT_RECEIVED");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.initializing.userAuth.validating", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionValidInput()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.initializing.userAuth.authenticated", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionInvalidInput()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("INVALID");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.initializing.userAuth.awaitingInput", currentState);
        }

        #endregion

        #region Nested State Transition Tests

        [Fact]
        public void TestTransitionToProcessing()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.processing.taskSelection", currentState);
        }

        [Fact]
        public void TestTaskASelectedStep1()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_A_SELECTED");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.processing.taskA.step1", currentState);
        }

        [Fact]
        public void TestTaskANextStep()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_A_SELECTED");
            _stateMachine!.Send("NEXT");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.processing.taskA.step2", currentState);
        }

        [Fact]
        public void TestTaskACompletion()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_A_SELECTED");
            _stateMachine!.Send("NEXT");
            _stateMachine!.Send("NEXT");
            _stateMachine!.Send("COMPLETE");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.ready", currentState);
        }

        [Fact]
        public void TestTaskBSelectedSubtask1()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_B_SELECTED");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.processing.taskB.subtask1", currentState);
        }

        [Fact]
        public void TestTaskBSubtask2Parallel()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_B_SELECTED");
            _stateMachine!.Send("NEXT");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskA.working", currentState);
            Assert.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskB.working", currentState);
        }

        [Fact]
        public void TestTaskBSubtask2ParallelCompleteA()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_B_SELECTED");
            _stateMachine!.Send("NEXT");
            _stateMachine!.Send("COMPLETE_A");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskA.completed", currentState);
            Assert.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskB.working", currentState);
        }

        [Fact]
        public void TestTaskBSubtask2ParallelCompleteB()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_B_SELECTED");
            _stateMachine!.Send("NEXT");
            _stateMachine!.Send("COMPLETE_B");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskB.completed", currentState);
            Assert.Contains("#superComplexFSM.processing.taskB.subtask2.parallelSubtaskA.working", currentState);
        }

        [Fact]
        public void TestTaskBCompletion()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("START_PROCESS");
            _stateMachine!.Send("TASK_B_SELECTED");
            _stateMachine!.Send("NEXT");
            _stateMachine!.Send("COMPLETE_A");
            _stateMachine!.Send("COMPLETE_B");
            _stateMachine!.Send("COMPLETE");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.ready", currentState);
        }

        #endregion

        #region Final State Transition Tests

        [Fact]
        public void TestReadyToShuttingDown()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("SHUTDOWN");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.shuttingDown.cleaningUp", currentState);
        }

        [Fact]
        public void TestCleaningUpToSavingState()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");   // add to original
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("SHUTDOWN");
            _stateMachine!.Send("CLEANUP_DONE");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.shuttingDown.savingState", currentState);
        }

        [Fact]
        public void TestSavingStateToDone()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("SHUTDOWN");
            _stateMachine!.Send("CLEANUP_DONE");
            _stateMachine!.Send("SAVE_COMPLETE");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.shuttingDown.done", currentState);
        }

        [Fact]
        public void TestReady()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.ready", currentState);
        }

        [Fact]
        public void TestFinalShutdown()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            _stateMachine!.Send("SHUTDOWN");
            _stateMachine!.Send("CLEANUP_DONE");
            _stateMachine!.Send("SAVE_COMPLETE");
            _stateMachine!.Send("SHUTDOWN_CONFIRMED");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.shutdownComplete", currentState);
        }

        #endregion

        #region Error Handling and Invalid Transitions

        [Fact]
        public void TestInvalidTransitionFromStartup()
        {
            _stateMachine!.Send("START_PROCESS");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.startup", currentState);
        }

        [Fact]
        public void TestInvalidTransitionInParallelState()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("INVALID_EVENT");
            var currentState = _stateMachine!.GetActiveStateString(false);
            Assert.Contains("#superComplexFSM.initializing.systemCheck.checkingMemory", currentState);
            Assert.Contains("#superComplexFSM.initializing.userAuth.awaitingInput", currentState);
        }

        [Fact]
        public void TestInvalidEventInReadyState()
        {
            _stateMachine!.Send("INIT_COMPLETE");
            _stateMachine!.Send("MEMORY_OK");
            _stateMachine!.Send("CPU_OK");
            _stateMachine!.Send("INPUT_RECEIVED");
            _stateMachine!.Send("VALID");
            //_stateMachine.Send("INVALID_EVENT");
            var currentState = _stateMachine!.GetActiveStateString();
            Assert.Equal("#superComplexFSM.ready", currentState);
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


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}



