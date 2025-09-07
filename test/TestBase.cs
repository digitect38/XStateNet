using System;
using XStateNet;

namespace XStateNet.Tests
{
    /// <summary>
    /// Base class for all XStateNet tests to ensure proper test isolation
    /// </summary>
    public abstract class TestBase : IDisposable
    {
        // Generate unique test ID to prevent cross-test contamination
        protected readonly string TestId = Guid.NewGuid().ToString("N").Substring(0, 8);
        
        protected TestBase()
        {
            // Each test gets a unique ID prefix to avoid conflicts
        }

        public void Dispose()
        {
            // Cleanup is handled by individual state machine disposal
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// Create a unique machine ID for this test
        /// </summary>
        protected string UniqueMachineId(string baseName)
        {
            return $"{baseName}_{TestId}";
        }
    }
}