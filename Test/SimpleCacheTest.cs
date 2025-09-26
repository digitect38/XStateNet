using System;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Semi.Transport;
using XStateNet.Semi.Secs;

namespace XStateNet.Tests
{
    public class SimpleCacheTest
    {
        private readonly ITestOutputHelper _output;

        public SimpleCacheTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void SimpleCache_BasicOperation()
        {
            // Arrange
            // Use System.Runtime.Caching directly to debug
            var memoryCache = System.Runtime.Caching.MemoryCache.Default;
            var key = "direct_test_key";
            var testValue = "test value";

            // Test direct MemoryCache
            memoryCache.Set(key, testValue, DateTimeOffset.UtcNow.AddMinutes(5));
            var directResult = memoryCache.Get(key);
            _output.WriteLine($"Direct MemoryCache test: {(directResult != null ? "Success" : "FAILED")}");

            // Now test SecsMessageCache
            var cache = new SecsMessageCache();
            var cacheKey = "test_key";
            var message = new SecsMessage(1, 1);

            _output.WriteLine($"Created message: Stream={message.Stream}, Function={message.Function}");

            // Act
            cache.CacheMessage(cacheKey, message);
            _output.WriteLine($"Cached message with key: {cacheKey}");

            // Add a small delay to ensure cache is ready
            System.Threading.Thread.Sleep(10);

            var result = cache.GetMessage(cacheKey);
            _output.WriteLine($"Retrieved message: {(result != null ? "Success" : "NULL")}");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, cache.TotalHits);
            Assert.Equal(0, cache.TotalMisses);

            _output.WriteLine($"Hits: {cache.TotalHits}, Misses: {cache.TotalMisses}");

            // Test miss
            var missResult = cache.GetMessage("non_existent_key");
            Assert.Null(missResult);
            Assert.Equal(1, cache.TotalHits);
            Assert.Equal(1, cache.TotalMisses);

            _output.WriteLine($"After miss - Hits: {cache.TotalHits}, Misses: {cache.TotalMisses}");

            cache.Dispose();
        }
    }
}