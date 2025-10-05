using XStateNet.Distributed.Resilience;
using Xunit;

namespace XStateNet.Distributed.Tests.Resilience
{
    public class TimeoutProtectionTests
    {
        [Fact]
        public async Task ExecuteAsync_CompletesWithinTimeout()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(1) };
            var timeoutProtection = new TimeoutProtection(options);

            // Act
            var result = await timeoutProtection.ExecuteAsync(
                async (ct) =>
                {
                    // Simulate quick operation
                    await Task.Yield();
                    return "success";
                },
                TimeSpan.FromSeconds(1)
            );

            // Assert
            Assert.Equal("success", result);
        }

        [Fact]
        public async Task ExecuteAsync_ThrowsTimeoutException()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(100) };
            var timeoutProtection = new TimeoutProtection(options);

            // Act & Assert
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await timeoutProtection.ExecuteAsync(
                    async (ct) =>
                    {
                        // Simulate operation that will timeout
                        var tcs = new TaskCompletionSource<bool>();
                        using (ct.Register(() => tcs.TrySetCanceled()))
                        {
                            await tcs.Task;
                        }
                        return "should not complete";
                    },
                    TimeSpan.FromMilliseconds(100)
                );
            });
        }

        [Fact]
        public async Task ExecuteAsync_UsesDefaultTimeout()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(100) };
            var timeoutProtection = new TimeoutProtection(options);

            // Act & Assert
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await timeoutProtection.ExecuteAsync<string>(
                    async (ct) =>
                    {
                        // Simulate operation that will timeout
                        var tcs = new TaskCompletionSource<bool>();
                        using (ct.Register(() => tcs.TrySetCanceled()))
                        {
                            await tcs.Task;
                        }
                        return "timeout";
                    }
                );
            });
        }

        [Fact]
        public async Task ExecuteAsync_CancellationTokenRespected()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(10) };
            var timeoutProtection = new TimeoutProtection(options);
            var cts = new CancellationTokenSource();

            // Act
            var task = timeoutProtection.ExecuteAsync(
                async (ct) =>
                {
                    // Wait for cancellation
                    var tcs = new TaskCompletionSource<bool>();
                    using (ct.Register(() => tcs.TrySetCanceled()))
                    {
                        await tcs.Task;
                    }
                    return "should not complete";
                },
                cancellationToken: cts.Token
            );

            cts.CancelAfter(100);

            // Assert
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        }

        [Fact]
        public async Task TryExecuteAsync_ReturnsTimeoutResult()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(100) };
            var timeoutProtection = new TimeoutProtection(options);

            // Act - Success case
            var successResult = await timeoutProtection.TryExecuteAsync(
                async (ct) =>
                {
                    await Task.Yield();
                    return "success";
                },
                TimeSpan.FromSeconds(1)
            );

            // Act - Timeout case
            var timeoutResult = await timeoutProtection.TryExecuteAsync(
                async (ct) =>
                {
                    // Simulate operation that will timeout
                    var tcs = new TaskCompletionSource<bool>();
                    using (ct.Register(() => tcs.TrySetCanceled()))
                    {
                        await tcs.Task;
                    }
                    return "timeout";
                },
                TimeSpan.FromMilliseconds(100)
            );

            // Assert
            Assert.True(successResult.IsSuccess);
            Assert.Equal("success", successResult.Value);
            Assert.Null(successResult.Exception);

            Assert.False(timeoutResult.IsSuccess);
            Assert.True(timeoutResult.IsTimeout);
            Assert.NotNull(timeoutResult.Exception);
            Assert.IsType<TimeoutException>(timeoutResult.Exception);
        }

        [Fact]
        public async Task CreateScope_AllowsMultipleOperations()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(1) };
            var timeoutProtection = new TimeoutProtection(options);

            // Act
            using (var scope = timeoutProtection.CreateScope(TimeSpan.FromSeconds(2)))
            {
                var result1 = await scope.ExecuteAsync(async (ct) =>
                {
                    await Task.Yield();
                    return "first";
                });

                var result2 = await scope.ExecuteAsync(async (ct) =>
                {
                    await Task.Yield();
                    return "second";
                });

                // Assert
                Assert.Equal("first", result1);
                Assert.Equal("second", result2);
            }
        }

        [Fact]
        public async Task CreateScope_SharesTimeoutAcrossOperations()
        {
            // Arrange - Use very short timeout to ensure deterministic behavior
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromMilliseconds(100) };
            var timeoutProtection = new TimeoutProtection(options);

            // Use TaskCompletionSource for complete control over timing
            var firstOperationTcs = new TaskCompletionSource<bool>();
            var secondOperationTcs = new TaskCompletionSource<bool>();
            var firstOperationStarted = new TaskCompletionSource<bool>();
            var secondOperationStarted = new TaskCompletionSource<bool>();

            // Act & Assert
            using (var scope = timeoutProtection.CreateScope(TimeSpan.FromMilliseconds(100)))
            {
                // First operation - completes immediately
                var firstTask = scope.ExecuteAsync(async (ct) =>
                {
                    firstOperationStarted.SetResult(true);
                    ct.Register(() => firstOperationTcs.TrySetCanceled());
                    return await Task.FromResult(true); // Complete immediately
                });

                // Wait for first operation to start and complete
                await firstOperationStarted.Task;
                var firstResult = await firstTask;
                Assert.True(firstResult);

                // Now the scope timeout is already running/expired
                // Second operation - will never complete, should timeout
                var secondTask = scope.ExecuteAsync(async (ct) =>
                {
                    secondOperationStarted.SetResult(true);
                    ct.Register(() => secondOperationTcs.TrySetCanceled());

                    // This will never complete - we won't set the result
                    return await secondOperationTcs.Task;
                });

                // Wait for second operation to start
                await secondOperationStarted.Task;

                // The scope should timeout the second operation
                // Since we're using a very short timeout (100ms) and the first operation
                // already consumed time, this should timeout quickly
                await Assert.ThrowsAsync<TimeoutException>(async () => await secondTask);

                // Verify the cancellation was triggered
                Assert.True(secondOperationTcs.Task.IsCanceled || secondOperationTcs.Task.IsFaulted);
            }
        }

        [Fact]
        public async Task ExecuteAsync_PreservesOriginalException()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(1) };
            var timeoutProtection = new TimeoutProtection(options);
            var expectedException = new InvalidOperationException("Test error");

            // Act & Assert
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await timeoutProtection.ExecuteAsync<string>(
                    async (ct) =>
                    {
                        await Task.Yield();
                        throw expectedException;
                    }
                );
            });

            Assert.Equal("Test error", actualException.Message);
        }

        [Fact]
        public async Task ConcurrentExecutions_ThreadSafe()
        {
            // Arrange
            var options = new TimeoutOptions { DefaultTimeout = TimeSpan.FromSeconds(1) };
            var timeoutProtection = new TimeoutProtection(options);
            var successCount = 0;
            var timeoutCount = 0;

            // Act
            var tasks = new Task[50];
            for (int i = 0; i < 50; i++)
            {
                var index = i;
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        var result = await timeoutProtection.ExecuteAsync(
                            async (ct) =>
                            {
                                // Some operations are slow
                                // Some operations are slow
                                if (index % 5 == 0)
                                {
                                    // Simulate slow operation that will timeout
                                    var tcs = new TaskCompletionSource<bool>();
                                    using (ct.Register(() => tcs.TrySetCanceled()))
                                    {
                                        await tcs.Task;
                                    }
                                }
                                else
                                {
                                    // Fast operation
                                    await Task.Yield();
                                }
                                return index;
                            },
                            TimeSpan.FromMilliseconds(100)
                        );
                        Interlocked.Increment(ref successCount);
                    }
                    catch (TimeoutException)
                    {
                        Interlocked.Increment(ref timeoutCount);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.True(successCount > 0);
            Assert.True(timeoutCount > 0);
            Assert.Equal(50, successCount + timeoutCount);
        }
    }
}