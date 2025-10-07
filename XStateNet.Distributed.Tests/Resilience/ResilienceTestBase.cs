using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace XStateNet.Distributed.Tests.Resilience
{
    /// <summary>
    /// Base class for resilience tests providing deterministic wait helpers
    /// </summary>
    public abstract class ResilienceTestBase : IDisposable
    {
        protected readonly ITestOutputHelper _output;

        protected ResilienceTestBase(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Wait deterministically for a condition to be met, polling with progress detection
        /// </summary>
        /// <param name="condition">Condition to wait for (e.g., () => count >= 50)</param>
        /// <param name="getProgress">Function to get current progress value for detecting stalls</param>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5)</param>
        /// <param name="pollIntervalMs">How often to check condition in milliseconds (default: 50)</param>
        /// <param name="noProgressTimeoutMs">How long to wait with no progress before giving up (default: 1000)</param>
        /// <returns>True if condition was met, false if timeout or no progress</returns>
        protected async Task<bool> WaitForConditionAsync(
            Func<bool> condition,
            Func<int> getProgress,
            double timeoutSeconds = 5.0,
            int pollIntervalMs = 50,
            int noProgressTimeoutMs = 1000)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var lastProgress = getProgress();
            var noProgressCount = 0;
            var noProgressThreshold = noProgressTimeoutMs / pollIntervalMs;

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;

                await Task.Delay(pollIntervalMs);

                var currentProgress = getProgress();
                if (currentProgress == lastProgress)
                {
                    noProgressCount++;
                    if (noProgressCount >= noProgressThreshold)
                    {
                        // No progress detected - queue likely drained
                        return condition();
                    }
                }
                else
                {
                    noProgressCount = 0;
                    lastProgress = currentProgress;
                }
            }

            return condition();
        }

        /// <summary>
        /// Wait deterministically for a counter to reach a target value
        /// </summary>
        /// <param name="counter">Reference to the counter variable</param>
        /// <param name="targetValue">Target value to wait for</param>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5)</param>
        /// <returns>True if target was reached, false if timeout</returns>
        protected async Task<bool> WaitForCountAsync(
            Func<int> getCount,
            int targetValue,
            double timeoutSeconds = 5.0)
        {
            return await WaitForConditionAsync(
                condition: () => getCount() >= targetValue,
                getProgress: getCount,
                timeoutSeconds: timeoutSeconds);
        }

        /// <summary>
        /// Wait deterministically until no progress is detected (queue drained)
        /// </summary>
        /// <param name="getProgress">Function to get current progress value</param>
        /// <param name="noProgressTimeoutMs">How long to wait with no progress (default: 1000)</param>
        /// <param name="maxWaitSeconds">Maximum time to wait overall (default: 5)</param>
        protected async Task WaitUntilQuiescentAsync(
            Func<int> getProgress,
            int noProgressTimeoutMs = 1000,
            double maxWaitSeconds = 5.0)
        {
            await WaitForConditionAsync(
                condition: () => false, // Never satisfied, relies on no-progress detection
                getProgress: getProgress,
                timeoutSeconds: maxWaitSeconds,
                noProgressTimeoutMs: noProgressTimeoutMs);
        }

        /// <summary>
        /// Wait for a specific amount of time with deterministic progress detection
        /// Use this instead of Task.Delay when you want to ensure operations complete
        /// </summary>
        /// <param name="getProgress">Function to track progress during the wait</param>
        /// <param name="minimumWaitMs">Minimum time to wait in milliseconds</param>
        /// <param name="additionalQuiescentMs">Additional time to wait after no progress (default: 500)</param>
        protected async Task WaitWithProgressAsync(
            Func<int> getProgress,
            int minimumWaitMs,
            int additionalQuiescentMs = 500)
        {
            await Task.Delay(minimumWaitMs);

            // Then wait until quiescent
            await WaitUntilQuiescentAsync(
                getProgress: getProgress,
                noProgressTimeoutMs: additionalQuiescentMs,
                maxWaitSeconds: 5.0);
        }

        public virtual void Dispose()
        {
            // Override in derived classes if needed
        }
    }
}
