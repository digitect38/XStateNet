using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// High-performance retry policy with exponential backoff and jitter
    /// </summary>
    public sealed class RetryPolicy : IRetryPolicy
    {
        private readonly string _name;
        private readonly RetryOptions _options;
        private readonly IRetryMetrics _metrics;
        private readonly Random _jitterRandom;

        // Pre-calculated delays to avoid repeated calculations
        private readonly TimeSpan[] _baseDelays;

        public string Name => _name;

        public RetryPolicy(string name, RetryOptions options, IRetryMetrics? metrics = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics ?? new NullRetryMetrics();
            _jitterRandom = new Random();

            // Pre-calculate base delays for exponential backoff
            _baseDelays = PrecalculateDelays(options);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;
            var exceptions = new List<Exception>(_options.MaxRetries + 1);
            var stopwatch = Stopwatch.StartNew();

            while (attempt <= _options.MaxRetries)
            {
                try
                {
                    if (attempt > 0)
                    {
                        var delay = CalculateDelay(attempt);
                        _metrics.RecordRetryDelay(_name, delay);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    var result = await operation(cancellationToken).ConfigureAwait(false);

                    if (attempt > 0)
                    {
                        _metrics.RecordRetrySuccess(_name, attempt, stopwatch.Elapsed);
                    }

                    return result;
                }
                catch (Exception ex) when (ShouldRetry(ex, attempt))
                {
                    exceptions.Add(ex);
                    _metrics.RecordRetryAttempt(_name, attempt, ex.GetType().Name);

                    if (attempt == _options.MaxRetries)
                    {
                        _metrics.RecordRetryExhaustion(_name, stopwatch.Elapsed);
                        throw new RetryExhaustedException(_name, attempt + 1, exceptions, ex);
                    }

                    attempt++;
                }
                catch (Exception ex)
                {
                    // Non-retryable exception
                    _metrics.RecordNonRetryableError(_name, ex.GetType().Name);
                    throw;
                }
            }

            // This should never be reached, but satisfy compiler
            throw new InvalidOperationException("Retry loop ended unexpectedly");
        }

        public Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(_ => Task.FromResult(operation()), cancellationToken);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldRetry(Exception exception, int attempt)
        {
            // Check if we've exceeded max retries
            if (attempt >= _options.MaxRetries)
                return false;

            // Check if it's a retryable exception
            if (_options.RetryableExceptions != null && _options.RetryableExceptions.Count > 0)
            {
                var exceptionType = exception.GetType();
                foreach (var retryableType in _options.RetryableExceptions)
                {
                    if (retryableType.IsAssignableFrom(exceptionType))
                        return true;
                }
                return false;
            }

            // Check if it's a non-retryable exception
            if (_options.NonRetryableExceptions != null)
            {
                var exceptionType = exception.GetType();
                foreach (var nonRetryableType in _options.NonRetryableExceptions)
                {
                    if (nonRetryableType.IsAssignableFrom(exceptionType))
                        return false;
                }
            }

            // Default: retry all exceptions except cancellation
            return !(exception is OperationCanceledException);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimeSpan CalculateDelay(int attempt)
        {
            if (attempt <= 0)
                return TimeSpan.Zero;

            TimeSpan baseDelay;

            switch (_options.BackoffStrategy)
            {
                case BackoffStrategy.Fixed:
                    baseDelay = _options.InitialDelay;
                    break;

                case BackoffStrategy.Linear:
                    baseDelay = TimeSpan.FromMilliseconds(
                        _options.InitialDelay.TotalMilliseconds * attempt);
                    break;

                case BackoffStrategy.Exponential:
                    if (attempt - 1 < _baseDelays.Length)
                    {
                        baseDelay = _baseDelays[attempt - 1];
                    }
                    else
                    {
                        // Fallback for attempts beyond pre-calculated
                        baseDelay = TimeSpan.FromMilliseconds(
                            _options.InitialDelay.TotalMilliseconds * Math.Pow(_options.BackoffMultiplier, attempt - 1));
                    }
                    break;

                default:
                    baseDelay = _options.InitialDelay;
                    break;
            }

            // Apply maximum delay cap
            if (baseDelay > _options.MaxDelay)
                baseDelay = _options.MaxDelay;

            // Apply jitter
            if (_options.JitterStrategy != JitterStrategy.None)
            {
                baseDelay = ApplyJitter(baseDelay);
            }

            return baseDelay;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimeSpan ApplyJitter(TimeSpan delay)
        {
            double jitteredMilliseconds;

            switch (_options.JitterStrategy)
            {
                case JitterStrategy.Full:
                    // Random between 0 and delay
                    jitteredMilliseconds = _jitterRandom.NextDouble() * delay.TotalMilliseconds;
                    break;

                case JitterStrategy.EqualJitter:
                    // Half of delay plus random between 0 and half delay
                    var halfDelay = delay.TotalMilliseconds / 2;
                    jitteredMilliseconds = halfDelay + (_jitterRandom.NextDouble() * halfDelay);
                    break;

                case JitterStrategy.DecorrelatedJitter:
                    // Between base delay and 3x base delay
                    var min = _options.InitialDelay.TotalMilliseconds;
                    var max = delay.TotalMilliseconds * 3;
                    jitteredMilliseconds = min + (_jitterRandom.NextDouble() * (max - min));
                    break;

                default:
                    jitteredMilliseconds = delay.TotalMilliseconds;
                    break;
            }

            return TimeSpan.FromMilliseconds(jitteredMilliseconds);
        }

        private static TimeSpan[] PrecalculateDelays(RetryOptions options)
        {
            if (options.BackoffStrategy != BackoffStrategy.Exponential)
                return Array.Empty<TimeSpan>();

            var delays = new TimeSpan[Math.Min(options.MaxRetries, 10)];
            for (int i = 0; i < delays.Length; i++)
            {
                var delayMs = options.InitialDelay.TotalMilliseconds * Math.Pow(options.BackoffMultiplier, i);
                delays[i] = TimeSpan.FromMilliseconds(Math.Min(delayMs, options.MaxDelay.TotalMilliseconds));
            }

            return delays;
        }
    }

    public interface IRetryPolicy
    {
        string Name { get; }
        Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
        Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default);
    }

    public class RetryOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.Exponential;
        public double BackoffMultiplier { get; set; } = 2.0;
        public JitterStrategy JitterStrategy { get; set; } = JitterStrategy.Full;
        public HashSet<Type>? RetryableExceptions { get; set; }
        public HashSet<Type>? NonRetryableExceptions { get; set; }
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
        EqualJitter,
        DecorrelatedJitter
    }

    public class RetryExhaustedException : AggregateException
    {
        public string PolicyName { get; }
        public int AttemptCount { get; }

        public RetryExhaustedException(string policyName, int attemptCount, IEnumerable<Exception> innerExceptions, Exception lastException)
            : base($"Retry policy '{policyName}' exhausted after {attemptCount} attempts. Last error: {lastException.Message}", innerExceptions)
        {
            PolicyName = policyName;
            AttemptCount = attemptCount;
        }
    }

    public interface IRetryMetrics
    {
        void RecordRetryAttempt(string policyName, int attemptNumber, string exceptionType);
        void RecordRetrySuccess(string policyName, int totalAttempts, TimeSpan totalDuration);
        void RecordRetryExhaustion(string policyName, TimeSpan totalDuration);
        void RecordRetryDelay(string policyName, TimeSpan delay);
        void RecordNonRetryableError(string policyName, string exceptionType);
    }

    internal class NullRetryMetrics : IRetryMetrics
    {
        public void RecordRetryAttempt(string policyName, int attemptNumber, string exceptionType) { }
        public void RecordRetrySuccess(string policyName, int totalAttempts, TimeSpan totalDuration) { }
        public void RecordRetryExhaustion(string policyName, TimeSpan totalDuration) { }
        public void RecordRetryDelay(string policyName, TimeSpan delay) { }
        public void RecordNonRetryableError(string policyName, string exceptionType) { }
    }
}