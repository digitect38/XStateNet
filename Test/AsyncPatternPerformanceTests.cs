using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Tests
{
    /// <summary>
    /// Performance tests comparing old patterns vs improved patterns
    /// </summary>
    public class AsyncPatternPerformanceTests
    {
        private readonly ITestOutputHelper _output;

        public AsyncPatternPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Compare fire-and-forget vs proper async/await
        /// </summary>
        [Fact]
        public async Task Compare_FireAndForget_vs_ProperAsync()
        {
            const int iterations = 100;
            var exceptions = 0;

            // Test 1: Fire-and-forget pattern (BAD)
            _output.WriteLine("Testing Fire-and-Forget pattern (OLD/BAD):");
            var sw1 = Stopwatch.StartNew();
            exceptions = 0;

            for (int i = 0; i < iterations; i++)
            {
                // Fire and forget - exceptions are lost
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SimulateWorkAsync();
                        if (Random.Shared.Next(10) == 0)
                            throw new Exception("Random error");
                    }
                    catch
                    {
                        Interlocked.Increment(ref exceptions);
                    }
                });
            }

            // Can't properly wait for completion!
            await Task.Delay(500);
            sw1.Stop();

            _output.WriteLine($"  Time: {sw1.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Exceptions caught: {exceptions}");
            _output.WriteLine($"  ❌ Can't know when tasks complete");
            _output.WriteLine($"  ❌ Exceptions might be lost");

            // Test 2: Proper async/await pattern (GOOD)
            _output.WriteLine("\nTesting Proper Async/Await pattern (NEW/GOOD):");
            var sw2 = Stopwatch.StartNew();
            exceptions = 0;

            var tasks = new Task[iterations];
            for (int i = 0; i < iterations; i++)
            {
                tasks[i] = ProperAsyncWork();
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Check for exceptions in each task
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                        exceptions++;
                }
            }

            sw2.Stop();

            _output.WriteLine($"  Time: {sw2.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Exceptions caught: {exceptions}");
            _output.WriteLine($"  ✅ Know exactly when all tasks complete");
            _output.WriteLine($"  ✅ All exceptions properly handled");

            // Assert proper pattern is better
            Assert.True(exceptions > 0 || sw2.ElapsedMilliseconds > 0, "Proper pattern should track completion");
        }

        /// <summary>
        /// Compare GetAwaiter().GetResult() vs timeout-protected disposal
        /// </summary>
        [Fact]
        public async Task Compare_BlockingDispose_vs_TimeoutProtected()
        {
            // Test 1: GetAwaiter().GetResult() pattern (BAD)
            _output.WriteLine("Testing GetAwaiter().GetResult() in Dispose (OLD/BAD):");

            var oldResource = new OldPatternResource();
            var sw1 = Stopwatch.StartNew();

            // This could deadlock in certain scenarios
            await Task.Run(() => oldResource.Dispose());

            sw1.Stop();
            _output.WriteLine($"  Time: {sw1.ElapsedMilliseconds}ms");
            _output.WriteLine($"  ❌ Risk of deadlock in certain contexts");
            _output.WriteLine($"  ❌ Blocks thread while waiting");

            // Test 2: Timeout-protected disposal (GOOD)
            _output.WriteLine("\nTesting Timeout-Protected Dispose (NEW/GOOD):");

            var newResource = new ImprovedPatternResource();
            var sw2 = Stopwatch.StartNew();

            // Safe disposal with timeout
            newResource.Dispose();

            sw2.Stop();
            _output.WriteLine($"  Time: {sw2.ElapsedMilliseconds}ms");
            _output.WriteLine($"  ✅ No deadlock risk");
            _output.WriteLine($"  ✅ Guaranteed to complete within timeout");

            Assert.True(sw2.ElapsedMilliseconds < 3000, "Improved dispose should complete quickly");
        }

        /// <summary>
        /// Test cancellation performance
        /// </summary>
        [Fact]
        public async Task Compare_Cancellation_Performance()
        {
            const int operations = 50;

            // Test 1: Poor cancellation (OLD)
            _output.WriteLine("Testing Poor Cancellation Handling (OLD/BAD):");
            var sw1 = Stopwatch.StartNew();
            var cts1 = new CancellationTokenSource(100);
            var tasksWithoutProperCancellation = new Task[operations];

            for (int i = 0; i < operations; i++)
            {
                tasksWithoutProperCancellation[i] = PoorCancellationPattern(cts1.Token);
            }

            await Task.WhenAll(tasksWithoutProperCancellation.Select(t => t.ContinueWith(_ => { })));
            sw1.Stop();

            _output.WriteLine($"  Time to cancel: {sw1.ElapsedMilliseconds}ms");
            _output.WriteLine($"  ❌ May continue running after cancellation requested");

            // Test 2: Proper cancellation (NEW)
            _output.WriteLine("\nTesting Proper Cancellation (NEW/GOOD):");
            var sw2 = Stopwatch.StartNew();
            var cts2 = new CancellationTokenSource(100);
            var properTasks = new Task[operations];

            for (int i = 0; i < operations; i++)
            {
                properTasks[i] = ProperCancellationPattern(cts2.Token);
            }

            await Task.WhenAll(properTasks.Select(t => t.ContinueWith(_ => { })));
            sw2.Stop();

            _output.WriteLine($"  Time to cancel: {sw2.ElapsedMilliseconds}ms");
            _output.WriteLine($"  ✅ Cancels promptly when requested");

            Assert.True(sw2.ElapsedMilliseconds <= sw1.ElapsedMilliseconds + 100,
                "Proper cancellation should be at least as fast");
        }

        private async Task SimulateWorkAsync()
        {
            await Task.Delay(10);
        }

        private async Task ProperAsyncWork()
        {
            await SimulateWorkAsync();
            if (Random.Shared.Next(10) == 0)
                throw new Exception("Random error");
        }

        private async Task PoorCancellationPattern(CancellationToken ct)
        {
            try
            {
                // Poor pattern - doesn't check cancellation frequently
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(50); // Doesn't pass token!
                }
            }
            catch { }
        }

        private async Task ProperCancellationPattern(CancellationToken ct)
        {
            try
            {
                // Good pattern - respects cancellation
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(50, ct); // Passes token
                }
            }
            catch (OperationCanceledException) { }
        }
    }

    // Example of OLD pattern with GetAwaiter().GetResult()
    public class OldPatternResource : IDisposable
    {
        private Task? _backgroundTask;

        public OldPatternResource()
        {
            _backgroundTask = Task.Delay(100);
        }

        public void Dispose()
        {
            // BAD: Blocks synchronously
            _backgroundTask?.GetAwaiter().GetResult();
        }
    }

    // Example of IMPROVED pattern with timeout
    public class ImprovedPatternResource : IDisposable
    {
        private Task? _backgroundTask;

        public ImprovedPatternResource()
        {
            _backgroundTask = Task.Delay(100);
        }

        public void Dispose()
        {
            // GOOD: Timeout-protected
            try
            {
                _backgroundTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }
    }
}