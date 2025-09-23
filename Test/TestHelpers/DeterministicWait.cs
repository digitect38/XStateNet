using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XStateNet;

namespace XStateNet.Tests.TestHelpers
{
    /// <summary>
    /// Helper class for deterministic waiting in tests to replace Task.Delay
    /// </summary>
    public static class DeterministicWait
    {
        /// <summary>
        /// Waits for a specific condition to be true within a timeout
        /// </summary>
        public static async Task WaitForConditionAsync(
            Func<bool> condition,
            int timeoutMs = 5000,
            int pollIntervalMs = 10,
            string conditionDescription = null)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(pollIntervalMs);
            }

            if (!condition())
            {
                var description = conditionDescription ?? "Condition";
                throw new TimeoutException($"{description} not met within {timeoutMs}ms timeout");
            }
        }

        /// <summary>
        /// Waits for a state machine to reach a specific state
        /// </summary>
        public static async Task WaitForStateAsync(
            StateMachine stateMachine,
            string expectedState,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => stateMachine.GetSourceSubStateCollection(null)
                    .ToCsvString(stateMachine, true)
                    .Contains(expectedState),
                timeoutMs,
                conditionDescription: $"State '{expectedState}'");
        }

        /// <summary>
        /// Waits for a collection to contain a specific number of items
        /// </summary>
        public static async Task WaitForCountAsync<T>(
            Func<IEnumerable<T>> collectionProvider,
            int expectedCount,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => collectionProvider().Count() >= expectedCount,
                timeoutMs,
                conditionDescription: $"Collection count >= {expectedCount}");
        }

        /// <summary>
        /// Waits for a collection to contain a specific item
        /// </summary>
        public static async Task WaitForItemAsync<T>(
            Func<IEnumerable<T>> collectionProvider,
            Func<T, bool> predicate,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => collectionProvider().Any(predicate),
                timeoutMs,
                conditionDescription: "Expected item in collection");
        }

        /// <summary>
        /// Waits for a string collection to contain a specific value
        /// </summary>
        public static async Task WaitForLogEntryAsync(
            IEnumerable<string> log,
            string expectedEntry,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => log.Contains(expectedEntry),
                timeoutMs,
                conditionDescription: $"Log entry '{expectedEntry}'");
        }

        /// <summary>
        /// Waits for a string collection to match a predicate
        /// </summary>
        public static async Task WaitForLogEntryAsync(
            IEnumerable<string> log,
            Func<string, bool> predicate,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => log.Any(predicate),
                timeoutMs,
                conditionDescription: "Matching log entry");
        }

        /// <summary>
        /// Waits for a counter/value to reach a specific threshold
        /// </summary>
        public static async Task WaitForValueAsync(
            Func<int> valueProvider,
            int expectedValue,
            int timeoutMs = 5000,
            bool atLeast = true)
        {
            if (atLeast)
            {
                await WaitForConditionAsync(
                    () => valueProvider() >= expectedValue,
                    timeoutMs,
                    conditionDescription: $"Value >= {expectedValue}");
            }
            else
            {
                await WaitForConditionAsync(
                    () => valueProvider() == expectedValue,
                    timeoutMs,
                    conditionDescription: $"Value == {expectedValue}");
            }
        }

        /// <summary>
        /// Waits for multiple conditions to be true
        /// </summary>
        public static async Task WaitForAllConditionsAsync(
            params Func<bool>[] conditions)
        {
            await WaitForConditionAsync(
                () => conditions.All(c => c()),
                5000,
                conditionDescription: "All conditions");
        }

        /// <summary>
        /// Waits for any of the conditions to be true
        /// </summary>
        public static async Task WaitForAnyConditionAsync(
            params Func<bool>[] conditions)
        {
            await WaitForConditionAsync(
                () => conditions.Any(c => c()),
                5000,
                conditionDescription: "Any condition");
        }

        /// <summary>
        /// Waits for a service to complete by checking event logs
        /// </summary>
        public static async Task WaitForServiceCompletionAsync(
            IEnumerable<string> eventLog,
            string serviceName,
            int timeoutMs = 5000)
        {
            await WaitForConditionAsync(
                () => eventLog.Any(e => e.Contains($"service:{serviceName}:completed")),
                timeoutMs,
                conditionDescription: $"Service '{serviceName}' completion");
        }

        /// <summary>
        /// Waits for multiple services to complete
        /// </summary>
        public static async Task WaitForServicesCompletionAsync(
            IEnumerable<string> eventLog,
            params string[] serviceNames)
        {
            foreach (var serviceName in serviceNames)
            {
                await WaitForServiceCompletionAsync(eventLog, serviceName);
            }
        }
    }
}