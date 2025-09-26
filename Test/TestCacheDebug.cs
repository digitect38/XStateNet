using System;
using System.Runtime.Caching;
using Xunit;
using Xunit.Abstractions;

namespace XStateNet.Tests
{
    public class TestCacheDebug
    {
        private readonly ITestOutputHelper _output;

        public TestCacheDebug(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void MemoryCache_Basic_Works()
        {
            // Test with unique cache
            var cacheId = $"TestCache_{Guid.NewGuid():N}";
            var cache = new MemoryCache(cacheId);

            var key = "test_key";
            var value = new TestObject { Id = 1, Name = "Test" };

            // Set with DateTimeOffset
            cache.Set(key, value, DateTimeOffset.UtcNow.AddMinutes(5));

            // Get immediately
            var retrieved = cache.Get(key);

            _output.WriteLine($"Cache Id: {cacheId}");
            _output.WriteLine($"Set value: {value}");
            _output.WriteLine($"Retrieved value: {retrieved}");
            _output.WriteLine($"Retrieved type: {retrieved?.GetType().FullName ?? "null"}");

            Assert.NotNull(retrieved);
            Assert.IsType<TestObject>(retrieved);

            var typed = (TestObject)retrieved;
            Assert.Equal(1, typed.Id);
            Assert.Equal("Test", typed.Name);

            cache.Dispose();
        }

        private class TestObject
        {
            public int Id { get; set; }
            public string Name { get; set; }

            public override string ToString() => $"TestObject(Id={Id}, Name={Name})";
        }
    }
}