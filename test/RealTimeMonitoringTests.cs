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
            string uniqueId = $"#{id}-{Guid.NewGuid()}";
            var json = @"{
                'id': '" + uniqueId + @"',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': {
                                'target': 'running',
                                'actions': ['logStart']
                            }
                        }
                    },
                    'running': {
                        'entry': ['startProcess'],
                        'exit': ['stopProcess'],
                        'on': {
                            'STOP': {
                                'target': 'idle',
                                'actions': ['logStop']
                            },
                            'PAUSE': {
                                'target': 'paused',
                                'cond': 'canPause'
                            }
                        }
                    },
                    'paused': {
                        'on': {
                            'RESUME': 'running',
                            'STOP': 'idle'
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

            var machine = StateMachine.CreateFromScript(json, guidIsolate: true, actionMap, guardMap);
            machine.Start();
            _machines.Add(machine);
            return machine;
        }

        [Fact]
        public async Task Monitor_CapturesStateTransitions()
        {
            // Arrange
            var machine = CreateTestMachine("test-transitions");
            var monitor = new StateMachineMonitor(machine);
            var transitions = new List<StateTransitionEventArgs>();
            var tcs = new TaskCompletionSource<bool>();

            monitor.StateTransitioned += (sender, e) =>
            {
                transitions.Add(e);
                if (transitions.Count >= 2)
                {
                    tcs.TrySetResult(true);
                }
            };
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            machine.Send("STOP");

            // Assert
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for state transitions.");

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
        public async Task Monitor_CapturesEvents()
        {
            // Arrange
            var machine = CreateTestMachine("test-events");
            var monitor = new StateMachineMonitor(machine);
            var events = new List<StateMachineEventArgs>();
            var tcs = new TaskCompletionSource<bool>();

            monitor.EventReceived += (sender, e) =>
            {
                events.Add(e);
                if (events.Count >= 4)
                {
                    tcs.TrySetResult(true);
                }
            };
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            machine.Send("PAUSE");
            machine.Send("RESUME");
            machine.Send("STOP");

            // Assert
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for events.");

            Assert.Equal(4, events.Count);
            Assert.Contains(events, e => e.EventName == "START");
            Assert.Contains(events, e => e.EventName == "PAUSE");
            Assert.Contains(events, e => e.EventName == "RESUME");
            Assert.Contains(events, e => e.EventName == "STOP");

            monitor.StopMonitoring();
        }

        [Fact]
        public async Task Monitor_CapturesActions()
        {
            // Arrange
            var machine = CreateTestMachine("test-actions_2");
            var monitor = new StateMachineMonitor(machine);
            var actions = new List<ActionExecutedEventArgs>();
            var tcs = new TaskCompletionSource<bool>();

            monitor.ActionExecuted += (sender, e) =>
            {
                actions.Add(e);
                if (actions.Count >= 4)
                {
                    tcs.TrySetResult(true);
                }
            };
            monitor.StartMonitoring();

            // Act
            machine.Send("START"); // Should trigger logStart and startProcess
            machine.Send("STOP"); // Should trigger stopProcess and logStop

            // Assert
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for actions.");

            Assert.True(actions.Count >= 4, $"Expected at least 4 actions, got {actions.Count}");
            Assert.Contains(actions, a => a.ActionName == "logStart");
            Assert.Contains(actions, a => a.ActionName == "startProcess");
            Assert.Contains(actions, a => a.ActionName == "stopProcess");
            Assert.Contains(actions, a => a.ActionName == "logStop");

            monitor.StopMonitoring();
        }

        [Fact]
        public async Task Monitor_GetCurrentStates_ReturnsCorrectStates()
        {
            // Arrange
            var machine = CreateTestMachine("test-current-states");
            var monitor = new StateMachineMonitor(machine);
            var tcs = new TaskCompletionSource<bool>();

            monitor.StateTransitioned += (sender, e) => tcs.TrySetResult(true);
            monitor.StartMonitoring();

            // Act & Assert
            var initialStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("idle", initialStates);

            machine.Send("START");
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Timed out waiting for START transition");

            var runningStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("running", runningStates);

            tcs = new TaskCompletionSource<bool>();
            machine.Send("PAUSE");
            completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Timed out waiting for PAUSE transition");

            var pausedStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("paused", pausedStates);
        }

        [Fact]
        public async Task Monitor_StartStop_CanBeCalledMultipleTimes()
        {
            // Arrange
            var machine = CreateTestMachine("test-start-stop");
            var monitor = new StateMachineMonitor(machine);
            var eventCount = 0;
            var tcs = new TaskCompletionSource<bool>();

            monitor.EventReceived += (sender, e) =>
            {
                eventCount++;
                tcs.TrySetResult(true);
            };

            // Act
            monitor.StartMonitoring();
            monitor.StartMonitoring(); // Should be idempotent

            machine.Send("START");
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Timed out waiting for START event");
            var countAfterStart = eventCount;

            monitor.StopMonitoring();
            monitor.StopMonitoring(); // Should be idempotent

            machine.Send("STOP");
            await Task.Delay(100); // Give a small delay to ensure no more events are processed
            var countAfterStop = eventCount;

            // Assert
            Assert.True(countAfterStart > 0, "Should have captured events while monitoring");
            Assert.Equal(countAfterStart, countAfterStop); // No new events after stopping
        }

        [Fact]
        public async Task Monitor_ThreadSafety_HandlesMultipleThreads()
        {
            // Arrange
            const int taskCount = 10;

            var machine = CreateTestMachine("test-thread-safety");
            var monitor = new StateMachineMonitor(machine);
            var transitions = new List<StateTransitionEventArgs>();
            var lockObj = new object();
            var tcs = new TaskCompletionSource<bool>();

            monitor.StateTransitioned += (sender, e) =>
            {
                lock (lockObj)
                {
                    transitions.Add(e);
                }
            };
            monitor.StartMonitoring();

            // Act - Send events from multiple threads with delays to ensure transitions occur
            var tasks = new List<Task>();
            for (int i = 0; i < taskCount; i++)
            {
                var taskIndex = i;
                tasks.Add(Task.Run(async () =>
                {
                    // Stagger the events slightly to avoid conflicts
                    await Task.Delay(taskIndex * 10);
                    machine.Send("START");
                    await Task.Delay(5); // Small delay to allow transition
                    machine.Send("STOP");
                }));
            }

            await Task.WhenAll(tasks);

            // Give time for all transitions to be processed
            await Task.Delay(100);

            // Assert - We expect at least some transitions to be captured
            // Due to concurrent access, not all transitions may occur (some events may be ignored)
            Assert.True(transitions.Count > 0, $"Should have captured at least some transitions. Captured {transitions.Count}");

            // Verify thread-safety: no exceptions or corrupted data
            foreach (var transition in transitions)
            {
                Assert.NotNull(transition.StateMachineId);
                Assert.NotNull(transition.ToState);
                Assert.True(transition.Timestamp > DateTime.MinValue);
            }

            monitor.StopMonitoring();
        }

        [Fact]
        public async Task Monitor_Timestamps_AreIncreasing()
        {
            // Arrange
            var machine = CreateTestMachine("test-timestamps");
            var monitor = new StateMachineMonitor(machine);
            var events = new List<StateMachineEventArgs>();
            var tcs = new TaskCompletionSource<bool>();

            monitor.EventReceived += (sender, e) =>
            {
                events.Add(e);
                if (events.Count >= 4)
                {
                    tcs.TrySetResult(true);
                }
            };
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            await Task.Delay(10);
            machine.Send("PAUSE");
            await Task.Delay(10);
            machine.Send("RESUME");
            await Task.Delay(10);
            machine.Send("STOP");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for events.");

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
        public async Task Monitor_Guards_AreCaptured()
        {
            // Arrange
            var machine = CreateTestMachine("test-guards");
            var monitor = new StateMachineMonitor(machine);
            var guards = new List<GuardEvaluatedEventArgs>();
            var tcs = new TaskCompletionSource<bool>();

            monitor.GuardEvaluated += (sender, e) =>
            {
                guards.Add(e);
                tcs.TrySetResult(true);
            };
            monitor.StartMonitoring();

            // Act
            machine.Send("START");
            machine.Send("PAUSE"); // This has a guard condition

            // Assert - Guard should have been evaluated
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for guard evaluation.");

            Assert.Contains(guards, g => g.GuardName == "canPause");

            var canPauseGuard = guards.First(g => g.GuardName == "canPause");
            Assert.True(canPauseGuard.Result); // We set it to always return true

            monitor.StopMonitoring();
        }

        [Fact]
        public async Task Monitor_MultipleMachines_IndependentMonitoring()
        {
            // Arrange
            var machine1 = CreateTestMachine("test-multi-1");
            var machine2 = CreateTestMachine("test-multi-2");

            var monitor1 = new StateMachineMonitor(machine1);
            var monitor2 = new StateMachineMonitor(machine2);

            var events1 = new List<StateMachineEventArgs>();
            var events2 = new List<StateMachineEventArgs>();
            var tcs = new TaskCompletionSource<bool>();

            monitor1.EventReceived += (sender, e) => events1.Add(e);
            monitor2.EventReceived += (sender, e) =>
            {
                events2.Add(e);
                if (events2.Count >= 2)
                {
                    tcs.TrySetResult(true);
                }
            };

            monitor1.StartMonitoring();
            monitor2.StartMonitoring();

            // Act
            machine1.Send("START");
            machine2.Send("START");
            machine2.Send("PAUSE");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for events from machine2.");

            // Assert
            Assert.Single(events1); // Machine1 received only START
            Assert.Equal(2, events2.Count); // Machine2 received START and PAUSE

            Assert.Contains("test-multi-1", monitor1.StateMachineId);
            Assert.Contains("test-multi-2", monitor2.StateMachineId);

            monitor1.StopMonitoring();
            monitor2.StopMonitoring();
        }
    }
}