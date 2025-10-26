using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;
using XStateNet2.Core.Factory;
using XStateNet2.Core.Messages;

namespace XStateNet2.Tests;

/// <summary>
/// Super complex state machine tests covering nested states, parallel states, and complex transitions
/// Tests a comprehensive state machine with multiple levels of nesting, parallel regions, and final states
/// </summary>
public class SuperComplexStateMachineTests : XStateTestKit
{

    private IActorRef CreateStateMachine()
    {
        var uniqueId = "test";
        var jsonScript = GetJsonScript(uniqueId);
        var factory = new XStateMachineFactory(Sys);
        return factory.FromJson(jsonScript).BuildAndStart();
    }

    #region Initialization and Startup Tests

    [Fact]
    public void TestInitialStartupState()
    {
        var stateMachine = CreateStateMachine();

        WaitForState(stateMachine, s => s.CurrentState.Contains("startup"), "startup state");
    }

    [Fact]
    public void TestTransitionToInitializing()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory") && s.CurrentState.Contains("awaitingInput"),
            "initializing parallel state with checkingMemory and awaitingInput");
    }

    #endregion

    #region Parallel State Transition Tests

    [Fact]
    public void TestParallelStateTransitionMemoryOk()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory state");

        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU") && s.CurrentState.Contains("awaitingInput"),
            "checkingCPU and awaitingInput");
    }

    [Fact]
    public void TestParallelStateTransitionCPUOk()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory state");

        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU state");

        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done") && s.CurrentState.Contains("awaitingInput"),
            "done and awaitingInput");
    }

    [Fact]
    public void TestParallelStateTransitionInputReceived()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("awaitingInput"),
            "awaitingInput state");

        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating state");
    }

    [Fact]
    public void TestParallelStateTransitionValidInput()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("awaitingInput"),
            "awaitingInput state");

        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating state");

        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("authenticated"),
            "authenticated state");
    }

    [Fact]
    public void TestParallelStateTransitionInvalidInput()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("awaitingInput"),
            "awaitingInput state");

        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating state");

        SendEventAndWait(stateMachine, "INVALID",
            s => s.CurrentState.Contains("awaitingInput"),
            "back to awaitingInput state");
    }

    #endregion

    #region Nested State Transition Tests

    [Fact]
    public void TestTransitionToProcessing()
    {
        var stateMachine = CreateStateMachine();

        // Complete initialization
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory state");

        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU state");

        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "systemCheck done");

        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating state");

        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready state");

        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection state");
    }

    [Fact]
    public void TestTaskASelectedStep1()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to ready state
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");

        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");

        SendEventAndWait(stateMachine, "TASK_A_SELECTED",
            s => s.CurrentState.Contains("step1"),
            "step1 state");
    }

    [Fact]
    public void TestTaskANextStep()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to task A
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");
        SendEventAndWait(stateMachine, "TASK_A_SELECTED",
            s => s.CurrentState.Contains("step1"),
            "step1");

        SendEventAndWait(stateMachine, "NEXT",
            s => s.CurrentState.Contains("step2"),
            "step2 state");
    }

    [Fact]
    public void TestTaskACompletion()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to task A and complete it
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");
        SendEventAndWait(stateMachine, "TASK_A_SELECTED",
            s => s.CurrentState.Contains("step1"),
            "step1");
        SendEventAndWait(stateMachine, "NEXT",
            s => s.CurrentState.Contains("step2"),
            "step2");
        SendEventAndWait(stateMachine, "NEXT",
            s => s.CurrentState.Contains("completed"),
            "completed");

        SendEventAndWait(stateMachine, "COMPLETE",
            s => s.CurrentState.Contains("ready"),
            "back to ready state");
    }

    [Fact]
    public void TestTaskBSelectedSubtask1()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to task B
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");

        SendEventAndWait(stateMachine, "TASK_B_SELECTED",
            s => s.CurrentState.Contains("subtask1"),
            "subtask1 state");
    }

    [Fact]
    public void TestTaskBSubtask2Parallel()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to task B subtask2
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");
        SendEventAndWait(stateMachine, "TASK_B_SELECTED",
            s => s.CurrentState.Contains("subtask1"),
            "subtask1");

        SendEventAndWait(stateMachine, "NEXT",
            s => s.CurrentState.Contains("parallelSubtaskA") && s.CurrentState.Contains("parallelSubtaskB"),
            "parallel subtasks A and B");
    }

    [Fact]
    public void TestTaskBSubtask2ParallelCompleteA()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to parallel subtasks and complete A
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");
        SendEventAndWait(stateMachine, "TASK_B_SELECTED",
            s => s.CurrentState.Contains("subtask1"),
            "subtask1");
        SendEventAndWait(stateMachine, "NEXT",
            s => s.CurrentState.Contains("parallelSubtaskA"),
            "parallelSubtaskA");

        SendEventAndWait(stateMachine, "COMPLETE_A",
            s => s.CurrentState.Contains("parallelSubtaskA") && s.CurrentState.Contains("parallelSubtaskB"),
            "still in parallel state with A completed");
    }

    [Fact]
    public void TestTaskBSubtask2ParallelCompleteB()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to parallel subtasks and complete B
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");
        SendEventAndWait(stateMachine, "TASK_B_SELECTED",
            s => s.CurrentState.Contains("subtask1"),
            "subtask1");
        SendEventAndWait(stateMachine, "NEXT",
            s => s.CurrentState.Contains("parallelSubtaskB"),
            "parallelSubtaskB");

        SendEventAndWait(stateMachine, "COMPLETE_B",
            s => s.CurrentState.Contains("parallelSubtaskB") && s.CurrentState.Contains("parallelSubtaskA"),
            "still in parallel state with B completed");
    }

    [Fact]
    public void TestTaskBCompletion()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to task B and complete it
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "START_PROCESS",
            s => s.CurrentState.Contains("taskSelection"),
            "taskSelection");
        SendEventAndWait(stateMachine, "TASK_B_SELECTED",
            s => s.CurrentState.Contains("subtask1"),
            "subtask1");
        SendEventAndWait(stateMachine, "NEXT",
            s => s.CurrentState.Contains("parallelSubtaskA"),
            "parallelSubtaskA");
        SendEventAndWait(stateMachine, "COMPLETE_A",
            s => s.CurrentState.Contains("parallelSubtaskA"),
            "parallelSubtaskA completed");
        SendEventAndWait(stateMachine, "COMPLETE_B",
            s => s.CurrentState.Contains("completed"),
            "both parallel tasks completed");

        SendEventAndWait(stateMachine, "COMPLETE",
            s => s.CurrentState.Contains("ready"),
            "back to ready state");
    }

    #endregion

    #region Final State Transition Tests

    [Fact]
    public void TestReadyToShuttingDown()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to ready state
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");

        SendEventAndWait(stateMachine, "SHUTDOWN",
            s => s.CurrentState.Contains("cleaningUp"),
            "cleaningUp state");
    }

    [Fact]
    public void TestCleaningUpToSavingState()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to shutdown
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "SHUTDOWN",
            s => s.CurrentState.Contains("cleaningUp"),
            "cleaningUp");

        SendEventAndWait(stateMachine, "CLEANUP_DONE",
            s => s.CurrentState.Contains("savingState"),
            "savingState state");
    }

    [Fact]
    public void TestSavingStateToDone()
    {
        var stateMachine = CreateStateMachine();

        // Navigate to saving state
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "SHUTDOWN",
            s => s.CurrentState.Contains("cleaningUp"),
            "cleaningUp");
        SendEventAndWait(stateMachine, "CLEANUP_DONE",
            s => s.CurrentState.Contains("savingState"),
            "savingState");

        SendEventAndWait(stateMachine, "SAVE_COMPLETE",
            s => s.CurrentState.Contains("done"),
            "done state");
    }

    [Fact]
    public void TestReady()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");

        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready state");
    }

    [Fact]
    public void TestFinalShutdown()
    {
        var stateMachine = CreateStateMachine();

        // Complete full shutdown sequence
        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");
        SendEventAndWait(stateMachine, "SHUTDOWN",
            s => s.CurrentState.Contains("cleaningUp"),
            "cleaningUp");
        SendEventAndWait(stateMachine, "CLEANUP_DONE",
            s => s.CurrentState.Contains("savingState"),
            "savingState");
        SendEventAndWait(stateMachine, "SAVE_COMPLETE",
            s => s.CurrentState.Contains("done"),
            "done");

        SendEventAndWait(stateMachine, "SHUTDOWN_CONFIRMED",
            s => s.CurrentState.Contains("shutdownComplete"),
            "shutdownComplete state");
    }

    #endregion

    #region Error Handling and Invalid Transitions

    [Fact]
    public void TestInvalidTransitionFromStartup()
    {
        var stateMachine = CreateStateMachine();

        // Send invalid event - should remain in startup
        stateMachine.Tell(new SendEvent("START_PROCESS"));

        WaitForState(stateMachine,
            s => s.CurrentState.Contains("startup"),
            "still in startup state");
    }

    [Fact]
    public void TestInvalidTransitionInParallelState()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");

        // Send invalid event - should remain in same state
        stateMachine.Tell(new SendEvent("INVALID_EVENT"));

        WaitForState(stateMachine,
            s => s.CurrentState.Contains("checkingMemory") && s.CurrentState.Contains("awaitingInput"),
            "still in parallel state");
    }

    [Fact]
    public void TestInvalidEventInReadyState()
    {
        var stateMachine = CreateStateMachine();

        SendEventAndWait(stateMachine, "INIT_COMPLETE",
            s => s.CurrentState.Contains("checkingMemory"),
            "checkingMemory");
        SendEventAndWait(stateMachine, "MEMORY_OK",
            s => s.CurrentState.Contains("checkingCPU"),
            "checkingCPU");
        SendEventAndWait(stateMachine, "CPU_OK",
            s => s.CurrentState.Contains("done"),
            "done");
        SendEventAndWait(stateMachine, "INPUT_RECEIVED",
            s => s.CurrentState.Contains("validating"),
            "validating");
        SendEventAndWait(stateMachine, "VALID",
            s => s.CurrentState.Contains("ready"),
            "ready");

        // Verify state is ready
        WaitForState(stateMachine,
            s => s.CurrentState.Contains("ready"),
            "ready state");
    }

    #endregion

    // JSON Script for the super complex state machine
    private string GetJsonScript(string uniqueId) => $$"""
        {
            "id": "{{uniqueId}}",
            "initial": "startup",
            "states": {
                "startup": {
                    "on": { "INIT_COMPLETE": "initializing" }
                },
                "initializing": {
                    "type": "parallel",
                    "states": {
                        "systemCheck": {
                            "initial": "checkingMemory",
                            "states": {
                                "checkingMemory": { "on": { "MEMORY_OK": "checkingCPU" } },
                                "checkingCPU": { "on": { "CPU_OK": "done" } },
                                "done": { "type": "final" }
                            }
                        },
                        "userAuth": {
                            "initial": "awaitingInput",
                            "states": {
                                "awaitingInput": { "on": { "INPUT_RECEIVED": "validating" } },
                                "validating": {
                                    "on": {
                                        "VALID": "authenticated",
                                        "INVALID": "awaitingInput"
                                    }
                                },
                                "authenticated": { "type": "final" }
                            }
                        }
                    },
                    "onDone": "ready"
                },
                "ready": {
                    "on": { "START_PROCESS": "processing", "SHUTDOWN": "shuttingDown" }
                },
                "processing": {
                    "initial": "taskSelection",
                    "states": {
                        "taskSelection": { "on": { "TASK_A_SELECTED": "taskA", "TASK_B_SELECTED": "taskB" } },
                        "taskA": {
                            "initial": "step1",
                            "states": {
                                "step1": { "on": { "NEXT": "step2" } },
                                "step2": { "on": { "NEXT": "completed" } },
                                "completed": {
                                    "type": "final",
                                    "on": { "COMPLETE": "#{{uniqueId}}.ready" }
                                }
                            }
                        },
                        "taskB": {
                            "initial": "subtask1",
                            "states": {
                                "subtask1": { "on": { "NEXT": "subtask2" } },
                                "subtask2": {
                                    "type": "parallel",
                                    "states": {
                                        "parallelSubtaskA": {
                                            "initial": "working",
                                            "states": {
                                                "working": { "on": { "COMPLETE_A": "completed" } },
                                                "completed": { "type": "final" }
                                            }
                                        },
                                        "parallelSubtaskB": {
                                            "initial": "working",
                                            "states": {
                                                "working": { "on": { "COMPLETE_B": "completed" } },
                                                "completed": { "type": "final" }
                                            }
                                        }
                                    },
                                    "onDone": "completed"
                                },
                                "completed": {
                                    "type": "final",
                                    "on": { "COMPLETE": "#{{uniqueId}}.ready" }
                                }
                            }
                        }
                    }
                },
                "shuttingDown": {
                    "initial": "cleaningUp",
                    "states": {
                        "cleaningUp": { "on": { "CLEANUP_DONE": "savingState" } },
                        "savingState": { "on": { "SAVE_COMPLETE": "done" } },
                        "done": {
                            "type": "final",
                            "on": { "SHUTDOWN_CONFIRMED": "#{{uniqueId}}.shutdownComplete" }
                        }
                    }
                },
                "shutdownComplete": { "type": "final" }
            }
        }
        """;
}
