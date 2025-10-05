namespace XStateNet.Distributed.Resilience
{
    public interface IRetryPolicy
    {
        string Name { get; }
        Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
        Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default);
        Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
        Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default);
    }

    public interface IRetryMetrics
    {
        void RecordRetryAttempt(string policyName, int attemptNumber, string exceptionType);
        void RecordRetrySuccess(string policyName, int attemptCount, TimeSpan duration);
        void RecordRetryExhaustion(string policyName, TimeSpan totalDuration);
    }

    public class NullRetryMetrics : IRetryMetrics
    {
        public void RecordRetryAttempt(string policyName, int attemptNumber, string exceptionType) { }
        public void RecordRetrySuccess(string policyName, int attemptCount, TimeSpan duration) { }
        public void RecordRetryExhaustion(string policyName, TimeSpan totalDuration) { }
    }

    public class RetryOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.Exponential;
        public JitterStrategy JitterStrategy { get; set; } = JitterStrategy.Full;
        public HashSet<Type>? RetryableExceptions { get; set; }
        public double FailureRateThreshold { get; set; } = 0.5;
        public int MinimumThroughput { get; set; } = 10;
        public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(60);
    }

    public enum BackoffStrategy
    {
        Fixed,
        Linear,
        Exponential
    }

    public enum JitterStrategy
    {
        None,
        Full,
        EqualJitter
    }
}
