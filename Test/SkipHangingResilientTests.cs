using System;
using Xunit;

namespace XStateNet.Tests
{
    /// <summary>
    /// Wrapper to skip the hanging ResilientHsmsConnectionTests
    /// Use DeterministicResilientHsmsConnectionTests instead
    /// </summary>
    public static class SkipHangingResilientTests
    {
        public const string SkipReason =
            "These tests attempt real network connections and hang. " +
            "Use DeterministicResilientHsmsConnectionTests instead which provides " +
            "better coverage without network dependencies or hanging.";

        /// <summary>
        /// Check if we should skip the network-dependent tests
        /// </summary>
        public static bool ShouldSkipNetworkTests()
        {
            // Always skip these tests in favor of deterministic ones
            // Set environment variable FORCE_NETWORK_TESTS=true to run them anyway
            var forceRun = Environment.GetEnvironmentVariable("FORCE_NETWORK_TESTS");
            return forceRun?.ToLower() != "true";
        }
    }

    /// <summary>
    /// Fact attribute that skips tests when network tests should be skipped
    /// </summary>
    public class SkippableNetworkFactAttribute : FactAttribute
    {
        public SkippableNetworkFactAttribute()
        {
            if (SkipHangingResilientTests.ShouldSkipNetworkTests())
            {
                Skip = SkipHangingResilientTests.SkipReason;
            }
        }
    }
}