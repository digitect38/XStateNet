using Xunit;

using XStateNet;
using System.Collections.Generic;
using System;

namespace XStateNet.UnitTest
{
    public class LoggerDemoTests
    {
        [Fact]
        public void DemoLoggerWithStateMachine()
        {
            var uniqueId = "DemoLoggerWithStateMachine_" + Guid.NewGuid().ToString("N");

            // Enable caller info for better debugging
            Logger.IncludeCallerInfo = true;
            Logger.CurrentLevel = Logger.LogLevel.Info;

            // Log from test method - will show this file and line number
            Logger.Info("Starting state machine test");

            var actions = new ActionMap
            {
                ["logAction"] = new List<NamedAction> {
                    new NamedAction("logAction", (sm) => {
                        // This will show the line number in this test file
                        Logger.Info("Action executed in state machine");
                    })
                }
            };

            string script = @$"
            {{
                id: '{uniqueId}',
                initial: 'idle',
                states: {{
                    idle: {{
                        entry: 'logAction',
                        on: {{
                            START: 'running'
                        }}
                    }},
                    running: {{
                        entry: 'logAction',
                        on: {{
                            STOP: 'idle'
                        }}
                    }}
                }}
            }}";

            var stateMachine = StateMachineFactory.CreateFromScript(script, threadSafe:false, true,actions, new GuardMap());

            Logger.Info("Starting state machine");
            stateMachine.Start();

            Logger.Info("Sending START event");
            stateMachine.Send("START");

            Logger.Info("Sending STOP event");
            stateMachine.Send("STOP");

            Logger.Info("Test completed");

            // The Transit logs will show they come from Transition.cs
            // But our custom logs will show they come from this test file
        }
    }
}