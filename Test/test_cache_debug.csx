#r "nuget: System.Runtime.Caching, 9.0.9"

using System;
using System.Collections.Specialized;
using System.Runtime.Caching;

// Test 1: Default MemoryCache
Console.WriteLine("Test 1: Default MemoryCache");
var defaultCache = MemoryCache.Default;
defaultCache.Set("test1", "value1", DateTimeOffset.UtcNow.AddMinutes(5));
var result1 = defaultCache.Get("test1");
Console.WriteLine($"Result: {result1}");

// Test 2: Named MemoryCache with simple configuration
Console.WriteLine("\nTest 2: Named MemoryCache");
var namedCache = new MemoryCache($"TestCache_{Guid.NewGuid():N}");
namedCache.Set("test2", "value2", DateTimeOffset.UtcNow.AddMinutes(5));
var result2 = namedCache.Get("test2");
Console.WriteLine($"Result: {result2}");

// Test 3: Named MemoryCache with config
Console.WriteLine("\nTest 3: Named MemoryCache with Config");
var config = new NameValueCollection
{
    { "cacheMemoryLimitMegabytes", "100" },
    { "physicalMemoryLimitPercentage", "10" },
    { "pollingInterval", "00:02:00" }
};
var configCache = new MemoryCache($"TestCache_{Guid.NewGuid():N}", config);
configCache.Set("test3", "value3", DateTimeOffset.UtcNow.AddMinutes(5));
var result3 = configCache.Get("test3");
Console.WriteLine($"Result: {result3}");

// Test 4: CacheItemPolicy
Console.WriteLine("\nTest 4: CacheItemPolicy");
var policy = new CacheItemPolicy
{
    AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(5),
    Priority = CacheItemPriority.Default
};
namedCache.Set("test4", "value4", policy);
var result4 = namedCache.Get("test4");
Console.WriteLine($"Result: {result4}");

Console.WriteLine("\nAll tests completed.");