using System.Diagnostics;
using Xunit;

namespace XStateNet.Tests.TestInfrastructure
{
    public class ConcurrentTestFixture : IDisposable
    {
        private readonly int _originalMinWorkerThreads;
        private readonly int _originalMinIoThreads;
        private readonly ProcessPriorityClass _originalPriority;

        public ConcurrentTestFixture()
        {
            // Save original thread pool settings
            ThreadPool.GetMinThreads(out _originalMinWorkerThreads, out _originalMinIoThreads);

            // Configure thread pool for deterministic concurrent testing
            ThreadPool.SetMinThreads(100, 100);
            ThreadPool.SetMaxThreads(200, 200);

            // Save and set process priority
            var currentProcess = Process.GetCurrentProcess();
            _originalPriority = currentProcess.PriorityClass;

            try
            {
                currentProcess.PriorityClass = ProcessPriorityClass.High;
            }
            catch
            {
                // Ignore if we don't have permission to change priority
            }

            // Force garbage collection before tests to have a clean slate
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public void Dispose()
        {
            // Restore original thread pool settings
            ThreadPool.SetMinThreads(_originalMinWorkerThreads, _originalMinIoThreads);

            // Restore process priority
            try
            {
                Process.GetCurrentProcess().PriorityClass = _originalPriority;
            }
            catch
            {
                // Ignore if we don't have permission to change priority
            }
        }
    }

    [CollectionDefinition("ConcurrentTests")]
    public class ConcurrentTestCollection : ICollectionFixture<ConcurrentTestFixture>
    {
        // This class has no code, and is never created.
        // Its purpose is to be the place to apply [CollectionDefinition] and ICollectionFixture<>
    }
}