using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;
using Xunit;

namespace XStateNet.Tests.TestInfrastructure
{
    /// <summary>
    /// Orders test cases by priority attribute, then alphabetically
    /// </summary>
    public class PriorityOrderer : ITestCaseOrderer
    {
        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
            where TTestCase : ITestCase
        {
            var sortedMethods = new SortedDictionary<int, List<TTestCase>>();

            foreach (var testCase in testCases)
            {
                var priority = GetPriority(testCase);

                if (!sortedMethods.ContainsKey(priority))
                {
                    sortedMethods.Add(priority, new List<TTestCase>());
                }

                sortedMethods[priority].Add(testCase);
            }

            // Return tests ordered by priority, then by name within each priority
            foreach (var kvp in sortedMethods)
            {
                var orderedTests = kvp.Value.OrderBy(tc => tc.TestMethod.Method.Name);
                foreach (var testCase in orderedTests)
                {
                    yield return testCase;
                }
            }
        }

        private static int GetPriority<TTestCase>(TTestCase testCase)
            where TTestCase : ITestCase
        {
            var priorityAttribute = testCase.TestMethod.Method
                .GetCustomAttributes(typeof(TestPriorityAttribute).AssemblyQualifiedName)
                .FirstOrDefault();

            if (priorityAttribute != null)
            {
                return priorityAttribute.GetNamedArgument<int>(nameof(TestPriorityAttribute.Priority));
            }

            // Default priority if not specified
            return TestPriority.Normal;
        }
    }

    /// <summary>
    /// Orders test collections by name, with timing-sensitive collections first
    /// </summary>
    public class PriorityCollectionOrderer : ITestCollectionOrderer
    {
        public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
        {
            // Priority collections that should run first
            var priorityCollectionNames = new[]
            {
                "ThreadSafeCircuitBreakerTests",
                "ResilientHsmsConnectionTests",
                "EventNotificationServiceTests",
                "ConcurrencyTests",
                "ThreadSafeEventHandlerTests"
            };

            var collections = testCollections.ToList();
            var priorityCollections = new List<ITestCollection>();
            var normalCollections = new List<ITestCollection>();

            foreach (var collection in collections)
            {
                if (priorityCollectionNames.Any(name =>
                    collection.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    priorityCollections.Add(collection);
                }
                else
                {
                    normalCollections.Add(collection);
                }
            }

            // Return priority collections first, then normal collections
            foreach (var collection in priorityCollections.OrderBy(c => c.DisplayName))
            {
                yield return collection;
            }

            foreach (var collection in normalCollections.OrderBy(c => c.DisplayName))
            {
                yield return collection;
            }
        }
    }
}