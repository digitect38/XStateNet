using System;

namespace XStateNet.Tests.TestInfrastructure
{
    /// <summary>
    /// Defines test execution priority. Lower values execute first.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TestPriorityAttribute : Attribute
    {
        public int Priority { get; }

        public TestPriorityAttribute(int priority)
        {
            Priority = priority;
        }
    }

    /// <summary>
    /// Priority levels for test execution
    /// </summary>
    public static class TestPriority
    {
        /// <summary>
        /// Timing-sensitive tests that must run first (e.g., circuit breaker, race conditions)
        /// </summary>
        public const int Critical = 0;

        /// <summary>
        /// High priority tests (e.g., concurrency tests)
        /// </summary>
        public const int High = 100;

        /// <summary>
        /// Normal priority tests
        /// </summary>
        public const int Normal = 200;

        /// <summary>
        /// Low priority tests (e.g., long-running integration tests)
        /// </summary>
        public const int Low = 300;

        /// <summary>
        /// Lowest priority tests (e.g., cleanup or diagnostic tests)
        /// </summary>
        public const int Lowest = 400;
    }
}