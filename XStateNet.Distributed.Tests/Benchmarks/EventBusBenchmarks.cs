using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Logging;
using XStateNet;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.EventBus.Optimized;
using XStateNet.Distributed.PubSub;
using XStateNet.Distributed.PubSub.Optimized;

namespace XStateNet.Distributed.Tests.Benchmarks
{
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    [SimpleJob(RuntimeMoniker.Net80)]
    [Config(typeof(Config))]
    public class EventBusBenchmarks
    {
        private class Config : ManualConfig
        {
            public Config()
            {
                WithOptions(ConfigOptions.DisableOptimizationsValidator);
            }
        }

        private InMemoryEventBus _standardEventBus = null!;
        private OptimizedInMemoryEventBus _optimizedEventBus = null!;
        private IStateMachine _stateMachine = null!;
        private EventNotificationService _standardService = null!;
        private OptimizedEventNotificationService _optimizedService = null!;
        private List<IDisposable> _subscriptions = null!;

        [GlobalSetup]
        public void Setup()
        {
            // Create event buses
            _standardEventBus = new InMemoryEventBus();
            _optimizedEventBus = new OptimizedInMemoryEventBus(workerCount: Environment.ProcessorCount);

            // Create state machine            
            var json = @"{
                id: 'benchmark',
                initial: 'idle',
                states: {
                    idle: {
                        on: {
                            START: 'running'
                        }
                    },
                    running: {
                        entry: ['doWork'],
                        on: {
                            STOP: 'idle'
                        }
                    }
                }
            }";

            var actionMap = new ActionMap
            {
                ["doWork"] = new List<NamedAction> { new NamedAction("doWork", _ => { }) }
            };

            _stateMachine = XStateNet.StateMachine.CreateFromScript(json, guidIsolate: true, actionMap);

            // Create notification services
            _standardService = new EventNotificationService(_stateMachine, _standardEventBus, "bench-1");
            _optimizedService = new OptimizedEventNotificationService(_stateMachine, _optimizedEventBus, "bench-1");

            _subscriptions = new List<IDisposable>();

            // Connect buses
            _standardEventBus.ConnectAsync().GetAwaiter().GetResult();
            _optimizedEventBus.ConnectAsync().GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            foreach (var sub in _subscriptions)
            {
                sub?.Dispose();
            }

            _standardService?.Dispose();
            _optimizedService?.Dispose();
            _standardEventBus?.Dispose();
            _optimizedEventBus?.Dispose();
            _stateMachine?.Stop();
        }

        #region Event Publishing Benchmarks

        [Benchmark(Baseline = true)]
        public async Task StandardEventBus_PublishSingleEvent()
        {
            await _standardEventBus.PublishEventAsync("target-1", "TEST_EVENT", new { value = 42 });
        }

        [Benchmark]
        public async Task OptimizedEventBus_PublishSingleEvent()
        {
            await _optimizedEventBus.PublishEventAsync("target-1", "TEST_EVENT", new { value = 42 });
        }

        [Benchmark]
        public async Task StandardEventBus_Publish1000Events()
        {
            for (int i = 0; i < 1000; i++)
            {
                await _standardEventBus.PublishEventAsync($"target-{i % 10}", "TEST_EVENT", new { value = i });
            }
        }

        [Benchmark]
        public async Task OptimizedEventBus_Publish1000Events()
        {
            for (int i = 0; i < 1000; i++)
            {
                await _optimizedEventBus.PublishEventAsync($"target-{i % 10}", "TEST_EVENT", new { value = i });
            }
        }

        [Benchmark]
        public async Task StandardEventBus_BroadcastEvent()
        {
            await _standardEventBus.BroadcastAsync("BROADCAST_EVENT", new { timestamp = DateTime.UtcNow });
        }

        [Benchmark]
        public async Task OptimizedEventBus_BroadcastEvent()
        {
            await _optimizedEventBus.BroadcastAsync("BROADCAST_EVENT", new { timestamp = DateTime.UtcNow });
        }

        #endregion

        #region Subscription Benchmarks

        [Benchmark]
        public async Task StandardEventBus_CreateAndRemove100Subscriptions()
        {
            var subs = new List<IDisposable>();
            for (int i = 0; i < 100; i++)
            {
                var sub = await _standardEventBus.SubscribeToMachineAsync($"machine-{i}", _ => { });
                subs.Add(sub);
            }

            foreach (var sub in subs)
            {
                sub.Dispose();
            }
        }

        [Benchmark]
        public async Task OptimizedEventBus_CreateAndRemove100Subscriptions()
        {
            var subs = new List<IDisposable>();
            for (int i = 0; i < 100; i++)
            {
                var sub = await _optimizedEventBus.SubscribeToMachineAsync($"machine-{i}", _ => { });
                subs.Add(sub);
            }

            foreach (var sub in subs)
            {
                sub.Dispose();
            }
        }

        #endregion

        #region State Change Notification Benchmarks

        [Benchmark]
        public async Task StandardService_PublishStateChange()
        {
            await _standardService.PublishStateChangeAsync("idle", "running", "START");
        }

        [Benchmark]
        public async Task OptimizedService_PublishStateChange()
        {
            await _optimizedService.PublishStateChangeAsync("idle", "running", "START");
        }

        [Benchmark]
        public async Task StandardService_Publish100StateChanges()
        {
            for (int i = 0; i < 100; i++)
            {
                await _standardService.PublishStateChangeAsync("state1", "state2", $"EVENT_{i}");
            }
        }

        [Benchmark]
        public async Task OptimizedService_Publish100StateChanges()
        {
            for (int i = 0; i < 100; i++)
            {
                await _optimizedService.PublishStateChangeAsync("state1", "state2", $"EVENT_{i}");
            }
        }

        #endregion

        #region End-to-End Benchmarks

        [Benchmark]
        public async Task StandardEventBus_EndToEnd_1000Events_10Subscribers()
        {
            var received = 0;
            var completed = new TaskCompletionSource<bool>();

            // Create subscribers
            var subs = new List<IDisposable>();
            for (int i = 0; i < 10; i++)
            {
                var sub = await _standardEventBus.SubscribeToAllAsync(_ =>
                {
                    if (Interlocked.Increment(ref received) >= 10000) // 1000 events * 10 subscribers
                    {
                        completed.TrySetResult(true);
                    }
                });
                subs.Add(sub);
            }

            // Publish events
            for (int i = 0; i < 1000; i++)
            {
                await _standardEventBus.BroadcastAsync($"EVENT_{i}", new { index = i });
            }

            // Wait for all events to be received
            await Task.WhenAny(completed.Task, Task.Delay(5000));

            // Cleanup
            foreach (var sub in subs)
            {
                sub.Dispose();
            }
        }

        [Benchmark]
        public async Task OptimizedEventBus_EndToEnd_1000Events_10Subscribers()
        {
            var received = 0;
            var completed = new TaskCompletionSource<bool>();

            // Create subscribers
            var subs = new List<IDisposable>();
            for (int i = 0; i < 10; i++)
            {
                var sub = await _optimizedEventBus.SubscribeToAllAsync(_ =>
                {
                    if (Interlocked.Increment(ref received) >= 10000) // 1000 events * 10 subscribers
                    {
                        completed.TrySetResult(true);
                    }
                });
                subs.Add(sub);
            }

            // Publish events
            for (int i = 0; i < 1000; i++)
            {
                await _optimizedEventBus.BroadcastAsync($"EVENT_{i}", new { index = i });
            }

            // Wait for all events to be received
            await Task.WhenAny(completed.Task, Task.Delay(5000));

            // Cleanup
            foreach (var sub in subs)
            {
                sub.Dispose();
            }
        }

        #endregion

        #region Memory Allocation Benchmarks

        [Benchmark]
        public void StandardEventBus_AllocateAndPublish100Events()
        {
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks[i] = Task.Run(async () =>
                {
                    await _standardEventBus.PublishEventAsync($"target-{index}", $"EVENT_{index}",
                        new ConcurrentDictionary<string, object> { ["value"] = index, ["timestamp"] = DateTime.UtcNow });
                });
            }
            Task.WaitAll(tasks);
        }

        [Benchmark]
        public void OptimizedEventBus_AllocateAndPublish100Events()
        {
            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks[i] = Task.Run(async () =>
                {
                    await _optimizedEventBus.PublishEventAsync($"target-{index}", $"EVENT_{index}",
                        new ConcurrentDictionary<string, object> { ["value"] = index, ["timestamp"] = DateTime.UtcNow });
                });
            }
            Task.WaitAll(tasks);
        }

        #endregion

        #region Throughput Benchmarks

        [Benchmark]
        public async Task StandardEventBus_MaxThroughput_10Seconds()
        {
            var count = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (!cts.Token.IsCancellationRequested)
            {
                await _standardEventBus.PublishEventAsync("target", "EVENT", null);
                count++;
            }

            Console.WriteLine($"Standard: {count} events in 10 seconds");
        }

        [Benchmark]
        public async Task OptimizedEventBus_MaxThroughput_10Seconds()
        {
            var count = 0;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (!cts.Token.IsCancellationRequested)
            {
                await _optimizedEventBus.PublishEventAsync("target", "EVENT", null);
                count++;
            }

            Console.WriteLine($"Optimized: {count} events in 10 seconds");
        }

        #endregion
    }

    // NOTE: To run benchmarks, create a separate console app project and call:
    // BenchmarkRunner.Run<EventBusBenchmarks>();
    // This is commented out to avoid CS0017 error in test project

    ///// <summary>
    ///// Program to run benchmarks
    ///// </summary>
    //public class BenchmarkProgram
    //{
    //    public static void Main(string[] args)
    //    {
    //        var summary = BenchmarkRunner.Run<EventBusBenchmarks>();
    //        Console.WriteLine(summary);
    //    }
    //}
}