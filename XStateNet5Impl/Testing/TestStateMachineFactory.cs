using System.Collections.Concurrent;

namespace XStateNet.Testing
{
    /// <summary>
    /// Factory for creating test-isolated state machines with automatic cleanup
    /// </summary>
    public class TestStateMachineFactory : IDisposable
    {
        private readonly ConcurrentDictionary<string, StateMachine> _machines = new();
        private readonly string _testClassName;
        private readonly string _testInstanceId;
        private bool _disposed;

        /// <summary>
        /// Create a new test factory for a specific test class
        /// </summary>
        public TestStateMachineFactory(string testClassName)
        {
            _testClassName = testClassName;
            _testInstanceId = Guid.NewGuid().ToString("N")[..8];
        }

        /// <summary>
        /// Create an isolated state machine for testing
        /// </summary>
        public async Task<StateMachine> CreateStateMachine(
            string jsonScript,
            string baseId,
            string? testMethodName = null,
            ActionMap? actionMap = null)
        {
            var instanceId = GenerateInstanceId(testMethodName);

            var machine = await new StateMachineBuilder()
                .WithJsonScript(jsonScript)
                .WithBaseId(baseId)
                .WithIsolation(StateMachineBuilder.IsolationMode.Test, $"{_testClassName}_{_testInstanceId}")
                .WithActionMap(actionMap ?? new ActionMap())
                .WithContext("testClass", _testClassName)
                .WithContext("testMethod", testMethodName ?? "Unknown")
                .WithContext("testInstance", _testInstanceId)
                .WithAutoStart(true)
                .Build(instanceId);

            _machines[instanceId] = machine;
            return machine;
        }

        /// <summary>
        /// Create multiple isolated state machines
        /// </summary>
        public async Task<List<StateMachine>> CreateMultiple(
            int count,
            string jsonScript,
            string baseId,
            string? testMethodName = null,
            ActionMap? actionMap = null)
        {
            var machines = new List<StateMachine>();

            for (int i = 0; i < count; i++)
            {
                var machine = await CreateStateMachine(
                    jsonScript,
                    baseId,
                    $"{testMethodName}_{i}",
                    actionMap);
                machines.Add(machine);
            }

            return machines;
        }

        /// <summary>
        /// Generate a unique instance ID for the state machine
        /// </summary>
        private string GenerateInstanceId(string? testMethodName)
        {
            var methodPart = string.IsNullOrEmpty(testMethodName)
                ? "Anonymous"
                : testMethodName.Replace(" ", "_");

            return $"{_testClassName}_{methodPart}_{_testInstanceId}_{Guid.NewGuid().ToString("N")[..8]}";
        }

        /// <summary>
        /// Get a state machine by its instance ID
        /// </summary>
        public StateMachine? GetMachine(string instanceId)
        {
            return _machines.TryGetValue(instanceId, out var machine) ? machine : null;
        }

        /// <summary>
        /// Reset all state machines (useful between test methods)
        /// </summary>
        public async void ResetAll()
        {
            foreach (var machine in _machines.Values)
            {
                try
                {
                    await machine.SendAsync("RESET");
                }
                catch
                {
                    // Ignore reset errors
                }
            }
        }

        /// <summary>
        /// Dispose of all state machines
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            foreach (var kvp in _machines)
            {
                try
                {
                    kvp.Value.SendAsync("DISPOSE").GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }

            _machines.Clear();
            _disposed = true;

            // Force garbage collection for test cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    /// <summary>
    /// Base class for test fixtures that use state machines
    /// </summary>
    public abstract class StateMachineTestBase : IDisposable
    {
        protected TestStateMachineFactory Factory { get; }

        protected StateMachineTestBase()
        {
            var testClassName = GetType().Name;
            Factory = new TestStateMachineFactory(testClassName);
        }

        /// <summary>
        /// Create an isolated state machine for the current test
        /// </summary>
        protected async Task<StateMachine> CreateStateMachine(
            string jsonScript,
            string baseId,
            ActionMap? actionMap = null,
            [System.Runtime.CompilerServices.CallerMemberName] string? testMethodName = null)
        {
            return await Factory.CreateStateMachine(jsonScript, baseId, testMethodName, actionMap);
        }

        /// <summary>
        /// Cleanup resources
        /// </summary>
        public virtual void Dispose()
        {
            Factory?.Dispose();
        }
    }

    /// <summary>
    /// Test utilities for state machine testing
    /// </summary>
    public static class StateMachineTestUtils
    {
        /// <summary>
        /// Wait for a state machine to reach a specific state
        /// </summary>
        public static bool WaitForState(
            StateMachine machine,
            string expectedState,
            TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < endTime)
            {
                var currentState = machine.GetSourceSubStateCollection(null)
                    .ToCsvString(machine, true);

                if (currentState.Contains(expectedState))
                    return true;

                Thread.Sleep(10);
            }

            return false;
        }

        /// <summary>
        /// Send event and wait for state transition
        /// </summary>
        public static async Task<bool> SendAndWaitForStateAsync(
            StateMachine machine,
            string eventName,
            string expectedState,
            TimeSpan timeout)
        {
            await machine.SendAsync(eventName);
            return WaitForState(machine, expectedState, timeout);
        }

        // Backward compatibility - synchronous version
        public static bool SendAndWaitForState(
            StateMachine machine,
            string eventName,
            string expectedState,
            TimeSpan timeout)
        {
            return SendAndWaitForStateAsync(machine, eventName, expectedState, timeout).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Create a unique test ID
        /// </summary>
        public static string CreateTestId(string prefix = "Test")
        {
            return $"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString("N")[..8]}";
        }

        /// <summary>
        /// Create isolated IDs for parallel tests
        /// </summary>
        public static string[] CreateParallelTestIds(int count, string prefix = "Parallel")
        {
            var baseId = CreateTestId(prefix);
            var ids = new string[count];

            for (int i = 0; i < count; i++)
            {
                ids[i] = $"{baseId}_{i}";
            }

            return ids;
        }
    }
}
