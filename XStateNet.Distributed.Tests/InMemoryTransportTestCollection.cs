using Xunit;

namespace XStateNet.Distributed.Tests
{
    /// <summary>
    /// Test collection definition for InMemoryTransport tests.
    /// This ensures tests that use InMemoryTransport don't run in parallel,
    /// preventing state leakage between tests since InMemoryTransport uses static state.
    /// </summary>
    [CollectionDefinition("InMemoryTransport")]
    public class InMemoryTransportTestCollection : ICollectionFixture<InMemoryTransportTestFixture>
    {
        // This class has no code, and is never created.
        // Its purpose is to be the place to apply [CollectionDefinition] and ICollectionFixture<>
    }

    /// <summary>
    /// Shared fixture for InMemoryTransport tests.
    /// Ensures proper cleanup of static registry between test runs.
    /// </summary>
    public class InMemoryTransportTestFixture : IDisposable
    {
        public InMemoryTransportTestFixture()
        {
            // Clear any leftover state from previous test runs
            XStateNet.Distributed.Transports.InMemoryTransport.ClearRegistry();
        }

        public void Dispose()
        {
            // Final cleanup
            XStateNet.Distributed.Transports.InMemoryTransport.ClearRegistry();
        }
    }
}