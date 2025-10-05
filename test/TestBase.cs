namespace XStateNet.Tests
{
    /// <summary>
    /// Base class for all XStateNet tests to ensure proper test isolation
    /// </summary>
    public abstract class TestBase : IDisposable
    {
        // Generate unique test ID to prevent cross-test contamination
        protected readonly string TestId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Thread-local storage to track test-specific machines
        private static readonly ThreadLocal<List<StateMachine>> _testMachines = new(() => new List<StateMachine>());

        protected TestBase()
        {
            // Each test gets a unique ID prefix to avoid conflicts
            // Clear any leftover machines from this thread
            CleanupThreadMachines();
        }

        public void Dispose()
        {
            // Clean up all machines created by this test
            CleanupThreadMachines();

            // Force garbage collection to clean up any lingering timers
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Create a unique machine ID for this test
        /// </summary>
        protected string UniqueMachineId(string baseName)
        {
            return $"{baseName}_{TestId}_{Thread.CurrentThread.ManagedThreadId}";
        }

        /// <summary>
        /// Register a state machine created by this test for cleanup
        /// </summary>
        protected void RegisterMachine(StateMachine machine)
        {
            if (machine != null && _testMachines.Value != null)
            {
                _testMachines.Value.Add(machine);
            }
        }

        private void CleanupThreadMachines()
        {
            if (_testMachines.Value != null)
            {
                foreach (var machine in _testMachines.Value)
                {
                    try
                    {
                        machine?.Stop();
                        machine?.Dispose();
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                _testMachines.Value.Clear();
            }
        }
    }
}