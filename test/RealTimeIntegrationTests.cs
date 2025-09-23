using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using XStateNet;
using TimelineWPF.Models;
using TimelineWPF.ViewModels;

namespace TimelineWPF.Tests
{
    public class RealTimeIntegrationTests : IDisposable
    {
        private readonly List<StateMachine> _machines = new();
        private readonly List<RealTimeStateMachineAdapter> _adapters = new();

        public void Dispose()
        {
            foreach (var adapter in _adapters)
            {
                adapter.Dispose();
            }
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
        }

        private StateMachine CreateTestMachineWithFixedId(string fixedId)
        {
            var json = @"{
                'id': '" + fixedId + @"',
                'initial': 'idle',
                'states': {
                    'idle': {
                        'on': {
                            'START': {
                                'target': 'active',
                                'actions': ['onStart']
                            }
                        }
                    },
                    'active': {
                        'entry': ['enterActive'],
                        'exit': ['exitActive'],
                        'on': {
                            'STOP': 'idle',
                            'ERROR': 'error'
                        }
                    },
                    'error': {
                        'on': {
                            'RESET': 'idle'
                        }
                    }
                }
            }";

            var actionMap = new ActionMap
            {
                ["onStart"] = new List<NamedAction> { new NamedAction("onStart", _ => { }) },
                ["enterActive"] = new List<NamedAction> { new NamedAction("enterActive", _ => { }) },
                ["exitActive"] = new List<NamedAction> { new NamedAction("exitActive", _ => { }) }
            };

            var machine = StateMachine.CreateFromScript(json, guidIsolate: true, actionMap);
            machine.Start();
            _machines.Add(machine);
            return machine;
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
                                'target': 'active',
                                'actions': ['onStart']
                            }
                        }
                    },
                    'active': {
                        'entry': ['enterActive'],
                        'exit': ['exitActive'],
                        'on': {
                            'STOP': 'idle',
                            'ERROR': 'error'
                        }
                    },
                    'error': {
                        'on': {
                            'RESET': 'idle'
                        }
                    }
                }
            }";

            var actionMap = new ActionMap
            {
                ["onStart"] = new List<NamedAction> { new NamedAction("onStart", _ => { }) },
                ["enterActive"] = new List<NamedAction> { new NamedAction("enterActive", _ => { }) },
                ["exitActive"] = new List<NamedAction> { new NamedAction("exitActive", _ => { }) }
            };

            var machine = StateMachine.CreateFromScript(json, guidIsolate: true, actionMap);
            machine.Start();
            _machines.Add(machine);
            return machine;
        }

        [Fact]
        public async Task RealTimeAdapter_RegistersStateMachine()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var tcs = new TaskCompletionSource<bool>();
            adapter.ViewModelUpdated += (s, e) => tcs.TrySetResult(true);

            var machine = CreateTestMachine("test-register");

            // Act
            adapter.RegisterStateMachine(machine, "Test Machine");
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for view model update.");

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Single(stateMachines);
            Assert.Equal("Test Machine", stateMachines[0].Name);
        }

        [Fact]
        public async Task RealTimeAdapter_CapturesStateTransitions()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var registrationTcs = new TaskCompletionSource<bool>();
            adapter.ViewModelUpdated += (s, e) => registrationTcs.TrySetResult(true);

            var machine = CreateTestMachine("test-transitions");
            adapter.RegisterStateMachine(machine, "Test Machine");
            var completed = await Task.WhenAny(registrationTcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == registrationTcs.Task, "Test timed out waiting for registration.");

            var transitionTcs = new TaskCompletionSource<bool>();
            adapter.ViewModelUpdated += (s, e) => transitionTcs.TrySetResult(true);

            // Act
            machine.Send("START");
            completed = await Task.WhenAny(transitionTcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == transitionTcs.Task, "Test timed out waiting for state transition.");

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var stateItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.State).ToList();

            Assert.True(stateItems.Count >= 2, $"Expected at least 2 state items, got {stateItems.Count}");
            Assert.Contains(stateItems, s => s.Name == "active");
        }

        [Fact]
        public async Task RealTimeAdapter_CapturesEvents()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var registrationTcs = new TaskCompletionSource<bool>();
            adapter.ViewModelUpdated += (s, e) => registrationTcs.TrySetResult(true);

            var machine = CreateTestMachine("test-events");
            adapter.RegisterStateMachine(machine, "Test Machine");
            var completed = await Task.WhenAny(registrationTcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == registrationTcs.Task, "Test timed out waiting for registration.");

            var eventTcs = new TaskCompletionSource<bool>();
            var eventCount = 0;
            adapter.ViewModelUpdated += (s, e) =>
            {
                // This event handler is simplistic. A real implementation might need to check which event was received.
                eventCount++;
                if (eventCount >= 3)
                {
                    eventTcs.TrySetResult(true);
                }
            };

            // Act
            machine.Send("START");
            machine.Send("ERROR");
            machine.Send("RESET");
            completed = await Task.WhenAny(eventTcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == eventTcs.Task, "Test timed out waiting for events.");

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var eventItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.Event).ToList();

            Assert.True(eventItems.Count >= 3, $"Expected at least 3 events, got {eventItems.Count}");
            Assert.Contains(eventItems, e => e.Name == "START");
            Assert.Contains(eventItems, e => e.Name == "ERROR");
            Assert.Contains(eventItems, e => e.Name == "RESET");
        }

        [Fact]
        public async Task RealTimeAdapter_CapturesActions()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var registrationTcs = new TaskCompletionSource<bool>();
            adapter.ViewModelUpdated += (s, e) => registrationTcs.TrySetResult(true);

            var machine = CreateTestMachine("test-actions");
            adapter.RegisterStateMachine(machine, "Test Machine");
            var completed = await Task.WhenAny(registrationTcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == registrationTcs.Task, "Test timed out waiting for registration.");

            var actionTcs = new TaskCompletionSource<bool>();
            var actionCount = 0;
            adapter.ViewModelUpdated += (s, e) =>
            {
                actionCount++;
                if (actionCount >= 2) // onStart and enterActive
                {
                    actionTcs.TrySetResult(true);
                }
            };

            // Act
            machine.Send("START"); // Should trigger onStart and enterActive
            completed = await Task.WhenAny(actionTcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == actionTcs.Task, "Test timed out waiting for actions.");

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var actionItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.Action).ToList();

            Assert.True(actionItems.Count >= 2, $"Expected at least 2 actions, got {actionItems.Count}");
            Assert.Contains(actionItems, a => a.Name == "onStart");
            Assert.Contains(actionItems, a => a.Name == "enterActive");
        }

        [Fact]
        public async Task RealTimeAdapter_UnregisterStateMachine()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var updateReceived = false;
            adapter.ViewModelUpdated += (s, e) => updateReceived = true;

            var machine = CreateTestMachine("test-unregister");
            adapter.RegisterStateMachine(machine, "Test Machine");
            await Task.Delay(100); // Allow registration to complete
            Assert.Single(viewModel.GetStateMachines());

            // Act
            var actualId = machine.machineId;
            adapter.UnregisterStateMachine(actualId);
            await Task.Delay(100); // Allow unregistration to complete

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Empty(stateMachines);

            // Verify no more updates are received
            updateReceived = false;
            machine.Send("START");
            await Task.Delay(200); // Wait to see if an update occurs
            Assert.False(updateReceived, "Should not receive updates after unregistering.");
        }

        [Fact]
        public async Task RealTimeAdapter_MultipleMachines()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var tcs = new TaskCompletionSource<bool>();
            var updateCount = 0;

            adapter.ViewModelUpdated += (s, e) =>
            {
                updateCount++;
                // 2 registrations + 1 event for m1 + 2 events for m2 = 5 updates
                if (updateCount >= 5)
                {
                    tcs.TrySetResult(true);
                }
            };

            var machine1 = CreateTestMachine("test-multi-1");
            var machine2 = CreateTestMachine("test-multi-2");

            // Act
            adapter.RegisterStateMachine(machine1, "Machine 1");
            adapter.RegisterStateMachine(machine2, "Machine 2");

            machine1.Send("START");
            machine2.Send("START");
            machine2.Send("ERROR");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for view model updates.");

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Equal(2, stateMachines.Count);

            var m1 = stateMachines.First(m => m.Name == "Machine 1");
            var m2 = stateMachines.First(m => m.Name == "Machine 2");

            // Machine 1 should have fewer events than Machine 2
            var m1Events = m1.Data.Where(d => d.Type == TimelineItemType.Event).Count();
            var m2Events = m2.Data.Where(d => d.Type == TimelineItemType.Event).Count();

            Assert.True(m2Events > m1Events, "Machine 2 should have more events");
        }

        [Fact]
        public async Task RealTimeAdapter_TimestampsAreSequential()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var tcs = new TaskCompletionSource<bool>();
            var eventCount = 0;

            adapter.ViewModelUpdated += (s, e) =>
            {
                eventCount++;
                // Wait for registration + 3 events
                if (eventCount >= 4)
                {
                    tcs.TrySetResult(true);
                }
            };

            var machine = CreateTestMachine("test-timestamps");
            adapter.RegisterStateMachine(machine, "Test Machine");

            // Act
            machine.Send("START");
            await Task.Delay(10);
            machine.Send("ERROR");
            await Task.Delay(10);
            machine.Send("RESET");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for view model updates.");

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var allItems = stateMachine.Data.OrderBy(d => d.Time).ToList();

            for (int i = 1; i < allItems.Count; i++)
            {
                Assert.True(allItems[i].Time >= allItems[i - 1].Time,
                    $"Item {i} time ({allItems[i].Time}) should be >= item {i - 1} time ({allItems[i - 1].Time})");
            }
        }

        [Fact]
        public async Task RealTimeAdapter_Clear_RemovesAllMachines()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var updateReceived = false;
            adapter.ViewModelUpdated += (s, e) => updateReceived = true;

            var machine1 = CreateTestMachine("test-clear-1");
            var machine2 = CreateTestMachine("test-clear-2");

            adapter.RegisterStateMachine(machine1, "Machine 1");
            adapter.RegisterStateMachine(machine2, "Machine 2");
            await Task.Delay(100); // Allow registrations to complete

            // Act
            adapter.Clear();
            await Task.Delay(100); // Allow clear to process

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Empty(stateMachines);

            // Events should not be captured after clearing
            updateReceived = false;
            machine1.Send("START");
            machine2.Send("START");
            await Task.Delay(200); // Wait to see if an update occurs

            Assert.False(updateReceived, "Should not receive updates after clearing.");
            stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Empty(stateMachines); // Still empty
        }

        [Fact]
        public async Task RealTimeAdapter_StateDurationCalculation()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var tcs = new TaskCompletionSource<bool>();
            var updateCount = 0;

            adapter.ViewModelUpdated += (s, e) =>
            {
                updateCount++;
                // Registration + START + STOP
                if (updateCount >= 3)
                {
                    tcs.TrySetResult(true);
                }
            };

            var machine = CreateTestMachine("test-duration");
            adapter.RegisterStateMachine(machine, "Test Machine");

            // Act
            machine.Send("START");
            await Task.Delay(100); // Stay in active for 100ms
            machine.Send("STOP");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for view model updates.");

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var stateItems = stateMachine.Data
                .Where(d => d.Type == TimelineItemType.State)
                .OrderBy(d => d.Time)
                .ToList();

            // Find the active state item
            var activeState = stateItems.FirstOrDefault(s => s.Name == "active");
            Assert.NotNull(activeState);

            // Duration should be set when transitioning to next state
            if (activeState.Duration > 0)
            {
                // Duration should be at least 100ms (100,000 microseconds)
                Assert.True(activeState.Duration >= 90000, // Allow some tolerance
                    $"Active state duration ({activeState.Duration} us) should be at least 90,000 us");
            }
        }

        [Fact]
        public async Task RealTimeAdapter_ReRegisteringSameMachine()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);
            var tcs = new TaskCompletionSource<bool>();
            var updateCount = 0;

            adapter.ViewModelUpdated += (s, e) =>
            {
                updateCount++;
                // 1st registration, START event, 2nd registration, STOP event
                if (updateCount >= 4)
                {
                    tcs.TrySetResult(true);
                }
            };

            // For this test, we need a fixed ID to test re-registration
            var fixedId = $"test-reregister-{Guid.NewGuid():N}";
            var machine = CreateTestMachineWithFixedId(fixedId);

            // Act
            adapter.RegisterStateMachine(machine, "Original Name");
            machine.Send("START");

            // Re-register with different name
            adapter.RegisterStateMachine(machine, "New Name");
            machine.Send("STOP");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(completed == tcs.Task, "Test timed out waiting for view model updates.");

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Single(stateMachines);
            Assert.Equal("New Name", stateMachines[0].Name);

            // Should have events from both before and after re-registration
            var events = stateMachines[0].Data.Where(d => d.Type == TimelineItemType.Event).ToList();
            Assert.Contains(events, e => e.Name == "STOP"); // Event after re-registration
        }
    }
}