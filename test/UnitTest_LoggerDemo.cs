using Xunit;
using FluentAssertions;
using XStateNet;
using System.Collections.Generic;

namespace XStateNet.UnitTest
{
    public class LoggerDemoTests
    {
        [Fact]
        public void DemoLoggerWithStateMachine()
        {
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
            
            const string script = @"
            {
                'id': 'demo',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'entry': ['logAction'],
                        'on': {
                            'START': 'running'
                        }
                    },
                    'running': {
                        'entry': ['logAction'],
                        'on': {
                            'STOP': 'idle'
                        }
                    }
                }
            }";
            
            var stateMachine = StateMachine.CreateFromScript(script, actions, new GuardMap());
            
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