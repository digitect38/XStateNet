using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet;
using XStateNet.Monitoring;

namespace XStateNet.Tests
{
    public class RealTimeMonitoringTests : IDisposable
    {
        private readonly List<StateMachine> _machines = new();

        public void Dispose()
        {
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
        }

        private StateMachine CreateTestMachine(string id)
        {
            var json = @"{
                ""id"": """ + id + @""",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": {
                                ""target"": ""running"",
                                ""actions"": [""logStart""]
                            }
                        }
                    },
                    ""running"": {
                        ""entry"": [""startProcess""],
                        ""exit"": [""stopProcess""],
                        ""on"": {
                            ""STOP"": {
                                ""target"": ""idle"",
                                ""actions"": [""logStop""]
                            },
                            ""PAUSE"": {
                                ""target"": ""paused"",
                                ""cond"": ""canPause""
                            }
                        }
                    },
                    ""paused"": {
                        ""on"": {
                            ""RESUME"": ""running"",
                            ""STOP"": ""idle""
                        }
                    }
                }
            }";

            var actionMap = new ActionMap
            {
                ["logStart"] = new List<NamedAction> { new NamedAction("logStart", sm => { }) },
                ["logStop"] = new List<NamedAction> { new NamedAction("logStop", sm => { }) },
                ["startProcess"] = new List<NamedAction> { new NamedAction("startProcess", sm => { }) },
                ["stopProcess"] = new List<NamedAction> { new NamedAction("stopProcess", sm => { }) }
            };

            var guardMap = new GuardMap
            {
                ["canPause"] = new NamedGuard("canPause", sm => true)
            };

            var machine = StateMachine.CreateFromScript(json, actionMap, guardMap);
            machine.Start();
            _machines.Add(machine);
            return machine;
        }

        [Fact]
        public void Monitor_CapturesStateTransitions()
        {
            // Arrange
            var machine = CreateTestMachine("test-transitions");
            var monitor = new StateMachineMonitor(machine);
            var transitions = new List<StateTransitionEventArgs>();

            monitor.StateTransitioned += (sender, e) => transitions.Add(e);
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            Thread.Sleep(100); // Give time for event processing
            machine.Send("STOP");
            Thread.Sleep(100);

            // Assert
            Assert.True(transitions.Count >= 2, $"Expected at least 2 transitions, got {transitions.Count}");

            var firstTransition = transitions.First();
            // State names in XStateNet include machine ID prefix, extract just the state name
            var fromState = firstTransition.FromState.Contains('.') ? firstTransition.FromState.Split('.').Last() : firstTransition.FromState;
            var toState = firstTransition.ToState.Contains('.') ? firstTransition.ToState.Split('.').Last() : firstTransition.ToState;
            Assert.Equal("idle", fromState);
            Assert.Equal("running", toState);
            Assert.Equal("START", firstTransition.TriggerEvent);

            monitor.StopMonitoring();
        }

        [Fact]
        public void Monitor_CapturesEvents()
        {
            // Arrange
            var machine = CreateTestMachine("test-events");
            var monitor = new StateMachineMonitor(machine);
            var events = new List<StateMachineEventArgs>();

            monitor.EventReceived += (sender, e) => events.Add(e);
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            machine.Send("PAUSE");
            machine.Send("RESUME");
            machine.Send("STOP");
            Thread.Sleep(100);

            // Assert
            Assert.Equal(4, events.Count);
            Assert.Contains(events, e => e.EventName == "START");
            Assert.Contains(events, e => e.EventName == "PAUSE");
            Assert.Contains(events, e => e.EventName == "RESUME");
            Assert.Contains(events, e => e.EventName == "STOP");

            monitor.StopMonitoring();
        }

        [Fact]
        public void Monitor_CapturesActions()
        {
            // Arrange
            var machine = CreateTestMachine("test-actions_2");
            var monitor = new StateMachineMonitor(machine);
            var actions = new List<ActionExecutedEventArgs>();

            monitor.ActionExecuted += (sender, e) => actions.Add(e);
            monitor.StartMonitoring();

            // Act
            machine.Send("START"); // Should trigger logStart and startProcess
            Thread.Sleep(100);
            machine.Send("STOP"); // Should trigger stopProcess and logStop
            Thread.Sleep(100);

            // Assert
            Assert.True(actions.Count >= 4, $"Expected at least 4 actions, got {actions.Count}");
            Assert.Contains(actions, a => a.ActionName == "logStart");
            Assert.Contains(actions, a => a.ActionName == "startProcess");
            Assert.Contains(actions, a => a.ActionName == "stopProcess");
            Assert.Contains(actions, a => a.ActionName == "logStop");

            monitor.StopMonitoring();
        }

        [Fact]
        public void Monitor_GetCurrentStates_ReturnsCorrectStates()
        {
            // Arrange
            var machine = CreateTestMachine("test-current-states");
            var monitor = new StateMachineMonitor(machine);

            // Act & Assert
            var initialStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("idle", initialStates);

            machine.Send("START");
            Thread.Sleep(100);

            var runningStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("running", runningStates);

            machine.Send("PAUSE");
            Thread.Sleep(100);

            var pausedStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("paused", pausedStates);
        }

        [Fact]
        public void Monitor_StartStop_CanBeCalledMultipleTimes()
        {
            // Arrange
            var machine = CreateTestMachine("test-start-stop");
            var monitor = new StateMachineMonitor(machine);
            var eventCount = 0;

            monitor.EventReceived += (sender, e) => eventCount++;

            // Act
            monitor.StartMonitoring();
            monitor.StartMonitoring(); // Should be idempotent

            machine.Send("START");
            Thread.Sleep(100);
            var countAfterStart = eventCount;

            monitor.StopMonitoring();
            monitor.StopMonitoring(); // Should be idempotent

            machine.Send("STOP");
            Thread.Sleep(100);
            var countAfterStop = eventCount;

            // Assert
            Assert.True(countAfterStart > 0, "Should have captured events while monitoring");
            Assert.Equal(countAfterStart, countAfterStop); // No new events after stopping
        }

        [Fact]
        public async Task Monitor_ThreadSafety_HandlesMultipleThreads()
        {
            // Arrange
            var machine = CreateTestMachine("test-thread-safety");
            var monitor = new StateMachineMonitor(machine);
            var transitions = new List<StateTransitionEventArgs>();
            var lockObj = new object();

            monitor.StateTransitioned += (sender, e) =>
            {
                lock (lockObj)
                {
                    transitions.Add(e);
                }
            };
            monitor.StartMonitoring();

            // Act - Send events from multiple threads
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    machine.Send("START");
                    Thread.Sleep(10);
                    machine.Send("STOP");
                }));
            }

            await Task.WhenAll(tasks);
            Thread.Sleep(200); // Give time for all events to process

            // Assert
            Assert.True(transitions.Count > 0, "Should have captured transitions from multiple threads");
            monitor.StopMonitoring();
        }

        [Fact]
        public void Monitor_Timestamps_AreIncreasing()
        {
            // Arrange
            var machine = CreateTestMachine("test-timestamps");
            var monitor = new StateMachineMonitor(machine);
            var events = new List<StateMachineEventArgs>();

            monitor.EventReceived += (sender, e) => events.Add(e);
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            Thread.Sleep(50);
            machine.Send("PAUSE");
            Thread.Sleep(50);
            machine.Send("RESUME");
            Thread.Sleep(50);
            machine.Send("STOP");
            Thread.Sleep(100);

            // Assert
            Assert.True(events.Count >= 4, $"Expected at least 4 events, got {events.Count}");

            for (int i = 1; i < events.Count; i++)
            {
                Assert.True(events[i].Timestamp >= events[i - 1].Timestamp,
                    $"Timestamp {i} ({events[i].Timestamp}) should be >= timestamp {i - 1} ({events[i - 1].Timestamp})");
            }

            monitor.StopMonitoring();
        }

        [Fact]
        public void Monitor_Guards_AreCaptured()
        {
            // Arrange
            var machine = CreateTestMachine("test-guards");
            var monitor = new StateMachineMonitor(machine);
            var guards = new List<GuardEvaluatedEventArgs>();

            monitor.GuardEvaluated += (sender, e) => guards.Add(e);
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            Thread.Sleep(100);
            machine.Send("PAUSE"); // This has a guard condition
            Thread.Sleep(100);

            // Assert - Guard should have been evaluated
            Assert.Contains(guards, g => g.GuardName == "canPause");

            var canPauseGuard = guards.First(g => g.GuardName == "canPause");
            Assert.True(canPauseGuard.Result); // We set it to always return true

            monitor.StopMonitoring();
        }

        [Fact]
        public void Monitor_MultipleMachines_IndependentMonitoring()
        {
            // Arrange
            var machine1 = CreateTestMachine("test-multi-1");
            var machine2 = CreateTestMachine("test-multi-2");

            var monitor1 = new StateMachineMonitor(machine1);
            var monitor2 = new StateMachineMonitor(machine2);

            var events1 = new List<StateMachineEventArgs>();
            var events2 = new List<StateMachineEventArgs>();

            monitor1.EventReceived += (sender, e) => events1.Add(e);
            monitor2.EventReceived += (sender, e) => events2.Add(e);

            monitor1.StartMonitoring();
            monitor2.StartMonitoring();

            // Act
            machine1.Send("START");
            machine2.Send("START");
            machine2.Send("PAUSE");
            Thread.Sleep(100);

            // Assert
            Assert.Equal(1, events1.Count); // Machine1 received only START
            Assert.Equal(2, events2.Count); // Machine2 received START and PAUSE

            Assert.Equal("test-multi-1", monitor1.StateMachineId);
            Assert.Equal("test-multi-2", monitor2.StateMachineId);

            monitor1.StopMonitoring();
            monitor2.StopMonitoring();
        }
    }
}