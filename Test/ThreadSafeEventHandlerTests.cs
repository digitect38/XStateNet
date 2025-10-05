using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Tests
{
    public class ThreadSafeEventHandlerTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly List<IDisposable> _disposables = new();

        public ThreadSafeEventHandlerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Subscribe_And_Invoke_Works()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            _disposables.Add(handler);
            var result = "";

            // Act
            using (handler.Subscribe(s => result = s))
            {
                handler.Invoke("test");
            }

            // Assert
            Assert.Equal("test", result);
        }

        [Fact]
        public void Multiple_Handlers_Execute_In_Priority_Order()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<int>();
            _disposables.Add(handler);
            var executionOrder = new List<int>();

            // Subscribe with different priorities
            handler.SubscribeWithPriority(x => executionOrder.Add(2), priority: 20, name: "Second");
            handler.SubscribeWithPriority(x => executionOrder.Add(1), priority: 10, name: "First");
            handler.SubscribeWithPriority(x => executionOrder.Add(3), priority: 30, name: "Third");

            // Act
            handler.Invoke(100);

            // Assert
            Assert.Equal(new[] { 1, 2, 3 }, executionOrder);
            _output.WriteLine($"Execution order: {string.Join(", ", executionOrder)}");
        }

        [Fact]
        public void Concurrent_Invocations_Are_Serialized()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<int>();
            _disposables.Add(handler);
            var results = new List<(int threadId, int value, DateTime time)>();
            var lockObj = new object();

            handler.Subscribe(value =>
            {
                var threadId = Thread.CurrentThread.ManagedThreadId;
                Thread.Sleep(50); // Simulate work

                lock (lockObj)
                {
                    results.Add((threadId, value, DateTime.UtcNow));
                }
            });

            // Act - invoke from multiple threads
            var tasks = Enumerable.Range(0, 5).Select(i =>
                Task.Run(() => handler.Invoke(i))
            ).ToArray();

            Task.WaitAll(tasks);

            // Assert - executions should not overlap
            Assert.Equal(5, results.Count);

            for (int i = 1; i < results.Count; i++)
            {
                var timeDiff = (results[i].time - results[i - 1].time).TotalMilliseconds;
                Assert.True(timeDiff >= 45, $"Time difference {timeDiff}ms indicates overlapping execution");

                _output.WriteLine($"Thread {results[i].threadId}: Value={results[i].value}, " +
                    $"TimeDiff={timeDiff:F1}ms");
            }
        }

        [Fact]
        public void TryInvoke_Returns_False_When_Already_Executing()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            _disposables.Add(handler);
            var barrier = new ManualResetEventSlim(false);
            var tryInvokeResult = true;

            handler.Subscribe(s =>
            {
                barrier.Set();
                Thread.Sleep(100); // Hold the lock
            });

            // Act
            var invokeTask = Task.Run(() => handler.Invoke("blocking"));
            barrier.Wait(); // Wait for first invocation to start

            tryInvokeResult = handler.TryInvoke("should fail");

            invokeTask.Wait();

            // Assert
            Assert.False(tryInvokeResult, "TryInvoke should return false when handler is already executing");
        }

        [Fact]
        public void InvokeWithTimeout_Respects_Timeout()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            _disposables.Add(handler);

            handler.Subscribe(s =>
            {
                Thread.Sleep(500); // Simulate long-running handler
            });

            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = handler.InvokeWithTimeout("test", TimeSpan.FromMilliseconds(100));
            sw.Stop();

            // Assert
            Assert.False(result, "Should timeout");
            Assert.True(sw.ElapsedMilliseconds < 200, "Should return quickly after timeout");
            _output.WriteLine($"Timeout after {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void Handler_Exceptions_Do_Not_Break_Chain()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            _disposables.Add(handler);
            var results = new List<string>();
            var errors = new List<Exception>();

            handler.HandlerError += (sender, args) =>
            {
                errors.Add(args.Exception);
            };

            handler.Subscribe(s => results.Add("before"), "Before");
            handler.Subscribe(s => throw new InvalidOperationException("Test error"), "Failing");
            handler.Subscribe(s => results.Add("after"), "After");

            // Act
            handler.Invoke("test");

            // Assert
            Assert.Equal(new[] { "before", "after" }, results);
            Assert.Single(errors);
            Assert.IsType<InvalidOperationException>(errors[0]);
        }

        [Fact]
        public void Dispose_Prevents_Further_Operations()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            var invoked = false;

            handler.Subscribe(s => invoked = true);

            // Act
            handler.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => handler.Invoke("test"));
            Assert.Throws<ObjectDisposedException>(() => handler.Subscribe(s => { }));
            Assert.False(invoked);
        }

        [Fact]
        public void Unsubscribe_Removes_Handler()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<int>();
            _disposables.Add(handler);
            var count = 0;

            var subscription = handler.Subscribe(x => count++);

            // Act
            handler.Invoke(1);
            Assert.Equal(1, count);

            subscription.Dispose();
            handler.Invoke(2);

            // Assert
            Assert.Equal(1, count); // Should not increment after unsubscribe
        }

        [Fact]
        public void Clear_Removes_All_Handlers()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            _disposables.Add(handler);
            var count = 0;

            handler.Subscribe(s => count++);
            handler.Subscribe(s => count++);
            handler.Subscribe(s => count++);

            // Act
            handler.Invoke("test");
            Assert.Equal(3, count);

            handler.Clear();
            handler.Invoke("test2");

            // Assert
            Assert.Equal(3, count); // Should not increment after clear
            Assert.Equal(0, handler.HandlerCount);
        }

        [Fact]
        public void HandlerCount_And_HasHandlers_Work_Correctly()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            _disposables.Add(handler);

            // Assert initial state
            Assert.Equal(0, handler.HandlerCount);
            Assert.False(handler.HasHandlers);

            // Act & Assert - add handlers
            var sub1 = handler.Subscribe(s => { });
            Assert.Equal(1, handler.HandlerCount);
            Assert.True(handler.HasHandlers);

            var sub2 = handler.Subscribe(s => { });
            Assert.Equal(2, handler.HandlerCount);

            // Act & Assert - remove handlers
            sub1.Dispose();
            Assert.Equal(1, handler.HandlerCount);

            sub2.Dispose();
            Assert.Equal(0, handler.HandlerCount);
            Assert.False(handler.HasHandlers);
        }

        [Fact]
        public void Same_Priority_Maintains_Subscription_Order()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<string>();
            _disposables.Add(handler);
            var results = new List<int>();

            // Subscribe multiple handlers with same priority
            for (int i = 0; i < 5; i++)
            {
                int capturedIndex = i;
                handler.SubscribeWithPriority(s => results.Add(capturedIndex), priority: 100);
            }

            // Act
            handler.Invoke("test");

            // Assert - should maintain FIFO order for same priority
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, results);
        }

        [Fact]
        public async Task Stress_Test_High_Concurrency()
        {
            // Arrange
            var handler = new ThreadSafeEventHandler<int>();
            _disposables.Add(handler);
            var counter = 0;
            var errors = new List<Exception>();

            handler.Subscribe(x => Interlocked.Increment(ref counter));
            handler.HandlerError += (s, e) => errors.Add(e.Exception);

            // Act - many concurrent invocations
            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                int value = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        handler.Invoke(value * 10 + j);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(1000, counter); // 100 tasks * 10 invocations
            Assert.Empty(errors);
            _output.WriteLine($"Processed {counter} events without errors");
        }

        [Fact]
        public void ToSynchronized_Extension_Works()
        {
            // Arrange
            Action<string>? multicast = null;
            multicast += s => _output.WriteLine($"Handler1: {s}");
            multicast += s => _output.WriteLine($"Handler2: {s}");
            multicast += s => _output.WriteLine($"Handler3: {s}");

            // Act
            using var synchronized = multicast.ToSynchronized();

            var count = 0;
            synchronized.Subscribe(s => count++);

            synchronized.Invoke("test");

            // Assert
            Assert.Equal(4, synchronized.HandlerCount); // 3 from multicast + 1 added
            Assert.Equal(1, count);
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable?.Dispose();
            }
        }
    }
}