using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XStateNet;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace XStateV5_Test.AdvancedFeatures;

public class UnitTest_InvokeOnError
{
    [Fact]
    public async Task Invoke_OnError_TransitionIsTaken()
    {
        // Arrange
        var log = new List<string>();

        var services = new ServiceMap
        {
            ["errorService"] = new NamedService("errorService", (sm, ct) =>
            {
                log.Add("service:started");
                return Task.FromException<object>(new InvalidOperationException("Service error"));
            })
        };

        var actions = new ActionMap
        {
            ["logError"] = new List<NamedAction> { new NamedAction("logError", (sm) => {
                log.Add("action:logError");
            }) }
        };

        var script = @"
        {
            id: 'errorMachine',
            initial: 'running',
            states: {
                running: {
                    invoke: {
                        src: 'errorService',
                        onError: {
                            target: 'failed',
                            actions: ['logError']
                        }
                    }
                },
                failed: {}
            }
        }";

        var stateMachine = StateMachine.CreateFromScript(script, actions, null, services);
        stateMachine.Start();

        // Act
        await Task.Delay(100); // Give the service time to fail

        // Assert
        Assert.Contains("service:started", log);
        Assert.Contains("action:logError", log);
        Assert.True(stateMachine.IsInState(stateMachine, "#errorMachine.failed"));
    }

    [Fact]
    public async Task Invoke_OnDone_TransitionIsTaken()
    {
        // Arrange
        var log = new List<string>();

        var services = new ServiceMap
        {
            ["successService"] = new NamedService("successService", (sm, ct) =>
            {
                log.Add("service:started");
                return Task.FromResult<object>(42);
            })
        };

        var actions = new ActionMap
        {
            ["logDone"] = new List<NamedAction> { new NamedAction("logDone", (sm) => {
                log.Add("action:logDone");
            }) }
        };

        var script = @"
        {
            id: 'successMachine',
            initial: 'running',
            states: {
                running: {
                    invoke: {
                        src: 'successService',
                        onDone: {
                            target: 'succeeded',
                            actions: ['logDone']
                        }
                    }
                },
                succeeded: {}
            }
        }";

        var stateMachine = StateMachine.CreateFromScript(script, actions, null, services);
        stateMachine.Start();

        // Act
        await Task.Delay(100); // Give the service time to complete

        // Assert
        Assert.Contains("service:started", log);
        Assert.Contains("action:logDone", log);
        Assert.True(stateMachine.IsInState(stateMachine, "#successMachine.succeeded"));
    }

    [Fact]
    public async Task Invoke_OnDone_WithData_DataIsCorrectlyPassed()
    {
        // Arrange
        var log = new List<string>();

        var services = new ServiceMap
        {
            ["dataService"] = new NamedService("dataService", (sm, ct) =>
            {
                log.Add("service:started");
                return Task.FromResult<object>("some data");
            })
        };

        var actions = new ActionMap
        {
            ["logData"] = new List<NamedAction> { new NamedAction("logData", (sm) => {
                // Check if we have the service result in context
                var serviceResult = sm.ContextMap?["_serviceResult"];
                if (serviceResult != null)
                {
                    log.Add($"action:logData:{serviceResult}");
                }
            }) }
        };

        var script = @"
        {
            id: 'dataMachine',
            initial: 'running',
            states: {
                running: {
                    invoke: {
                        src: 'dataService',
                        onDone: {
                            target: 'succeeded',
                            actions: ['logData']
                        }
                    }
                },
                succeeded: {}
            }
        }";

        var stateMachine = StateMachine.CreateFromScript(script, actions, null, services);
        stateMachine.Start();

        // Act
        await Task.Delay(200); // Give the service time to complete and send events

        // Assert
        Assert.Contains("service:started", log);
        Assert.Contains("action:logData:some data", log);
        Assert.True(stateMachine.IsInState(stateMachine, "#dataMachine.succeeded"));
    }
}