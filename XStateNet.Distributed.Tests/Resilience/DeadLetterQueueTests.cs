using MessagePack;
using Moq;
using System.Collections.Concurrent;
using XStateNet.Distributed.Resilience;
using Xunit;

namespace XStateNet.Distributed.Tests.Resilience
{
    public class DeadLetterQueueTests : IDisposable
    {
        private readonly Mock<IDeadLetterStorage> _mockStorage;
        private readonly DeadLetterQueue _dlq;
        private readonly DeadLetterQueueOptions _options;

        public DeadLetterQueueTests()
        {
            _mockStorage = new Mock<IDeadLetterStorage>();
            _options = new DeadLetterQueueOptions
            {
                MaxRetries = 3,
            };
            _dlq = new DeadLetterQueue(_options, _mockStorage.Object);
        }

        [Fact]
        public async Task EnqueueAsync_StoresMessage()
        {
            // Arrange
            var message = new TestMessage { Id = "test-1", Data = "Test data" };
            DeadLetterEntry? storedEntry = null;

            _mockStorage.Setup(s => s.SaveAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()))
                .Callback<DeadLetterEntry, CancellationToken>((entry, ct) => storedEntry = entry)
                .Returns(Task.CompletedTask);

            // Act
            await _dlq.EnqueueAsync(message, "TestSource", "Test reason");
            await _dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(1)); // Wait for async processing

            // Assert
            Assert.NotNull(storedEntry);
            Assert.Equal(typeof(TestMessage).FullName, storedEntry.MessageType);
            Assert.Equal("TestSource", storedEntry.Source);
            Assert.Equal("Test reason", storedEntry.Reason);
            Assert.Equal(_options.MaxRetries, storedEntry.MaxRetries);
            _mockStorage.Verify(s => s.SaveAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnqueueAsync_WithException_StoresExceptionDetails()
        {
            // Arrange
            var message = new TestMessage { Id = "test-1" };
            var exception = new InvalidOperationException("Test error");
            DeadLetterEntry? storedEntry = null;

            _mockStorage.Setup(s => s.SaveAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()))
                .Callback<DeadLetterEntry, CancellationToken>((entry, ct) => storedEntry = entry)
                .Returns(Task.CompletedTask);

            // Act
            await _dlq.EnqueueAsync(message, "TestSource", "Failed", exception);
            await _dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(1)); // Wait for async processing

            // Assert
            Assert.NotNull(storedEntry);
            Assert.Contains("InvalidOperationException", storedEntry!.Exception);
            Assert.Contains("Test error", storedEntry.Exception);
        }

        [Fact]
        public async Task EnqueueAsync_WithMetadata_StoresMetadata()
        {
            // Arrange
            var message = new TestMessage { Id = "test-1" };
            var metadata = new ConcurrentDictionary<string, string>
            {
                ["UserId"] = "user123",
                ["CorrelationId"] = "corr456"
            };
            DeadLetterEntry? storedEntry = null;

            _mockStorage.Setup(s => s.SaveAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()))
                .Callback<DeadLetterEntry, CancellationToken>((entry, ct) => storedEntry = entry)
                .Returns(Task.CompletedTask);

            // Act
            await _dlq.EnqueueAsync(message, "TestSource", "Test", null, metadata);
            await _dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(1)); // Wait for async processing

            // Assert
            Assert.NotNull(storedEntry);
            Assert.Equal("user123", storedEntry!.Metadata["UserId"]);
            Assert.Equal("corr456", storedEntry.Metadata["CorrelationId"]);
        }

        [Fact]
        public async Task ConcurrentEnqueue_ThreadSafe()
        {
            // Arrange
            var messageCount = 100;
            var enqueuedCount = 0;

            _mockStorage.Setup(s => s.SaveAsync(It.IsAny<DeadLetterEntry>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref enqueuedCount);
                    return Task.CompletedTask;
                });

            // Act
            var tasks = Enumerable.Range(0, messageCount).Select(i =>
                Task.Run(() => _dlq.EnqueueAsync(
                    new TestMessage { Id = $"msg-{i}" },
                    "ConcurrentSource",
                    "Test"))
            ).ToArray();

            await Task.WhenAll(tasks);
            await _dlq.WaitForPendingOperationsAsync(TimeSpan.FromSeconds(2)); // Wait for async processing

            // Assert
            Assert.Equal(messageCount, enqueuedCount);
        }

        public void Dispose()
        {
        }

        [MessagePackObject]
        public class TestMessage
        {
            [Key(0)]
            public string Id { get; set; } = string.Empty;
            [Key(1)]
            public string Data { get; set; } = string.Empty;
        }
    }
}