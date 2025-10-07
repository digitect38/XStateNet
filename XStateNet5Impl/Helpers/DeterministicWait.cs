using System;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Helpers
{
    /// <summary>
    /// Production-grade deterministic wait utilities for async operations
    /// Provides progress-aware waiting instead of fixed Task.Delay calls
    /// </summary>
    public static class DeterministicWait
    {
        /// <summary>
        /// Wait deterministically for a condition to be met, polling with progress detection
        /// </summary>
        /// <param name="condition">Condition to wait for (e.g., () => count >= 50)</param>
        /// <param name="getProgress">Function to get current progress value for detecting stalls</param>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5)</param>
        /// <param name="pollIntervalMs">How often to check condition in milliseconds (default: 50)</param>
        /// <param name="noProgressTimeoutMs">How long to wait with no progress before giving up (default: 1000)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if condition was met, false if timeout or no progress</returns>
        /// <exception cref="OperationCanceledException">If cancellation is requested</exception>
        public static async Task<bool> WaitForConditionAsync(
            Func<bool> condition,
            Func<int> getProgress,
            double timeoutSeconds = 5.0,
            int pollIntervalMs = 50,
            int noProgressTimeoutMs = 1000,
            CancellationToken cancellationToken = default)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (getProgress == null) throw new ArgumentNullException(nameof(getProgress));
            if (timeoutSeconds <= 0) throw new ArgumentException("Timeout must be positive", nameof(timeoutSeconds));
            if (pollIntervalMs <= 0) throw new ArgumentException("Poll interval must be positive", nameof(pollIntervalMs));
            if (noProgressTimeoutMs <= 0) throw new ArgumentException("No-progress timeout must be positive", nameof(noProgressTimeoutMs));

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var lastProgress = getProgress();
            var noProgressCount = 0;
            var noProgressThreshold = noProgressTimeoutMs / pollIntervalMs;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (condition())
                    return true;

                await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);

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
        /// <param name="getCount">Function to get current count</param>
        /// <param name="targetValue">Target value to wait for</param>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if target was reached, false if timeout</returns>
        /// <exception cref="OperationCanceledException">If cancellation is requested</exception>
        public static async Task<bool> WaitForCountAsync(
            Func<int> getCount,
            int targetValue,
            double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            if (getCount == null) throw new ArgumentNullException(nameof(getCount));

            return await WaitForConditionAsync(
                condition: () => getCount() >= targetValue,
                getProgress: getCount,
                timeoutSeconds: timeoutSeconds,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait deterministically until no progress is detected (queue drained)
        /// Useful for waiting until async operations complete without knowing exact count
        /// </summary>
        /// <param name="getProgress">Function to get current progress value</param>
        /// <param name="noProgressTimeoutMs">How long to wait with no progress (default: 1000)</param>
        /// <param name="maxWaitSeconds">Maximum time to wait overall (default: 5)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Task that completes when quiescent or timeout</returns>
        /// <exception cref="OperationCanceledException">If cancellation is requested</exception>
        public static async Task WaitUntilQuiescentAsync(
            Func<int> getProgress,
            int noProgressTimeoutMs = 1000,
            double maxWaitSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            if (getProgress == null) throw new ArgumentNullException(nameof(getProgress));

            await WaitForConditionAsync(
                condition: () => false, // Never satisfied, relies on no-progress detection
                getProgress: getProgress,
                timeoutSeconds: maxWaitSeconds,
                noProgressTimeoutMs: noProgressTimeoutMs,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait for a specific amount of time with deterministic progress detection
        /// Use this instead of Task.Delay when you want to ensure operations complete
        /// </summary>
        /// <param name="getProgress">Function to track progress during the wait</param>
        /// <param name="minimumWaitMs">Minimum time to wait in milliseconds</param>
        /// <param name="additionalQuiescentMs">Additional time to wait after no progress (default: 500)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Task that completes after minimum wait + quiescence</returns>
        /// <exception cref="OperationCanceledException">If cancellation is requested</exception>
        public static async Task WaitWithProgressAsync(
            Func<int> getProgress,
            int minimumWaitMs,
            int additionalQuiescentMs = 500,
            CancellationToken cancellationToken = default)
        {
            if (getProgress == null) throw new ArgumentNullException(nameof(getProgress));
            if (minimumWaitMs < 0) throw new ArgumentException("Minimum wait must be non-negative", nameof(minimumWaitMs));

            await Task.Delay(minimumWaitMs, cancellationToken).ConfigureAwait(false);

            // Then wait until quiescent
            await WaitUntilQuiescentAsync(
                getProgress: getProgress,
                noProgressTimeoutMs: additionalQuiescentMs,
                maxWaitSeconds: 5.0,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait for multiple conditions with progress detection
        /// Returns when all conditions are met or timeout occurs
        /// </summary>
        /// <param name="conditions">Array of conditions to wait for</param>
        /// <param name="getProgress">Function to get current progress value</param>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if all conditions were met, false if timeout</returns>
        /// <exception cref="OperationCanceledException">If cancellation is requested</exception>
        public static async Task<bool> WaitForAllConditionsAsync(
            Func<bool>[] conditions,
            Func<int> getProgress,
            double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            if (conditions == null || conditions.Length == 0)
                throw new ArgumentException("Must provide at least one condition", nameof(conditions));
            if (getProgress == null) throw new ArgumentNullException(nameof(getProgress));

            return await WaitForConditionAsync(
                condition: () =>
                {
                    foreach (var cond in conditions)
                    {
                        if (!cond())
                            return false;
                    }
                    return true;
                },
                getProgress: getProgress,
                timeoutSeconds: timeoutSeconds,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait for any of multiple conditions with progress detection
        /// Returns when any condition is met or timeout occurs
        /// </summary>
        /// <param name="conditions">Array of conditions to wait for</param>
        /// <param name="getProgress">Function to get current progress value</param>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds (default: 5)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if any condition was met, false if timeout</returns>
        /// <exception cref="OperationCanceledException">If cancellation is requested</exception>
        public static async Task<bool> WaitForAnyConditionAsync(
            Func<bool>[] conditions,
            Func<int> getProgress,
            double timeoutSeconds = 5.0,
            CancellationToken cancellationToken = default)
        {
            if (conditions == null || conditions.Length == 0)
                throw new ArgumentException("Must provide at least one condition", nameof(conditions));
            if (getProgress == null) throw new ArgumentNullException(nameof(getProgress));

            return await WaitForConditionAsync(
                condition: () =>
                {
                    foreach (var cond in conditions)
                    {
                        if (cond())
                            return true;
                    }
                    return false;
                },
                getProgress: getProgress,
                timeoutSeconds: timeoutSeconds,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
