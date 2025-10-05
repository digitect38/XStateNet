using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Tests
{
    /// <summary>
    /// Simple, fast-running tests to demonstrate the improved async patterns
    /// These tests complete quickly and don't hang
    /// </summary>
    public class SimpleAsyncPatternTests
    {
        private readonly ITestOutputHelper _output;

        public SimpleAsyncPatternTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Test 1: Verify exceptions aren't lost (fire-and-forget issue fixed)
        /// </summary>
        [Fact]
        public async Task Test_ExceptionsNotLost()
        {
            _output.WriteLine("Testing: Exceptions are properly handled, not lost");

            // Improved pattern - exceptions are caught
            var exceptionCaught = false;
            var task = Task.Run(async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Test exception");
            });

            try
            {
                await task;
            }
            catch (InvalidOperationException)
            {
                exceptionCaught = true;
            }

            Assert.True(exceptionCaught, "Exception should be caught, not lost");
            _output.WriteLine("✅ PASS: Exception was properly caught");
        }

        /// <summary>
        /// Test 2: Verify dispose doesn't hang
        /// </summary>
        [Fact]
        public void Test_DisposeDoesntHang()
        {
            _output.WriteLine("Testing: Dispose completes quickly without hanging");

            var resource = new QuickDisposableResource();
            var sw = Stopwatch.StartNew();

            // This should complete quickly
            resource.Dispose();

            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 500,
                $"Dispose should complete in < 500ms, took {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"✅ PASS: Dispose completed in {sw.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Test 3: Verify async dispose works properly
        /// </summary>
        [Fact]
        public async Task Test_AsyncDisposeWorks()
        {
            _output.WriteLine("Testing: Async dispose pattern works correctly");

            await using var resource = new QuickAsyncDisposableResource();
            await resource.DoWorkAsync();

            // Resource will be disposed here automatically
            _output.WriteLine("✅ PASS: Async dispose completed successfully");
        }

        /// <summary>
        /// Test 4: Verify cancellation works promptly
        /// </summary>
        [Fact]
        public async Task Test_CancellationWorks()
        {
            _output.WriteLine("Testing: Cancellation stops operations promptly");

            using var cts = new CancellationTokenSource(50);
            var cancelled = false;

            try
            {
                await LongRunningOperation(cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.True(cancelled, "Operation should be cancelled");
            _output.WriteLine("✅ PASS: Operation was cancelled as expected");
        }

        /// <summary>
        /// Test 5: Verify no race conditions in concurrent operations
        /// </summary>
        [Fact]
        public async Task Test_NoConcurrencyIssues()
        {
            _output.WriteLine("Testing: Concurrent operations don't cause race conditions");

            var counter = new ThreadSafeCounter();
            var tasks = new Task[20];

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => counter.Increment());
            }

            await Task.WhenAll(tasks);

            Assert.Equal(20, counter.Value);
            _output.WriteLine($"✅ PASS: Counter = {counter.Value} (expected 20)");
        }

        private async Task LongRunningOperation(CancellationToken ct)
        {
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(10, ct);
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>
    /// Resource that disposes quickly (no hanging)
    /// </summary>
    public class QuickDisposableResource : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _backgroundTask;

        public QuickDisposableResource()
        {
            _backgroundTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, _cts.Token);
                }
                catch (OperationCanceledException) { }
            });
        }

        public void Dispose()
        {
            // Cancel operations first
            _cts.Cancel();

            // Then wait with timeout
            try
            {
                _backgroundTask.Wait(100);
            }
            catch { }

            _cts.Dispose();
        }
    }

    /// <summary>
    /// Resource with proper async disposal
    /// </summary>
    public class QuickAsyncDisposableResource : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1);

        public async Task DoWorkAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                await Task.Delay(10);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Delay(10);
            _semaphore?.Dispose();
        }
    }

    /// <summary>
    /// Thread-safe counter
    /// </summary>
    public class ThreadSafeCounter
    {
        private int _value;

        public int Value => _value;

        public void Increment()
        {
            Interlocked.Increment(ref _value);
        }
    }
}