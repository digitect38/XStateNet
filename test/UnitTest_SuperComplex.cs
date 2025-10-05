using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;
using Xunit;

namespace SuperComplexStateMachineTests
{
    public class SuperComplexStateMachineTests : OrchestratorTestBase
    {
        private IPureStateMachine? _currentMachine;

        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        private async Task<IPureStateMachine> CreateStateMachine(string uniqueId)
        {
            var jsonScript = GetJsonScript(uniqueId);
            var actions = new Dictionary<string, Action<OrchestratedContext>>();
            var guards = new Dictionary<string, Func<StateMachine, bool>>();

            // Load the state machine using orchestration pattern
            _currentMachine = CreateMachine(uniqueId, jsonScript, actions, guards);
            await _currentMachine.StartAsync();
            return _currentMachine;
        }


        #region Initialization and Startup Tests

        [Fact]
        public async Task TestInitialStartupState()
        {
            var uniqueId = "TestInitialStartupState_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("startup", currentState);
        }

        [Fact]
        public async Task TestTransitionToInitializing()
        {
            var uniqueId = "TestTransitionToInitializing_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");

            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("checkingMemory", currentState);
            Assert.Contains("awaitingInput", currentState);
        }

        #endregion

        #region Parallel State Transition Tests

        [Fact]
        public async Task TestParallelStateTransitionMemoryOk()
        {
            var uniqueId = "TestParallelStateTransitionMemoryOk_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("checkingCPU", currentState);
            Assert.Contains("awaitingInput", currentState);
        }

        [Fact]
        public async Task TestParallelStateTransitionCPUOk()
        {
            var uniqueId = "TestParallelStateTransitionCPUOk_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("done", currentState);
            Assert.Contains("awaitingInput", currentState);
        }

        [Fact]
        public async Task TestParallelStateTransitionInputReceived()
        {
            var uniqueId = "TestParallelStateTransitionInputReceived_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("validating", currentState);
        }

        [Fact]
        public async Task TestParallelStateTransitionValidInput()
        {
            var uniqueId = "TestParallelStateTransitionValidInput_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("authenticated", currentState);
        }

        [Fact]
        public async Task TestParallelStateTransitionInvalidInput()
        {
            var uniqueId = "TestParallelStateTransitionInvalidInput_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "INVALID");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("awaitingInput", currentState);
        }

        #endregion

        #region Nested State Transition Tests

        [Fact]
        public async Task TestTransitionToProcessing()
        {
            var uniqueId = "TestTransitionToProcessing_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("taskSelection", currentState);
        }

        [Fact]
        public async Task TestTaskASelectedStep1()
        {
            var uniqueId = "TestTaskASelectedStep1_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_A_SELECTED");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("step1", currentState);
        }

        [Fact]
        public async Task TestTaskANextStep()
        {
            var uniqueId = "TestTaskANextStep_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_A_SELECTED");
            await SendEventAsync("TEST", stateMachine.Id, "NEXT");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("step2", currentState);
        }

        [Fact]
        public async Task TestTaskACompletion()
        {
            var uniqueId = "TestTaskACompletion_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_A_SELECTED");
            await SendEventAsync("TEST", stateMachine.Id, "NEXT");
            await SendEventAsync("TEST", stateMachine.Id, "NEXT");
            await SendEventAsync("TEST", stateMachine.Id, "COMPLETE");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("ready", currentState);
        }

        [Fact]
        public async Task TestTaskBSelectedSubtask1()
        {
            var uniqueId = "TestTaskBSelectedSubtask1_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_B_SELECTED");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("subtask1", currentState);
        }

        [Fact]
        public async Task TestTaskBSubtask2Parallel()
        {
            var uniqueId = "TestTaskBSubtask2Parallel_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_B_SELECTED");
            await SendEventAsync("TEST", stateMachine.Id, "NEXT");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("parallelSubtaskA", currentState);
            Assert.Contains("parallelSubtaskB", currentState);
        }

        [Fact]
        public async Task TestTaskBSubtask2ParallelCompleteA()
        {
            var uniqueId = "TestTaskBSubtask2ParallelCompleteA_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_B_SELECTED");
            await SendEventAsync("TEST", stateMachine.Id, "NEXT");
            await SendEventAsync("TEST", stateMachine.Id, "COMPLETE_A");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("parallelSubtaskA", currentState);
            Assert.Contains("parallelSubtaskB", currentState);
        }

        [Fact]
        public async Task TestTaskBSubtask2ParallelCompleteB()
        {
            var uniqueId = "TestTaskBSubtask2ParallelCompleteB_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_B_SELECTED");
            await SendEventAsync("TEST", stateMachine.Id, "NEXT");
            await SendEventAsync("TEST", stateMachine.Id, "COMPLETE_B");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("parallelSubtaskB", currentState);
            Assert.Contains("parallelSubtaskA", currentState);
        }

        [Fact]
        public async Task TestTaskBCompletion()
        {
            var uniqueId = "TestTaskBCompletion_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await SendEventAsync("TEST", stateMachine.Id, "TASK_B_SELECTED");
            await SendEventAsync("TEST", stateMachine.Id, "NEXT");
            await SendEventAsync("TEST", stateMachine.Id, "COMPLETE_A");
            await SendEventAsync("TEST", stateMachine.Id, "COMPLETE_B");
            await SendEventAsync("TEST", stateMachine.Id, "COMPLETE");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("ready", currentState);
        }

        #endregion

        #region Final State Transition Tests

        [Fact]
        public async Task TestReadyToShuttingDown()
        {
            var uniqueId = "TestReadyToShuttingDown_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "SHUTDOWN");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("cleaningUp", currentState);
        }

        [Fact]
        public async Task TestCleaningUpToSavingState()
        {
            var uniqueId = "TestCleaningUpToSavingState_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "SHUTDOWN");
            await SendEventAsync("TEST", stateMachine.Id, "CLEANUP_DONE");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("savingState", currentState);
        }

        [Fact]
        public async Task TestSavingStateToDone()
        {
            var uniqueId = "TestSavingStateToDone_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "SHUTDOWN");
            await SendEventAsync("TEST", stateMachine.Id, "CLEANUP_DONE");
            await SendEventAsync("TEST", stateMachine.Id, "SAVE_COMPLETE");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("done", currentState);
        }

        [Fact]
        public async Task TestReady()
        {
            var uniqueId = "TestReady_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;

            Assert.Contains("ready", currentState);
        }

        [Fact]
        public async Task TestFinalShutdown()
        {
            var uniqueId = "TestFinalShutdown_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await SendEventAsync("TEST", stateMachine.Id, "SHUTDOWN");
            await SendEventAsync("TEST", stateMachine.Id, "CLEANUP_DONE");
            await SendEventAsync("TEST", stateMachine.Id, "SAVE_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "SHUTDOWN_CONFIRMED");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;

            Assert.Contains("shutdownComplete", currentState);
        }

        #endregion

        #region Error Handling and Invalid Transitions

        [Fact]
        public async Task TestInvalidTransitionFromStartup()
        {
            var uniqueId = "TestInvalidTransitionFromStartup_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "START_PROCESS");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("startup", currentState);
        }

        [Fact]
        public async Task TestInvalidTransitionInParallelState()
        {
            var uniqueId = "TestInvalidTransitionInParallelState_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "INVALID_EVENT");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("checkingMemory", currentState);
            Assert.Contains("awaitingInput", currentState);
        }

        [Fact]
        public async Task TestInvalidEventInReadyState()
        {
            var uniqueId = "TestInvalidEventInReadyState_" + Guid.NewGuid().ToString("N");
            var stateMachine = await CreateStateMachine(uniqueId);

            await SendEventAsync("TEST", stateMachine.Id, "INIT_COMPLETE");
            await SendEventAsync("TEST", stateMachine.Id, "MEMORY_OK");
            await SendEventAsync("TEST", stateMachine.Id, "CPU_OK");
            await SendEventAsync("TEST", stateMachine.Id, "INPUT_RECEIVED");
            await SendEventAsync("TEST", stateMachine.Id, "VALID");
            await Task.Delay(100);
            var currentState = stateMachine.CurrentState;
            Assert.Contains("ready", currentState);
        }

        #endregion

        #region History State Tests (if included)

        // If history states were added, include tests here for shallow and deep history state transitions.

        #endregion

        // JSON Script for the super complex state machine
        private string GetJsonScript(string uniqueId) => $@"{{
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
    }
}



