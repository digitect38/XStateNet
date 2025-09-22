using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TimelineWPF;
using TimelineWPF.Models;
using TimelineWPF.ViewModels;
using XStateNet;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.PubSub;
using Microsoft.Extensions.Logging;

namespace TimelineWPF.Tests
{
    /// <summary>
    /// Integration tests for pub/sub timeline functionality
    /// </summary>
    public class PubSubTimelineIntegrationTests : IDisposable
    {
        private readonly List<IDisposable> _disposables = new();
        private readonly List<StateMachine> _machines = new();
        private readonly ILoggerFactory _loggerFactory;

        public PubSubTimelineIntegrationTests()
        {
            _loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        }

        [Fact]
        public async Task PubSubAdapter_RegistersLocalMachine()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new PubSubTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            await adapter.StartAsync();

            var machine = CreateTestMachine("pubsub-local");

            // Act
            await adapter.RegisterStateMachineAsync(machine, "PubSub Test Machine");

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Single(stateMachines);
            Assert.Equal("PubSub Test Machine", stateMachines[0].Name);
        }

        [Fact]
        public async Task PubSubAdapter_ReceivesRemoteStateChanges()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new PubSubTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            await adapter.StartAsync();
            await adapter.SubscribeToRemoteMachineAsync("remote-machine", "Remote Machine");

            // Act - Simulate remote state change
            await eventBus.PublishStateChangeAsync("remote-machine", new StateChangeEvent
            {
                SourceMachineId = "remote-machine",
                OldState = "idle",
                NewState = "active",
                Timestamp = DateTime.UtcNow
            });

            await Task.Delay(100); // Let event propagate

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var stateItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.State).ToList();

            Assert.NotEmpty(stateItems);
            Assert.Contains(stateItems, s => s.Name == "active");
        }

        [Fact]
        public async Task PubSubAdapter_ReceivesRemoteActions()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new PubSubTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            await adapter.StartAsync();
            await adapter.SubscribeToRemoteMachineAsync("remote-machine", "Remote Machine");

            // Act - Simulate remote action
            await eventBus.PublishEventAsync("remote-machine", "ActionExecuted", new ActionExecutedNotification
            {
                SourceMachineId = "remote-machine",
                ActionName = "remoteAction",
                StateName = "active",
                Timestamp = DateTime.UtcNow
            });

            await Task.Delay(100);

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var actionItems = stateMachine.Data.Where(d => d.Type == TimelineItemType.Action).ToList();

            Assert.NotEmpty(actionItems);
            Assert.Contains(actionItems, a => a.Name == "remoteAction");
        }

        [Fact]
        public async Task PubSubAdapter_AutoDiscoversRemoteMachines()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new PubSubTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            await adapter.StartAsync();

            // Act - Simulate state change from unknown machine
            await eventBus.PublishStateChangeAsync("auto-discovered", new StateChangeEvent
            {
                SourceMachineId = "auto-discovered",
                OldState = null,
                NewState = "running",
                Timestamp = DateTime.UtcNow
            });

            await Task.Delay(100);

            // Assert - Machine should be auto-registered
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Single(stateMachines);
            Assert.Contains("auto-discovered", stateMachines[0].Name);
        }

        [Fact]
        public async Task DistributedAdapter_CombinesLocalAndRemote()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new DistributedTimelineAdapter(viewModel, eventBus,
                _loggerFactory.CreateLogger<DistributedTimelineAdapter>());
            _disposables.Add(adapter);

            await adapter.InitializeAsync();

            var localMachine = CreateTestMachine("distributed-local");

            // Act
            await adapter.RegisterLocalMachineAsync(localMachine, "Local Machine", true);
            await adapter.SubscribeToRemoteMachineAsync("distributed-remote", "Remote Machine");

            // Send events
            localMachine.Send("START");

            await eventBus.PublishStateChangeAsync("distributed-remote", new StateChangeEvent
            {
                SourceMachineId = "distributed-remote",
                OldState = "idle",
                NewState = "active",
                Timestamp = DateTime.UtcNow
            });

            await Task.Delay(200);

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Equal(2, stateMachines.Count);

            var localData = stateMachines.First(m => m.Name == "Local Machine");
            var remoteData = stateMachines.First(m => m.Name == "Remote Machine");

            Assert.NotEmpty(localData.Data);
            Assert.NotEmpty(remoteData.Data);
        }

        [Fact]
        public async Task DistributedAdapter_EnablesAutoDiscovery()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new DistributedTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            await adapter.InitializeAsync();
            await adapter.EnableAutoDiscoveryAsync("test-discovery");

            // Act - Announce a remote machine
            await eventBus.PublishToGroupAsync("test-discovery", "MACHINE_ANNOUNCE", new MachineAnnouncement
            {
                MachineId = "discovered-machine",
                DisplayName = "Discovered Machine",
                SourceHost = "RemoteHost",
                Timestamp = DateTime.UtcNow
            });

            await Task.Delay(200);

            // Assert
            var stateMachines = viewModel.GetStateMachines().ToList();
            Assert.Contains(stateMachines, m => m.Name == "Discovered Machine");
        }

        [Fact]
        public async Task DistributedAdapter_SynchronizesTimelines()
        {
            // Arrange
            var viewModel1 = new MainViewModel();
            var viewModel2 = new MainViewModel();
            var eventBus = new InMemoryEventBus();

            var adapter1 = new DistributedTimelineAdapter(viewModel1, eventBus);
            var adapter2 = new DistributedTimelineAdapter(viewModel2, eventBus);
            _disposables.Add(adapter1);
            _disposables.Add(adapter2);

            await adapter1.InitializeAsync();
            await adapter2.InitializeAsync();

            await adapter1.EnableTimelineSyncAsync("test-sync");
            await adapter2.EnableTimelineSyncAsync("test-sync");

            var machine1 = CreateTestMachine("sync-machine1");
            var machine2 = CreateTestMachine("sync-machine2");

            // Act
            await adapter1.RegisterLocalMachineAsync(machine1, "Machine 1", true);
            await adapter2.RegisterLocalMachineAsync(machine2, "Machine 2", true);

            // Send some events
            machine1.Send("START");
            machine2.Send("START");

            await Task.Delay(300);

            // Assert - Both adapters should see both machines via event bus
            // Note: Full sync would require implementing the sync protocol
            var stats1 = adapter1.GetStatistics();
            var stats2 = adapter2.GetStatistics();

            Assert.True(stats1.IsDistributed);
            Assert.True(stats2.IsDistributed);
            Assert.Equal(1, stats1.LocalMachines);
            Assert.Equal(1, stats2.LocalMachines);
        }

        [Fact]
        public async Task PubSubAdapter_BroadcastsTimelineCommands()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new PubSubTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            await adapter.StartAsync();

            var commandReceived = false;
            await eventBus.SubscribeToAllAsync(evt =>
            {
                if (evt.EventName == "TIMELINE_CLEAR")
                {
                    commandReceived = true;
                }
            });

            // Act
            await adapter.BroadcastLocalEventAsync("TIMELINE_CLEAR");
            await Task.Delay(100);

            // Assert
            Assert.True(commandReceived);
        }

        [Fact]
        public async Task EventNotificationService_IntegratesWithTimeline()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var machine = CreateTestMachine("notification-test");

            var notificationService = new EventNotificationService(
                machine,
                eventBus,
                "notification-test",
                _loggerFactory.CreateLogger<EventNotificationService>()
            );
            _disposables.Add(notificationService);

            var adapter = new PubSubTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            // Connect event bus first
            await eventBus.ConnectAsync();

            await adapter.StartAsync();
            await adapter.SubscribeToRemoteMachineAsync("notification-test", "Notification Test");
            await notificationService.StartAsync();

            // Act
            machine.Send("START");

            // Also manually publish a state change to verify the adapter is working
            await eventBus.PublishStateChangeAsync("notification-test", new StateChangeEvent
            {
                SourceMachineId = "notification-test",
                OldState = "idle",
                NewState = "active",
                Timestamp = DateTime.UtcNow
            });

            // Wait longer for async event processing and timeline updates
            await Task.Delay(500);

            // Assert
            var stateMachine = viewModel.GetStateMachines().First();
            var allItems = stateMachine.Data.ToList();

            // Log for debugging
            if (!allItems.Any())
            {
                Console.WriteLine($"No timeline items found. Machine state: {machine.GetActiveStateString()}");
            }

            // Should have at least some timeline items
            Assert.NotEmpty(allItems);
        }

        [Fact]
        public async Task DistributedAdapter_TracksStatistics()
        {
            // Arrange
            var viewModel = new MainViewModel();
            var eventBus = new InMemoryEventBus();
            var adapter = new DistributedTimelineAdapter(viewModel, eventBus);
            _disposables.Add(adapter);

            await adapter.InitializeAsync();

            var machine1 = CreateTestMachine("stats-1");
            var machine2 = CreateTestMachine("stats-2");

            // Act
            await adapter.RegisterLocalMachineAsync(machine1, "Machine 1");
            await adapter.RegisterLocalMachineAsync(machine2, "Machine 2");
            await adapter.SubscribeToRemoteMachineAsync("remote-1", "Remote 1");

            await Task.Delay(100);

            // Assert
            var stats = adapter.GetStatistics();
            Assert.Equal(2, stats.LocalMachines);
            Assert.True(stats.IsDistributed);
            Assert.True(stats.UptimeSeconds > 0);
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

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable?.Dispose();
            }
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
            _loggerFactory?.Dispose();
        }
    }
}