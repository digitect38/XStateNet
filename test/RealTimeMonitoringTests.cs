using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet;
using XStateNet.Monitoring;
using XStateNet.Orchestration;

namespace XStateNet.Tests
{
    public class RealTimeMonitoringTests : OrchestratorTestBase
    {
        private StateMachine? GetUnderlying(IPureStateMachine machine)
        {
            return (machine as PureStateMachineAdapter)?.GetUnderlying() as StateMachine;
        }

        private async Task SendToMachineAsync(string machineId, string eventName)
        {
            await _orchestrator.SendEventAsync("test", machineId, eventName);
        }

        private (IPureStateMachine machine, string machineId) CreateTestMachine(string id)
        {
            string uniqueId = $"{id}_{Guid.NewGuid():N}";
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

            var actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["logStart"] = ctx => { },
                ["startProcess"] = ctx => { },
                ["stopProcess"] = ctx => { },
                ["logStop"] = ctx => { }
            };

            var guards = new Dictionary<string, Func<StateMachine, bool>>
            {
                ["canPause"] = sm => true
            };

            var machine = CreateMachine(uniqueId, json, actions, guards);
            machine.StartAsync().Wait();
            return (machine, uniqueId);
        }

        [Fact]
        public async Task Monitor_CapturesStateTransitions()
        {
            // Arrange
            var (pureMachine, machineId) = CreateTestMachine("test-transitions");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
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
            await SendToMachineAsync(machineId, "START");
            await SendToMachineAsync(machineId, "STOP");

            // Assert
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Test timed out waiting for state transitions.");
            }

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
            var (pureMachine, machineId) = CreateTestMachine("test-events");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
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
            await SendToMachineAsync(machineId, "START");
            await SendToMachineAsync(machineId, "PAUSE");
            await SendToMachineAsync(machineId, "RESUME");
            await SendToMachineAsync(machineId, "STOP");

            // Assert
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Test timed out waiting for events.");
            }

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
            var (pureMachine, machineId) = CreateTestMachine("test-actions_2");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
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
            await SendToMachineAsync(machineId, "START"); // Should trigger logStart and startProcess
            await SendToMachineAsync(machineId, "STOP"); // Should trigger stopProcess and logStop

            // Assert
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Test timed out waiting for actions.");
            }

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
            var (pureMachine, machineId) = CreateTestMachine("test-current-states");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
            var tcs = new TaskCompletionSource<bool>();

            monitor.StateTransitioned += (sender, e) => tcs.TrySetResult(true);
            monitor.StartMonitoring();

            // Act & Assert
            var initialStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("idle", initialStates);

            await SendToMachineAsync(machineId, "START");
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Timed out waiting for START transition");
            }

            var runningStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("running", runningStates);

            tcs = new TaskCompletionSource<bool>();
            await SendToMachineAsync(machineId, "PAUSE");
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts2.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Timed out waiting for PAUSE transition");
            }

            var pausedStates = monitor.GetCurrentStates().Select(s => s.Contains('.') ? s.Split('.').Last() : s).ToList();
            Assert.Contains("paused", pausedStates);
        }

        [Fact]
        public async Task Monitor_StartStop_CanBeCalledMultipleTimes()
        {
            // Arrange
            var (pureMachine, machineId) = CreateTestMachine("test-start-stop");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
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

            await SendToMachineAsync(machineId, "START");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Timed out waiting for START event");
            }
            var countAfterStart = eventCount;

            monitor.StopMonitoring();
            monitor.StopMonitoring(); // Should be idempotent

            await SendToMachineAsync(machineId, "STOP");
            // Wait for state to stabilize
            await WaitForStateAsync(pureMachine, "idle", 1000);
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

            var (pureMachine, machineId) = CreateTestMachine("test-thread-safety");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
            var transitions = new List<StateTransitionEventArgs>();
            var lockObj = new object();
            var exceptions = new List<Exception>();

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
            for (int i = 0; i < taskCount; i++)
            {
                var taskIndex = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Each SendEventAsync operation is atomic and serialized
                        await SendToMachineAsync(machineId, "START");
                        await SendToMachineAsync(machineId, "PAUSE");
                        await SendToMachineAsync(machineId, "RESUME");
                        await SendToMachineAsync(machineId, "STOP");
                    }
                    catch (Exception ex)
                    {
                        // Capture any exceptions to verify thread safety
                        lock (lockObj)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Ensure we end in a valid state
            var finalState = pureMachine.CurrentState;
            Assert.True(
                finalState.Contains("idle") ||
                finalState.Contains("running") ||
                finalState.Contains("paused"),
                $"Machine should be in a valid state. Current state: {finalState}");

            // Assert - We expect at least some transitions to be captured
            Assert.True(transitions.Count > 0, $"Should have captured at least some transitions. Captured {transitions.Count}");

            // Verify thread-safety: no exceptions should have occurred
            Assert.Empty(exceptions);

            // Verify no data corruption in captured transitions
            foreach (var transition in transitions)
            {
                Assert.NotNull(transition.StateMachineId);
                Assert.NotNull(transition.ToState);
                Assert.True(transition.Timestamp > DateTime.MinValue);
                // Verify states are valid (not corrupted)
                Assert.True(
                    transition.ToState.Contains("idle") ||
                    transition.ToState.Contains("running") ||
                    transition.ToState.Contains("paused"),
                    $"Invalid state in transition: {transition.ToState}");
            }

            monitor.StopMonitoring();
        }

        [Fact]
        public async Task Monitor_Timestamps_AreIncreasing()
        {
            // Arrange
            var (pureMachine, machineId) = CreateTestMachine("test-timestamps");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
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
            await SendToMachineAsync(machineId, "START");
            await WaitForStateAsync(pureMachine, "running", 1000);
            await SendToMachineAsync(machineId, "PAUSE");
            await WaitForStateAsync(pureMachine, "paused", 1000);
            await SendToMachineAsync(machineId, "RESUME");
            await WaitForStateAsync(pureMachine, "running", 1000);
            await SendToMachineAsync(machineId, "STOP");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Test timed out waiting for events.");
            }

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
            var (pureMachine, machineId) = CreateTestMachine("test-guards");
            var underlying = GetUnderlying(pureMachine);
            Assert.NotNull(underlying);

            var monitor = new StateMachineMonitor(underlying!);
            var guards = new List<GuardEvaluatedEventArgs>();
            var tcs = new TaskCompletionSource<bool>();

            monitor.GuardEvaluated += (sender, e) =>
            {
                guards.Add(e);
                tcs.TrySetResult(true);
            };
            monitor.StartMonitoring();

            // Act
            await SendToMachineAsync(machineId, "START");
            await SendToMachineAsync(machineId, "PAUSE"); // This has a guard condition

            // Assert - Guard should have been evaluated
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Test timed out waiting for guard evaluation.");
            }

            Assert.Contains(guards, g => g.GuardName == "canPause");

            var canPauseGuard = guards.First(g => g.GuardName == "canPause");
            Assert.True(canPauseGuard.Result); // We set it to always return true

            monitor.StopMonitoring();
        }

        [Fact]
        public async Task Monitor_MultipleMachines_IndependentMonitoring()
        {
            // Arrange
            var (pureMachine1, machineId1) = CreateTestMachine("test-multi-1");
            var (pureMachine2, machineId2) = CreateTestMachine("test-multi-2");

            var underlying1 = GetUnderlying(pureMachine1);
            var underlying2 = GetUnderlying(pureMachine2);
            Assert.NotNull(underlying1);
            Assert.NotNull(underlying2);

            var monitor1 = new StateMachineMonitor(underlying1!);
            var monitor2 = new StateMachineMonitor(underlying2!);

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
            await SendToMachineAsync(machineId1, "START");
            await SendToMachineAsync(machineId2, "START");
            await SendToMachineAsync(machineId2, "PAUSE");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, "Test timed out waiting for events from machine2.");
            }

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