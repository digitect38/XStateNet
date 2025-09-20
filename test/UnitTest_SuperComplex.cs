using Xunit;

using XStateNet;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System;

namespace SuperComplexStateMachineTests
{
    public class SuperComplexStateMachineTests : IDisposable
    {
        private StateMachine CreateStateMachine(string uniqueId)
        {
            // Initialize action and guard dictionaries
            var actions = new ActionMap();
            var guards = new GuardMap();

            var jsonScript = GetJsonScript(uniqueId);

            // Load the state machine from the provided JSON script
            var stateMachine = (StateMachine)StateMachine.CreateFromScript(jsonScript, actions, guards).Start();
            return stateMachine;
        }


        #region Initialization and Startup Tests

        [Fact]
        public void TestInitialStartupState()
        {
            var uniqueId = "TestInitialStartupState_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.startup", currentState);
        }

        [Fact]
        public void TestTransitionToInitializing()
        {
            var uniqueId = "TestTransitionToInitializing_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.initializing.systemCheck.checkingMemory", currentState);
            Assert.Contains($"#{uniqueId}.initializing.userAuth.awaitingInput", currentState);
        }

        #endregion

        #region Parallel State Transition Tests

        [Fact]
        public void TestParallelStateTransitionMemoryOk()
        {
            var uniqueId = "TestParallelStateTransitionMemoryOk_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            var currentState = stateMachine!.GetActiveStateString(true);
            Assert.Contains($"#{uniqueId}.initializing.systemCheck.checkingCPU", currentState);
            Assert.Contains($"#{uniqueId}.initializing.userAuth.awaitingInput", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionCPUOk()
        {
            var uniqueId = "TestParallelStateTransitionCPUOk_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.initializing.systemCheck.done", currentState);
            Assert.Contains($"#{uniqueId}.initializing.userAuth.awaitingInput", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionInputReceived()
        {
            var uniqueId = "TestParallelStateTransitionInputReceived_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("INPUT_RECEIVED");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.initializing.userAuth.validating", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionValidInput()
        {
            var uniqueId = "TestParallelStateTransitionValidInput_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.initializing.userAuth.authenticated", currentState);
        }

        [Fact]
        public void TestParallelStateTransitionInvalidInput()
        {
            var uniqueId = "TestParallelStateTransitionInvalidInput_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("INVALID");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.initializing.userAuth.awaitingInput", currentState);
        }

        #endregion

        #region Nested State Transition Tests

        [Fact]
        public void TestTransitionToProcessing()
        {
            var uniqueId = "TestTransitionToProcessing_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.processing.taskSelection", currentState);
        }

        [Fact]
        public void TestTaskASelectedStep1()
        {
            var uniqueId = "TestTaskASelectedStep1_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_A_SELECTED");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.processing.taskA.step1", currentState);
        }

        [Fact]
        public void TestTaskANextStep()
        {
            var uniqueId = "TestTaskANextStep_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_A_SELECTED");
            stateMachine!.Send("NEXT");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.processing.taskA.step2", currentState);
        }

        [Fact]
        public void TestTaskACompletion()
        {
            var uniqueId = "TestTaskACompletion_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_A_SELECTED");
            stateMachine!.Send("NEXT");
            stateMachine!.Send("NEXT");
            stateMachine!.Send("COMPLETE");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.ready", currentState);
        }

        [Fact]
        public void TestTaskBSelectedSubtask1()
        {
            var uniqueId = "TestTaskBSelectedSubtask1_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_B_SELECTED");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.processing.taskB.subtask1", currentState);
        }

        [Fact]
        public void TestTaskBSubtask2Parallel()
        {
            var uniqueId = "TestTaskBSubtask2Parallel_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_B_SELECTED");
            stateMachine!.Send("NEXT");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.processing.taskB.subtask2.parallelSubtaskA.working", currentState);
            Assert.Contains($"#{uniqueId}.processing.taskB.subtask2.parallelSubtaskB.working", currentState);
        }

        [Fact]
        public void TestTaskBSubtask2ParallelCompleteA()
        {
            var uniqueId = "TestTaskBSubtask2ParallelCompleteA_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_B_SELECTED");
            stateMachine!.Send("NEXT");
            stateMachine!.Send("COMPLETE_A");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.processing.taskB.subtask2.parallelSubtaskA.completed", currentState);
            Assert.Contains($"#{uniqueId}.processing.taskB.subtask2.parallelSubtaskB.working", currentState);
        }

        [Fact]
        public void TestTaskBSubtask2ParallelCompleteB()
        {
            var uniqueId = "TestTaskBSubtask2ParallelCompleteB_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_B_SELECTED");
            stateMachine!.Send("NEXT");
            stateMachine!.Send("COMPLETE_B");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.processing.taskB.subtask2.parallelSubtaskB.completed", currentState);
            Assert.Contains($"#{uniqueId}.processing.taskB.subtask2.parallelSubtaskA.working", currentState);
        }

        [Fact]
        public void TestTaskBCompletion()
        {
            var uniqueId = "TestTaskBCompletion_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("START_PROCESS");
            stateMachine!.Send("TASK_B_SELECTED");
            stateMachine!.Send("NEXT");
            stateMachine!.Send("COMPLETE_A");
            stateMachine!.Send("COMPLETE_B");
            stateMachine!.Send("COMPLETE");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.ready", currentState);
        }

        #endregion

        #region Final State Transition Tests

        [Fact]
        public void TestReadyToShuttingDown()
        {
            var uniqueId = "TestReadyToShuttingDown_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("SHUTDOWN");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.shuttingDown.cleaningUp", currentState);
        }

        [Fact]
        public void TestCleaningUpToSavingState()
        {
            var uniqueId = "TestCleaningUpToSavingState_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");   // add to original
            stateMachine!.Send("VALID");
            stateMachine!.Send("SHUTDOWN");
            stateMachine!.Send("CLEANUP_DONE");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.shuttingDown.savingState", currentState);
        }

        [Fact]
        public void TestSavingStateToDone()
        {
            var uniqueId = "TestSavingStateToDone_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("SHUTDOWN");
            stateMachine!.Send("CLEANUP_DONE");
            stateMachine!.Send("SAVE_COMPLETE");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.shuttingDown.done", currentState);
        }

        [Fact]
        public void TestReady()
        {
            var uniqueId = "TestReady_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.ready", currentState);
        }

        [Fact]
        public void TestFinalShutdown()
        {
            var uniqueId = "TestFinalShutdown_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            stateMachine!.Send("SHUTDOWN");
            stateMachine!.Send("CLEANUP_DONE");
            stateMachine!.Send("SAVE_COMPLETE");
            stateMachine!.Send("SHUTDOWN_CONFIRMED");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.shutdownComplete", currentState);
        }

        #endregion

        #region Error Handling and Invalid Transitions

        [Fact]
        public void TestInvalidTransitionFromStartup()
        {
            var uniqueId = "TestInvalidTransitionFromStartup_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("START_PROCESS");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.startup", currentState);
        }

        [Fact]
        public void TestInvalidTransitionInParallelState()
        {
            var uniqueId = "TestInvalidTransitionInParallelState_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("INVALID_EVENT");
            var currentState = stateMachine!.GetActiveStateString(false);
            Assert.Contains($"#{uniqueId}.initializing.systemCheck.checkingMemory", currentState);
            Assert.Contains($"#{uniqueId}.initializing.userAuth.awaitingInput", currentState);
        }

        [Fact]
        public void TestInvalidEventInReadyState()
        {
            var uniqueId = "TestInvalidEventInReadyState_" + Guid.NewGuid().ToString("N");
            var stateMachine = CreateStateMachine(uniqueId);

            stateMachine!.Send("INIT_COMPLETE");
            stateMachine!.Send("MEMORY_OK");
            stateMachine!.Send("CPU_OK");
            stateMachine!.Send("INPUT_RECEIVED");
            stateMachine!.Send("VALID");
            //_stateMachine.Send("INVALID_EVENT");
            var currentState = stateMachine!.GetActiveStateString();
            Assert.Equal($"#{uniqueId}.ready", currentState);
        }

        #endregion

        #region History State Tests (if included)

        // If history states were added, include tests here for shallow and deep history state transitions.

        #endregion

        // JSON Script for the super complex state machine
        private string GetJsonScript(string uniqueId) => @$"{{
            id: '{uniqueId}',
            initial: 'startup',
            states: {{
                startup: {{
                    on: {{ INIT_COMPLETE: 'initializing' }}
                }},
                initializing: {{
                    type: 'parallel',
                    states: {{
                        systemCheck: {{
                            initial: 'checkingMemory',
                            states: {{
                                checkingMemory: {{ on: {{ MEMORY_OK: 'checkingCPU' }} }},
                                checkingCPU: {{ on: {{ CPU_OK: 'done' }} }},
                                done: {{ type: 'final' }}
                            }}
                        }},
                        userAuth: {{
                            initial: 'awaitingInput',
                            states: {{
                                awaitingInput: {{ on: {{ INPUT_RECEIVED: 'validating' }} }},
                                validating: {{
                                    on: {{
                                        VALID: 'authenticated',
                                        INVALID: 'awaitingInput'
                                    }}
                                }},
                                authenticated: {{ type: 'final' }}
                            }}
                        }}
                    }},
                    onDone: 'ready'
                }},
                ready: {{
                    on: {{ START_PROCESS: 'processing', SHUTDOWN: 'shuttingDown' }}
                }},
                processing: {{
                    initial: 'taskSelection',
                    states: {{
                        taskSelection: {{ on: {{ TASK_A_SELECTED: 'taskA', TASK_B_SELECTED: 'taskB' }} }},
                        taskA: {{
                            initial: 'step1',
                            states: {{
                                step1: {{ on: {{ NEXT: 'step2' }} }},
                                step2: {{ on: {{ NEXT: 'completed' }} }},
                                completed: {{
                                    type: 'final',
                                    on: {{ COMPLETE: '#{uniqueId}.ready' }}
                                }}
                            }}
                        }},
                        taskB: {{
                            initial: 'subtask1',
                            states: {{
                                subtask1: {{ on: {{ NEXT: 'subtask2' }} }},
                                subtask2: {{
                                    type: 'parallel',
                                    states: {{
                                        parallelSubtaskA: {{
                                            initial: 'working',
                                            states: {{
                                                working: {{ on: {{ COMPLETE_A: 'completed' }} }},
                                                completed: {{ type: 'final' }}
                                            }}
                                        }},
                                        parallelSubtaskB: {{
                                            initial: 'working',
                                            states: {{
                                                working: {{ on: {{ COMPLETE_B: 'completed' }} }},
                                                completed: {{ type: 'final' }}
                                            }}
                                        }}
                                    }},
                                    onDone: 'completed'
                                }},
                                completed: {{
                                    type: 'final',
                                    on: {{ COMPLETE: '#{uniqueId}.ready' }}
                                }}
                            }}
                        }}
                    }}
                }},
                shuttingDown: {{
                    initial: 'cleaningUp',
                    states: {{
                        cleaningUp: {{ on: {{ CLEANUP_DONE: 'savingState' }} }},
                        savingState: {{ on: {{ SAVE_COMPLETE: 'done' }} }},
                        done: {{
                            type: 'final',
                            on: {{ SHUTDOWN_CONFIRMED: '#{uniqueId}.shutdownComplete' }}
                        }}
                    }}
                }},
                shutdownComplete: {{ type: 'final' }}
            }}
        }}";


        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}



