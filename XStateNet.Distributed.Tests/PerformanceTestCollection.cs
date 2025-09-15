using Xunit;

namespace XStateNet.Distributed.Tests
{
    /// <summary>
    /// Test collection for performance-sensitive tests that should not run in parallel
    /// </summary>
    [CollectionDefinition("Performance", DisableParallelization = true)]
    public class PerformanceTestCollection
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}