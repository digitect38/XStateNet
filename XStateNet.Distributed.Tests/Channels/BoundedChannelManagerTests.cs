using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet.Distributed.Channels;
using Moq;

namespace XStateNet.Distributed.Tests.Channels
{
    public class BoundedChannelManagerTests
    {
        [Fact]
        public async Task WriteAsync_And_ReadAsync_SingleItem()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<string>("test", options);

            // Act
            await channel.WriteAsync("test-message");
            var (success, item) = await channel.ReadAsync();

            // Assert
            Assert.True(success);
            Assert.Equal("test-message", item);
        }

        [Fact]
        public async Task WriteAsync_ReturnsTrue_WhenFull_WithDropStrategy()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions
            {
                Capacity = 2,
                FullMode = ChannelFullMode.DropNewest
            };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act - Fill the channel
            Assert.True(await channel.WriteAsync(1));
            Assert.True(await channel.WriteAsync(2));

            // Try to write when full - should succeed but drop the item
            var result = await channel.WriteAsync(3);

            // Assert - DropNewest allows writes to succeed
            Assert.True(result);

            // Verify that we still only have 2 items (the 3rd was dropped)
            var stats = channel.GetStatistics();
            Assert.Equal(3, stats.TotalItemsWritten);
            Assert.Equal(1, stats.TotalItemsDropped);
        }

        [Fact]
        public async Task WriteAsync_Waits_WhenFull_WithWaitStrategy()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions
            {
                Capacity = 2,
                FullMode = ChannelFullMode.Wait
            };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act - Fill the channel
            await channel.WriteAsync(1);
            await channel.WriteAsync(2);

            // Start write that will wait
            var writeTask = channel.WriteAsync(3);
            Assert.False(writeTask.IsCompleted);

            // Read one item to make space
            var (success, item) = await channel.ReadAsync();
            Assert.True(success);
            Assert.Equal(1, item);

            // Now the write should complete
            var writeResult = await writeTask;
            Assert.True(writeResult);
        }

        [Fact]
        public async Task ReadBatchAsync_ReturnsAvailableItems()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act - Write some items
            for (int i = 1; i <= 5; i++)
            {
                await channel.WriteAsync(i);
            }

            // Read batch
            var batch = await channel.ReadBatchAsync(10, TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal(5, batch.Count);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, batch);
        }

        [Fact]
        public async Task ReadBatchAsync_RespectsMaxCount()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act - Write many items
            for (int i = 1; i <= 8; i++)
            {
                await channel.WriteAsync(i);
            }

            // Read limited batch
            var batch = await channel.ReadBatchAsync(3, TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal(3, batch.Count);
            Assert.Equal(new[] { 1, 2, 3 }, batch);

            // Remaining items should still be available
            var remaining = await channel.ReadBatchAsync(10, TimeSpan.FromMilliseconds(100));
            Assert.Equal(5, remaining.Count);
        }

        [Fact]
        public async Task ReadBatchAsync_WaitsForTimeout()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var batch = await channel.ReadBatchAsync(5, TimeSpan.FromMilliseconds(100));
            stopwatch.Stop();

            // Assert
            Assert.Empty(batch);
            Assert.True(stopwatch.ElapsedMilliseconds >= 90); // Allow some variance
        }

        [Fact]
        public async Task TryReadAsync_ReturnsValue_WhenAvailable()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<string>("test", options);
            await channel.WriteAsync("test");

            // Act
            var (success, value) = await channel.ReadAsync();

            // Assert
            Assert.True(success);
            Assert.Equal("test", value);
        }

        [Fact]
        public async Task TryReadAsync_ReturnsFalse_WhenEmpty()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<string>("test", options);

            // Act - Use a timeout to avoid hanging
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try
            {
                var (success, value) = await channel.ReadAsync(cts.Token);

                // Should not reach here on empty channel
                Assert.False(success);
                Assert.Null(value);
            }
            catch (OperationCanceledException)
            {
                // Expected - channel is empty and read timed out
                Assert.True(true);
            }
        }

        [Fact]
        public async Task GetStatistics_ReturnsAccurateData()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions
            {
                Capacity = 5,
                FullMode = ChannelFullMode.DropNewest,
                EnableMonitoring = true
            };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act
            for (int i = 1; i <= 5; i++)
            {
                await channel.WriteAsync(i);
            }

            // Try to write when full (should drop)
            var writeResult = await channel.WriteAsync(6);
            Assert.True(writeResult); // Should succeed even when full with DropNewest

            // Read some items
            var read1 = await channel.ReadAsync();
            var read2 = await channel.ReadAsync();

            // Count remaining items
            int remainingCount = 0;
            var remainingItems = new List<int>();
            while (channel.TryRead(out var item))
            {
                remainingItems.Add(item);
                remainingCount++;
            }

            var stats = channel.GetStatistics();

            // The actual behavior: DropNewest in .NET drops an item when writing beyond capacity
            Assert.Equal(6, stats.TotalItemsWritten); // 6 writes succeeded
            Assert.Equal(1, stats.TotalItemsDropped); // We track that 1 item was dropped
            Assert.Equal(2 + remainingCount, stats.TotalItemsRead); // Initial 2 reads + remaining
            Assert.Equal(0, stats.CurrentDepth); // Should be empty after reading all
            Assert.Equal(5, stats.Capacity);

            // Verify the actual items (1,2,3,4,6 with 5 dropped)
            Assert.Equal(1, read1.Item);
            Assert.Equal(2, read2.Item);
            Assert.Equal(new List<int> { 3, 4, 6 }, remainingItems);
        }

        [Fact]
        public async Task Complete_PreventsNewWrites()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<string>("test", options);

            // Act
            await channel.WriteAsync("item1");
            channel.Complete();

            // Try to write after closing
            var writeResult = await channel.WriteAsync("item2");

            // Assert
            Assert.False(writeResult);

            // But we can still read existing items
            var (success, item) = await channel.ReadAsync();
            Assert.True(success);
            Assert.Equal("item1", item);
        }

        [Fact]
        public async Task Throttle_BackpressureStrategy()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions
            {
                Capacity = 2,
                BackpressureStrategy = BackpressureStrategy.Throttle,
                EnableCustomBackpressure = true,
                FullMode = ChannelFullMode.DropNewest // Allow writes when full by dropping
            };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act - Fill channel
            await channel.WriteAsync(1);
            await channel.WriteAsync(2);

            // Write with throttle
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await channel.WriteAsync(3); // Should throttle
            stopwatch.Stop();

            // Assert
            Assert.True(stopwatch.ElapsedMilliseconds >= 10); // Default throttle is 10ms
        }

        [Fact]
        public async Task ConcurrentWriteRead_ThreadSafe()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions
            {
                Capacity = 100,
                FullMode = ChannelFullMode.Wait
            };
            var channel = new BoundedChannelManager<int>("test", options);
            var writtenItems = new HashSet<int>();
            var readItems = new List<int>();
            var itemCount = 1000;

            // Act - Concurrent writes
            var writeTask = Task.Run(async () =>
            {
                for (int i = 0; i < itemCount; i++)
                {
                    if (await channel.WriteAsync(i))
                    {
                        lock (writtenItems)
                        {
                            writtenItems.Add(i);
                        }
                    }
                }
                channel.Complete();
            });

            // Concurrent reads
            var readTask = Task.Run(async () =>
            {
                await foreach (var item in channel.ReadAllAsync())
                {
                    lock (readItems)
                    {
                        readItems.Add(item);
                    }
                }
            });

            await Task.WhenAll(writeTask, readTask);

            // Assert
            Assert.Equal(itemCount, writtenItems.Count);
            Assert.Equal(itemCount, readItems.Count);
            Assert.Equal(writtenItems.OrderBy(x => x), readItems.OrderBy(x => x));
        }

        [Fact]
        public async Task Metrics_BackpressureApplied()
        {
            // Arrange
            var metricsMock = new Mock<IChannelMetrics>();
            var options = new CustomBoundedChannelOptions
            {
                Capacity = 1,
                BackpressureStrategy = BackpressureStrategy.Drop,
                EnableCustomBackpressure = true
            };
            var channel = new BoundedChannelManager<int>("test", options, metrics: metricsMock.Object);

            // Act
            await channel.WriteAsync(1);
            await channel.WriteAsync(2); // Should trigger backpressure

            // Assert
            metricsMock.Verify(m => m.RecordBackpressure("test"), Times.Once);
        }

        [Fact]
        public async Task Redirect_BackpressureStrategy()
        {
            // Arrange
            var redirectChannel = new BoundedChannelManager<object>("redirect",
                new CustomBoundedChannelOptions { Capacity = 10 });

            var options = new CustomBoundedChannelOptions
            {
                Capacity = 2,
                BackpressureStrategy = BackpressureStrategy.Redirect,
                OverflowChannel = redirectChannel,
                EnableCustomBackpressure = true
            };
            var channel = new BoundedChannelManager<int>("main", options);

            // Act - Fill main channel
            await channel.WriteAsync(1);
            await channel.WriteAsync(2);

            // This should redirect
            await channel.WriteAsync(3);

            // Assert
            var (success, redirected) = await redirectChannel.ReadAsync();
            Assert.True(success);
            Assert.Equal(3, redirected);
        }

        [Fact]
        public void Dispose_CleansUpResources()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions { Capacity = 10 };
            var channel = new BoundedChannelManager<string>("test", options);

            // Act
            channel.Dispose();
            channel.Dispose(); // Should not throw on second dispose

            // Assert - No exception thrown
            Assert.True(true);
        }
    }
}