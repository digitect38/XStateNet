using Xunit;

namespace XStateNet.Distributed.Tests.TestInfrastructure
{
    /// <summary>
    /// Test collection for timing-sensitive tests that should run with highest priority
    /// and not in parallel with other tests to avoid timing issues.
    /// </summary>
    [CollectionDefinition("TimingSensitive")]
    public class TimingSensitiveTestCollection : ICollectionFixture<TimingSensitiveFixture>
    {
        // This class has no code, and is never created.
        // Its purpose is to be the place to apply [CollectionDefinition] and ICollectionFixture<>
    }

    /// <summary>
    /// Fixture for timing-sensitive tests to ensure proper test environment
    /// </summary>
    public class TimingSensitiveFixture
    {
        public TimingSensitiveFixture()
        {
            // Set thread pool to ensure sufficient threads for concurrent tests
            ThreadPool.SetMinThreads(50, 50);

            // Set process priority to high for more consistent timing
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
                System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
    }
}