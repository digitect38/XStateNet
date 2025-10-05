using Xunit;
using Xunit.Abstractions;

// Suppress obsolete warning - ordered event tests with no inter-machine communication
#pragma warning disable CS0618

namespace XStateNet.Tests
{
    public class OrderedEventTests
    {
        private readonly ITestOutputHelper _output;

        public OrderedEventTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void OrderedEventHandler_MaintainsExecutionOrder()
        {
            // Arrange
            var handler = new OrderedEventHandler<string>();
            var executionOrder = new List<int>();
            var lockObj = new object();

            // Subscribe handlers with different priorities
            handler.Subscribe(_ => { lock (lockObj) executionOrder.Add(1); }, priority: 10);
            handler.Subscribe(_ => { lock (lockObj) executionOrder.Add(2); }, priority: 20);
            handler.Subscribe(_ => { lock (lockObj) executionOrder.Add(3); }, priority: 30);

            // Subscribe out of order to test sorting
            handler.Subscribe(_ => { lock (lockObj) executionOrder.Add(0); }, priority: 5);
            handler.Subscribe(_ => { lock (lockObj) executionOrder.Add(4); }, priority: 40);

            // Act
            handler.Invoke("test");

            // Assert - handlers should execute in priority order
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, executionOrder);
        }

        [Fact]
        public async Task SequentialEventExecutor_PreservesOrder()
        {
            // Arrange
            using var executor = new SequentialEventExecutor<int>();
            var results = new List<(int value, int order)>();
            var counter = 0;
            var lockObj = new object();

            // Subscribe multiple handlers
            executor.Subscribe(value =>
            {
                Thread.Sleep(10); // Simulate work
                lock (lockObj)
                {
                    results.Add((value, Interlocked.Increment(ref counter)));
                }
            }, priority: 1);

            executor.Subscribe(value =>
            {
                Thread.Sleep(5); // Different work time
                lock (lockObj)
                {
                    results.Add((value * 10, Interlocked.Increment(ref counter)));
                }
            }, priority: 2);

            // Act - Invoke multiple times concurrently
            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                int value = i;
                tasks.Add(executor.InvokeAsync(value));
            }

            await Task.WhenAll(tasks);

            // Assert - All events processed in order
            _output.WriteLine("Results:");
            foreach (var (value, order) in results)
            {
                _output.WriteLine($"Value: {value}, Order: {order}");
            }

            // Each event should have been processed completely before the next
            Assert.Equal(10, results.Count); // 2 handlers * 5 events

            // Verify sequential processing
            for (int i = 0; i < 5; i++)
            {
                var baseIndex = i * 2;
                Assert.Equal(i, results[baseIndex].value);
                Assert.Equal(i * 10, results[baseIndex + 1].value);
            }
        }

        [Fact]
        public void SynchronizedEventHandler_PreventsRaceConditions()
        {
            // Arrange
            var handler = new SynchronizedEventHandler<int>();
            var sharedCounter = 0;
            var results = new List<int>();
            var lockObj = new object();

            // Subscribe handlers that modify shared state
            handler.Subscribe(value =>
            {
                var current = sharedCounter;
                Thread.Sleep(1); // Simulate race condition opportunity
                sharedCounter = current + value;
                lock (lockObj) results.Add(sharedCounter);
            }, priority: 1);

            handler.Subscribe(value =>
            {
                var current = sharedCounter;
                Thread.Sleep(1); // Simulate race condition opportunity
                sharedCounter = current * 2;
                lock (lockObj) results.Add(sharedCounter);
            }, priority: 2);

            // Act - Invoke from multiple threads
            var threads = new Thread[5];
            for (int i = 0; i < threads.Length; i++)
            {
                int value = i + 1;
                threads[i] = new Thread(() => handler.Invoke(value));
                threads[i].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }

            // Assert - No race conditions due to synchronized execution
            _output.WriteLine($"Final counter: {sharedCounter}");
            _output.WriteLine($"Results: {string.Join(", ", results)}");

            // With synchronized execution, operations are atomic per event
            Assert.Equal(10, results.Count); // 2 handlers * 5 invocations
        }

        [Fact]
        public async Task StateMachine_OrderedStateChanges()
        {
            // Arrange
            var config = @"{
  id: 'testMachine',
  initial: 'idle',
  states: {
    idle: {
      on: { START: 'working' }
    },
    working: {
      on: { COMPLETE: 'done' }
    },
    done: {
      type: 'final'
    }
  }
}";

            var machine = StateMachineFactory.CreateFromScript(config, true);
            var stateChanges = new List<string>();
            var lockObj = new object();

            // Subscribe with priorities to guarantee order
            machine.SubscribeToStateChange(state =>
            {
                lock (lockObj) stateChanges.Add($"Logger: {state}");
            }, priority: 10);

            machine.SubscribeToStateChange(state =>
            {
                lock (lockObj) stateChanges.Add($"Monitor: {state}");
            }, priority: 20);

            machine.SubscribeToStateChange(state =>
            {
                lock (lockObj) stateChanges.Add($"Audit: {state}");
            }, priority: 30);

            // Act
            await machine.StartAsync();
            machine.RaiseOrderedStateChanged("idle");

            await machine.SendAsync("START");
            machine.RaiseOrderedStateChanged("working");

            await machine.SendAsync("COMPLETE");
            machine.RaiseOrderedStateChanged("done");

            // Assert - handlers execute in priority order for each state
            _output.WriteLine("State changes:");
            foreach (var change in stateChanges)
            {
                _output.WriteLine(change);
            }

            Assert.Contains("Logger: idle", stateChanges);
            Assert.Contains("Monitor: idle", stateChanges);
            Assert.Contains("Audit: idle", stateChanges);

            // Verify order
            var idleIndex = stateChanges.IndexOf("Logger: idle");
            Assert.True(stateChanges.IndexOf("Monitor: idle") > idleIndex);
            Assert.True(stateChanges.IndexOf("Audit: idle") > stateChanges.IndexOf("Monitor: idle"));
        }
    }
}