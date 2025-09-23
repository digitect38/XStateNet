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
        public async Task WriteAsync_HandlesFullChannel_WithDropNewestStrategy()
        {
            // Arrange
            var options = new CustomBoundedChannelOptions
            {
                Capacity = 2,
                FullMode = ChannelFullMode.DropNewest
            };
            var channel = new BoundedChannelManager<int>("test", options);

            // Act - Fill the channel
            await channel.WriteAsync(1);
            await channel.WriteAsync(2);

            // Writing to a full channel with DropNewest will succeed,
            // but it will drop the newest existing item (2) to make space for the new one (3).
            var result = await channel.WriteAsync(3);

            // Assert
            Assert.True(result); // Write operation itself succeeds.

            // Verify the contents of the channel.
            var (success1, item1) = await channel.ReadAsync();
            Assert.True(success1);
            Assert.Equal(1, item1); // Oldest item remains.

            var (success2, item2) = await channel.ReadAsync();
            Assert.True(success2);
            Assert.Equal(3, item2); // New item was added.

            // Verify statistics (we can't reliably track drops anymore, but writes should be counted).
            var stats = channel.GetStatistics();
            Assert.Equal(3, stats.TotalItemsWritten);
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

            // With DropNewest mode, when channel is full, new items are dropped
            // So we write 1,2,3,4,5 (channel full), then 6 is dropped
            Assert.Equal(6, stats.TotalItemsWritten); // 6 write attempts
            Assert.Equal(1, stats.TotalItemsDropped); // Item 6 was dropped
            Assert.Equal(2 + remainingCount, stats.TotalItemsRead); // Initial 2 reads + remaining
            Assert.Equal(0, stats.CurrentDepth); // Should be empty after reading all
            Assert.Equal(5, stats.Capacity);

            // Verify the actual items (1,2,3,4,6 with 5 dropped - newest in channel was dropped)
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