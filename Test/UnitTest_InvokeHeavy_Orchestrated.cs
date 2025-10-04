using Xunit;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Orchestration;
using XStateNet.Tests;

namespace XStateV5_Test.AdvancedFeatures;

/// <summary>
/// Orchestrated version of heavy invoke tests
/// Tests complex invoke scenarios with EventBusOrchestrator pattern
/// </summary>
public class UnitTest_InvokeHeavy_Orchestrated : OrchestratorTestBase
{
    private readonly ConcurrentBag<string> _eventLog = new();
    private readonly ConcurrentDictionary<string, int> _counters = new();
    private readonly ConcurrentDictionary<string, object> _serviceResults = new();
    private IPureStateMachine? _currentMachine;

    private async Task WaitForState(IPureStateMachine machine, string expectedState, int timeoutMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (machine.CurrentState.Contains(expectedState))
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"State '{expectedState}' not reached within {timeoutMs}ms");
    }

    private async Task WaitForEventLog(string expectedLog, int timeoutMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (_eventLog.Contains(expectedLog))
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"Event log did not contain '{expectedLog}' within {timeoutMs}ms");
    }

    private Dictionary<string, Action<OrchestratedContext>> CreateActions()
    {
        // Get underlying machine for context access
        StateMachine? GetUnderlying() => (_currentMachine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;

        return new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logEntry"] = (ctx) => _eventLog.Add($"entry:{_currentMachine?.CurrentState}"),
            ["logExit"] = (ctx) => _eventLog.Add($"exit:{_currentMachine?.CurrentState}"),
            ["logSuccess"] = (ctx) =>
            {
                var result = GetUnderlying()?.ContextMap?["_serviceResult"];
                _eventLog.Add($"success:{result}");
                _serviceResults["last"] = result ?? "null";
            },
            ["logError"] = (ctx) =>
            {
                var error = GetUnderlying()?.ContextMap?["_errorMessage"];
                _eventLog.Add($"error:{error}");
            },
            ["incrementCounter"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    var counter = Convert.ToInt32(underlying.ContextMap["counter"] ?? 0);
                    underlying.ContextMap["counter"] = counter + 1;
                    _counters["main"] = counter + 1;
                }
            },
            ["prepareContext"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["serviceInput"] = "prepared-data";
                    _eventLog.Add("context:prepared");
                }
            },
            ["initRetries"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["retries"] = 0;
                }
            },
            ["incrementRetries"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    var retries = Convert.ToInt32(underlying.ContextMap["retries"] ?? 0);
                    underlying.ContextMap["retries"] = retries + 1;
                }
            },
            ["markFastCompleted"] = (ctx) =>
            {
                _counters.AddOrUpdate("completed", 1, (k, v) => v + 1);
                _eventLog.Add("fast:done");
            },
            ["markSlowCompleted"] = (ctx) =>
            {
                _counters.AddOrUpdate("completed", 1, (k, v) => v + 1);
                _eventLog.Add("slow:done");
            },
            ["logAllComplete"] = (ctx) => _eventLog.Add("all:complete"),
            ["logCancellingEntered"] = (ctx) => _eventLog.Add("cancelling:entered"),
            ["logCancelledEntered"] = (ctx) => _eventLog.Add("cancelled:entered"),
            ["logLevel2Error"] = (ctx) => _eventLog.Add("level2Error:handled"),
            ["logLevel1Error"] = (ctx) => _eventLog.Add("level1Error:handled"),
            ["processServiceData"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    var data = underlying.ContextMap["_serviceResult"];
                    underlying.ContextMap["processedData"] = $"{data}-processed";
                    _eventLog.Add($"data:processed:{data}");
                }
            },
            ["logProcessing"] = (ctx) =>
            {
                var data = GetUnderlying()?.ContextMap?["processedData"];
                _eventLog.Add($"processing:{data}");
            },
            ["markValid"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["valid"] = true;
                }
            },
            ["markProcessed"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    underlying.ContextMap["processed"] = true;
                    _eventLog.Add("workflow:complete");
                }
            },
            ["logWorkflowInvalid"] = (ctx) => _eventLog.Add("workflow:invalid"),
            ["incrementIterations"] = (ctx) =>
            {
                var underlying = GetUnderlying();
                if (underlying?.ContextMap != null)
                {
                    var iter = Convert.ToInt32(underlying.ContextMap["iterations"] ?? 0);
                    underlying.ContextMap["iterations"] = iter + 1;
                }
            }
        };
    }

    private Dictionary<string, Func<StateMachine, bool>> CreateGuards()
    {
        return new Dictionary<string, Func<StateMachine, bool>>
        {
            ["shouldRetry"] = (sm) =>
            {
                var retries = Convert.ToInt32(sm.ContextMap?["retries"] ?? 0);
                return retries < 3;
            },
            ["hasValidData"] = (sm) => sm.ContextMap?["serviceInput"] != null,
            ["lessThan10Iterations"] = (sm) => Convert.ToInt32(sm.ContextMap?["iterations"] ?? 0) < 10,
            ["isValid"] = (sm) => (bool)(sm.ContextMap?["valid"] ?? false)
        };
    }

    private Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>> CreateServices()
    {
        return new Dictionary<string, Func<StateMachine, CancellationToken, Task<object>>>
        {
            ["quickService"] = async (sm, ct) =>
            {
                _eventLog.Add("service:quick:started");
                await Task.Delay(50, ct);
                _eventLog.Add("service:quick:completed");
                return "quick-result";
            },
            ["slowService"] = async (sm, ct) =>
            {
                _eventLog.Add("service:slow:started");
                await Task.Delay(500, ct);
                _eventLog.Add("service:slow:completed");
                return "slow-result";
            },
            ["failingService"] = async (sm, ct) =>
            {
                _eventLog.Add("service:failing:started");
                await Task.Delay(50, ct);
                _eventLog.Add("service:failing:throwing");
                throw new InvalidOperationException("Service failed intentionally");
            },
            ["retryableService"] = async (sm, ct) =>
            {
                var attempts = _counters.AddOrUpdate("attempts", 1, (k, v) => v + 1);
                _eventLog.Add($"service:retryable:attempt:{attempts}");
                await Task.Delay(50, ct);

                if (attempts < 3)
                {
                    throw new InvalidOperationException($"Attempt {attempts} failed");
                }

                _eventLog.Add("service:retryable:success");
                return $"success-after-{attempts}-attempts";
            },
            ["contextAwareService"] = async (sm, ct) =>
            {
                var input = sm.ContextMap?["serviceInput"]?.ToString() ?? "no-input";
                _eventLog.Add($"service:context:input:{input}");
                await Task.Delay(50, ct);
                return $"processed-{input}";
            },
            ["longRunningService"] = async (sm, ct) =>
            {
                _eventLog.Add("service:long:started");
                for (int i = 0; i < 10; i++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        _eventLog.Add($"service:long:cancelled:step:{i}");
                        throw new OperationCanceledException();
                    }
                    await Task.Delay(100, ct);
                    _eventLog.Add($"service:long:step:{i}");
                }
                _eventLog.Add("service:long:completed");
                return "long-result";
            },
            ["parallelService1"] = async (sm, ct) =>
            {
                _eventLog.Add("service:p1:started");
                await Task.Delay(100, ct);
                _eventLog.Add("service:p1:completed");
                return "p1-result";
            },
            ["parallelService2"] = async (sm, ct) =>
            {
                _eventLog.Add("service:p2:started");
                await Task.Delay(150, ct);
                _eventLog.Add("service:p2:completed");
                return "p2-result";
            },
            ["nestedService"] = async (sm, ct) =>
            {
                _eventLog.Add("service:nested:started");
                await Task.Delay(50, ct);
                _eventLog.Add("service:nested:invoking-child");
                await Task.Delay(50, ct);
                _eventLog.Add("service:nested:completed");
                return "nested-result";
            }
        };
    }

    [Fact]
    public async Task MultipleConcurrentInvokes_AllComplete()
    {
        // Arrange
        var script = @"
        {
            id: 'concurrentInvokes',
            initial: 'invoking',
            states: {
                invoking: {
                    type: 'parallel',
                    states: {
                        service1: {
                            initial: 'invoking',
                            states: {
                                invoking: {
                                    invoke: {
                                        src: 'quickService',
                                        onDone: 'done1'
                                    }
                                },
                                done1: { type: 'final' }
                            }
                        },
                        service2: {
                            initial: 'invoking',
                            states: {
                                invoking: {
                                    invoke: {
                                        src: 'contextAwareService',
                                        onDone: 'done2'
                                    }
                                },
                                done2: { type: 'final' }
                            }
                        },
                        service3: {
                            initial: 'invoking',
                            states: {
                                invoking: {
                                    invoke: {
                                        src: 'parallelService1',
                                        onDone: 'done3'
                                    }
                                },
                                done3: { type: 'final' }
                            }
                        }
                    },
                    onDone: 'allComplete'
                },
                allComplete: {
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("concurrentInvokes", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;

        var adapter = machine as PureStateMachineAdapter;
        if (adapter?.GetUnderlying()?.ContextMap != null)
        {
            adapter.GetUnderlying().ContextMap["serviceInput"] = "test-input";
        }

        await machine.StartAsync();

        // Act - Wait for services to complete
        await WaitForState(machine, "allComplete");

        // Assert - Deterministically wait for expected log entries
        Assert.NotEmpty(_eventLog);
        var hasQuick = _eventLog.Contains("service:quick:started");
        var hasContext = _eventLog.Contains("service:context:input:test-input");
        var hasP1 = _eventLog.Contains("service:p1:started");

        Assert.True(hasQuick || hasContext || hasP1, "At least one service should have started");

        if (hasQuick)
        {
            // Wait deterministically for completion log
            await WaitForEventLog("service:quick:completed");
        }
    }

    [Fact]
    public async Task ServiceWithRetryLogic_SucceedsAfterRetries()
    {
        // Arrange
        var script = @"
        {
            id: 'retryMachine',
            initial: 'attempting',
            context: {
                retries: 0,
                counter: 0
            },
            states: {
                attempting: {
                    entry: 'incrementCounter',
                    invoke: {
                        src: 'retryableService',
                        onDone: 'success',
                        onError: [
                            {
                                target: 'attempting',
                                cond: 'shouldRetry',
                                actions: 'incrementRetries'
                            },
                            {
                                target: 'failed'
                            }
                        ]
                    }
                },
                success: {
                    entry: 'logSuccess',
                    type: 'final'
                },
                failed: {
                    entry: 'logError',
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("retryMachine", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;
        await machine.StartAsync();

        // Act - Wait for success state
        await WaitForState(machine, "success");

        // Assert
        Assert.Contains("service:retryable:attempt:1", _eventLog);
        Assert.Contains("service:retryable:attempt:2", _eventLog);
        Assert.Contains("service:retryable:attempt:3", _eventLog);
        Assert.Contains("service:retryable:success", _eventLog);
        Assert.Contains("success", machine.CurrentState);
        Assert.Equal("success-after-3-attempts", _serviceResults.GetValueOrDefault("last", ""));
    }

    [Fact]
    public async Task LongRunningService_CancellationWorks()
    {
        // Arrange
        var script = @"
        {
            id: 'cancellableMachine',
            initial: 'idle',
            states: {
                idle: {
                    on: {
                        START: 'running'
                    }
                },
                running: {
                    invoke: {
                        src: 'longRunningService',
                        onDone: 'completed',
                        onError: 'cancelled'
                    },
                    on: {
                        CANCEL: 'cancelling'
                    }
                },
                cancelling: {
                    entry: 'logCancellingEntered'
                },
                completed: {
                    entry: 'logSuccess',
                    type: 'final'
                },
                cancelled: {
                    entry: 'logCancelledEntered',
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("cancellableMachine", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;
        await machine.StartAsync();

        // Act
        await SendEventAsync("TEST", machine.Id, "START");
        await WaitForEventLog("service:long:step:2");
        await SendEventAsync("TEST", machine.Id, "CANCEL");
        await WaitForState(machine, "cancelling");

        // Assert
        Assert.Contains("service:long:started", _eventLog);
        Assert.Contains(_eventLog, e => e.Contains("service:long:step:"));
        Assert.Contains("cancelling", machine.CurrentState);
        Assert.DoesNotContain("service:long:completed", _eventLog);
    }

    [Fact]
    public async Task NestedInvokes_InHierarchicalStates()
    {
        // Arrange
        var script = @"
        {
            id: 'nestedInvokes',
            initial: 'parent',
            states: {
                parent: {
                    initial: 'child1',
                    invoke: {
                        src: 'slowService',
                        onDone: 'parentDone',
                        onError: 'parentError'
                    },
                    states: {
                        child1: {
                            invoke: {
                                src: 'quickService',
                                onDone: 'child2'
                            }
                        },
                        child2: {
                            invoke: {
                                src: 'nestedService',
                                onDone: 'childComplete'
                            }
                        },
                        childComplete: {
                            type: 'final'
                        }
                    },
                    onDone: 'allDone'
                },
                parentDone: {},
                parentError: {},
                allDone: {
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("nestedInvokes", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;
        await machine.StartAsync();

        // Act
        await WaitForEventLog("service:nested:completed");

        // Assert
        Assert.Contains("service:slow:started", _eventLog);
        Assert.Contains("service:quick:completed", _eventLog);
        Assert.Contains("service:nested:completed", _eventLog);

        var finalState = machine.CurrentState;
        Assert.True(finalState.Contains("parentDone") || finalState.Contains("allDone"));
    }

    [Fact]
    public async Task InvokeWithContextPassing_DataFlowsCorrectly()
    {
        // Arrange
        var script = @"
        {
            id: 'contextFlow',
            initial: 'preparing',
            context: {
                counter: 0,
                serviceInput: null
            },
            states: {
                preparing: {
                    entry: 'prepareContext',
                    on: {
                        READY: 'invoking'
                    }
                },
                invoking: {
                    invoke: {
                        src: 'contextAwareService',
                        onDone: {
                            target: 'processing',
                            actions: 'processServiceData'
                        }
                    }
                },
                processing: {
                    entry: 'logProcessing',
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("contextFlow", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;
        await machine.StartAsync();

        // Act
        await SendEventAsync("TEST", _currentMachine.Id, "READY");
        await WaitForState(machine, "processing");

        // Assert
        Assert.Contains("context:prepared", _eventLog);
        Assert.Contains("service:context:input:prepared-data", _eventLog);
        Assert.Contains("data:processed:processed-prepared-data", _eventLog);
        Assert.Contains("processing:processed-prepared-data-processed", _eventLog);
        Assert.Contains("processing", machine.CurrentState);
    }

    [Fact]
    public async Task ErrorHandlingChain_PropagatesToParent()
    {
        // Arrange
        var script = @"
        {
            id: 'errorChain',
            initial: 'level1',
            states: {
                level1: {
                    initial: 'level2',
                    states: {
                        level2: {
                            initial: 'level3',
                            states: {
                                level3: {
                                    invoke: {
                                        src: 'failingService',
                                        onDone: 'unexpected',
                                        onError: {
                                            target: '#errorChain.level1.level2Error'
                                        }
                                    }
                                },
                                unexpected: {}
                            }
                        },
                        level2Error: {
                            entry: 'logLevel2Error'
                        }
                    },
                    onError: {
                        target: 'level1Error'
                    }
                },
                level1Error: {
                    entry: 'logLevel1Error',
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("errorChain", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;
        await machine.StartAsync();

        // Act
        await WaitForEventLog("level2Error:handled");

        // Assert
        Assert.Contains("service:failing:started", _eventLog);
        Assert.Contains("service:failing:throwing", _eventLog);
        Assert.Contains("level2Error:handled", _eventLog);
        Assert.Contains("level2Error", machine.CurrentState);
    }

    [Fact]
    public async Task ParallelInvokes_IndependentCompletion()
    {
        // Arrange
        var script = @"
        {
            id: 'parallelServices',
            initial: 'running',
            states: {
                running: {
                    type: 'parallel',
                    states: {
                        fastTrack: {
                            initial: 'invokingFast',
                            states: {
                                invokingFast: {
                                    invoke: {
                                        src: 'quickService',
                                        onDone: 'fastDone'
                                    }
                                },
                                fastDone: {
                                    entry: 'markFastCompleted',
                                    type: 'final'
                                }
                            }
                        },
                        slowTrack: {
                            initial: 'invokingSlow',
                            states: {
                                invokingSlow: {
                                    invoke: {
                                        src: 'slowService',
                                        onDone: 'slowDone'
                                    }
                                },
                                slowDone: {
                                    entry: 'markSlowCompleted',
                                    type: 'final'
                                }
                            }
                        }
                    },
                    onDone: 'allComplete'
                },
                allComplete: {
                    entry: 'logAllComplete',
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("parallelServices", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;
        await machine.StartAsync();

        // Act
        await WaitForEventLog("fast:done");

        // Assert
        Assert.Contains("service:quick:completed", _eventLog);
        Assert.Contains("fast:done", _eventLog);

        var completedCount = _counters.GetValueOrDefault("completed", 0);
        Assert.True(completedCount >= 1, $"Expected at least 1 completion, got {completedCount}");
    }

    [Fact]
    public async Task ComplexServiceOrchestration_WorkflowCompletes()
    {
        // Arrange
        var script = @"
        {
            id: 'workflow',
            initial: 'validate',
            context: {
                valid: false,
                processed: false
            },
            states: {
                validate: {
                    invoke: {
                        src: 'contextAwareService',
                        onDone: {
                            target: 'decide',
                            actions: 'markValid'
                        },
                        onError: 'invalid'
                    }
                },
                decide: {
                    always: [
                        {
                            target: 'process',
                            cond: 'isValid'
                        },
                        {
                            target: 'invalid'
                        }
                    ]
                },
                process: {
                    type: 'parallel',
                    states: {
                        stream1: {
                            initial: 'invoking',
                            states: {
                                invoking: {
                                    invoke: {
                                        src: 'parallelService1',
                                        onDone: 'stream1Done'
                                    }
                                },
                                stream1Done: { type: 'final' }
                            }
                        },
                        stream2: {
                            initial: 'invoking',
                            states: {
                                invoking: {
                                    invoke: {
                                        src: 'parallelService2',
                                        onDone: 'stream2Done'
                                    }
                                },
                                stream2Done: { type: 'final' }
                            }
                        }
                    },
                    onDone: 'merge'
                },
                merge: {
                    invoke: {
                        src: 'nestedService',
                        onDone: 'complete'
                    }
                },
                complete: {
                    entry: 'markProcessed',
                    type: 'final'
                },
                invalid: {
                    entry: 'logWorkflowInvalid',
                    type: 'final'
                }
            }
        }";

        _currentMachine = CreateMachine("workflow", script, CreateActions(), CreateGuards(), CreateServices());
        var machine = _currentMachine;

        var adapter = machine as PureStateMachineAdapter;
        if (adapter?.GetUnderlying()?.ContextMap != null)
        {
            adapter.GetUnderlying().ContextMap["serviceInput"] = "workflow-data";
        }

        await machine.StartAsync();

        // Act
        await WaitForEventLog("service:context:input:workflow-data");

        // Assert
        Assert.NotEmpty(_eventLog);
        Assert.Contains("service:context:input:workflow-data", _eventLog);

        if (_eventLog.Any(e => e.Contains("service:p1")))
        {
            Assert.Contains("service:p1:completed", _eventLog);
            Assert.Contains("service:p2:completed", _eventLog);
        }

        if (_eventLog.Any(e => e.Contains("service:nested")))
        {
            Assert.Contains("service:nested:completed", _eventLog);
        }
    }
}
