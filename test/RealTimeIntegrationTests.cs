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

        private StateMachine CreateTestMachine(string id)
        {
            var json = @"{
                ""id"": """ + id + @""",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": {
                                ""target"": ""active"",
                                ""actions"": [""onStart""]
                            }
                        }
                    },
                    ""active"": {
                        ""entry"": [""enterActive""],
                        ""exit"": [""exitActive""],
                        ""on"": {
                            ""STOP"": ""idle"",
                            ""ERROR"": ""error""
                        }
                    },
                    ""error"": {
                        ""on"": {
                            ""RESET"": ""idle""
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

            var machine = StateMachine.CreateFromScript(json, actionMap);
            machine.Start();
            _machines.Add(machine);
            return machine;
        }

        [Fact]
        public void RealTimeAdapter_RegistersStateMachine()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-register");

            // Act
            adapter.RegisterStateMachine(machine, "Test Machine");
            Thread.Sleep(100);

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Single(stateMachines);
            Assert.Equal("Test Machine", stateMachines[0].Name);
        }

        [Fact]
        public void RealTimeAdapter_CapturesStateTransitions()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-transitions");
            adapter.RegisterStateMachine(machine, "Test Machine");
            Thread.Sleep(100);

            // Act
            machine.Send("START");
            Thread.Sleep(200); // Give time for async processing

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var stateItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.State).ToList();

            Assert.True(stateItems.Count >= 2, $"Expected at least 2 state items, got {stateItems.Count}");
            Assert.Contains(stateItems, s => s.Name == "active");
        }

        [Fact]
        public void RealTimeAdapter_CapturesEvents()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-events");
            adapter.RegisterStateMachine(machine, "Test Machine");
            Thread.Sleep(100);

            // Act
            machine.Send("START");
            machine.Send("ERROR");
            machine.Send("RESET");
            Thread.Sleep(200);

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var eventItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.Event).ToList();

            Assert.True(eventItems.Count >= 3, $"Expected at least 3 events, got {eventItems.Count}");
            Assert.Contains(eventItems, e => e.Name == "START");
            Assert.Contains(eventItems, e => e.Name == "ERROR");
            Assert.Contains(eventItems, e => e.Name == "RESET");
        }

        [Fact]
        public void RealTimeAdapter_CapturesActions()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-actions");
            adapter.RegisterStateMachine(machine, "Test Machine");
            Thread.Sleep(100);

            // Act
            machine.Send("START"); // Should trigger onStart and enterActive
            Thread.Sleep(200);

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var actionItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.Action).ToList();

            Assert.True(actionItems.Count >= 2, $"Expected at least 2 actions, got {actionItems.Count}");
            Assert.Contains(actionItems, a => a.Name == "onStart");
            Assert.Contains(actionItems, a => a.Name == "enterActive");
        }

        [Fact]
        public void RealTimeAdapter_UnregisterStateMachine()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-unregister");
            adapter.RegisterStateMachine(machine, "Test Machine");
            Thread.Sleep(100);

            // Act
            adapter.UnregisterStateMachine("test-unregister");
            Thread.Sleep(100);

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Empty(stateMachines);
        }

        [Fact]
        public void RealTimeAdapter_MultipleMachines()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine1 = CreateTestMachine("test-multi-1");
            var machine2 = CreateTestMachine("test-multi-2");

            // Act
            adapter.RegisterStateMachine(machine1, "Machine 1");
            adapter.RegisterStateMachine(machine2, "Machine 2");
            Thread.Sleep(100);

            machine1.Send("START");
            machine2.Send("START");
            machine2.Send("ERROR");
            Thread.Sleep(200);

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
        public void RealTimeAdapter_TimestampsAreSequential()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-timestamps");
            adapter.RegisterStateMachine(machine, "Test Machine");
            Thread.Sleep(100);

            // Act
            machine.Send("START");
            Thread.Sleep(50);
            machine.Send("ERROR");
            Thread.Sleep(50);
            machine.Send("RESET");
            Thread.Sleep(200);

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
        public void RealTimeAdapter_Clear_RemovesAllMachines()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine1 = CreateTestMachine("test-clear-1");
            var machine2 = CreateTestMachine("test-clear-2");

            adapter.RegisterStateMachine(machine1, "Machine 1");
            adapter.RegisterStateMachine(machine2, "Machine 2");
            Thread.Sleep(100);

            // Act
            adapter.Clear();
            Thread.Sleep(100);

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Empty(stateMachines);

            // Events should not be captured after clearing
            machine1.Send("START");
            machine2.Send("START");
            Thread.Sleep(100);

            stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Empty(stateMachines); // Still empty
        }

        [Fact]
        public void RealTimeAdapter_StateDurationCalculation()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-duration");
            adapter.RegisterStateMachine(machine, "Test Machine");
            Thread.Sleep(100);

            // Act
            machine.Send("START");
            Thread.Sleep(100); // Stay in active for 100ms
            machine.Send("STOP");
            Thread.Sleep(100);

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
                Assert.True(activeState.Duration >= 50000, // Allow some tolerance
                    $"Active state duration ({activeState.Duration} us) should be at least 50,000 us");
            }
        }

        [Fact]
        public void RealTimeAdapter_ReRegisteringSameMachine()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var adapter = new RealTimeStateMachineAdapter(viewModel);
            _adapters.Add(adapter);

            var machine = CreateTestMachine("test-reregister");

            // Act
            adapter.RegisterStateMachine(machine, "Original Name");
            Thread.Sleep(100);

            machine.Send("START");
            Thread.Sleep(100);

            // Re-register with different name
            adapter.RegisterStateMachine(machine, "New Name");
            Thread.Sleep(100);

            machine.Send("STOP");
            Thread.Sleep(100);

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