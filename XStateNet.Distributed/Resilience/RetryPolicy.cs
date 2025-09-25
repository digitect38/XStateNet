using System;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Distributed.Resilience
{
    public class RetryPolicy : IRetryPolicy
    {
        private readonly string _name;
        private readonly RetryOptions _options;
        private readonly IRetryMetrics? _metrics;
        private int _isExecuting = 0; // 0 = false, 1 = true

        public string Name => _name;

        public RetryPolicy(string name, RetryOptions options, IRetryMetrics? metrics = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _metrics = metrics;
        }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
            {
                throw new InvalidOperationException("Retry policy is already executing");
            }

            try
            {
                var attemptCount = 0;
                var delay = _options.InitialDelay;
                var exceptions = new List<Exception>();

                while (attemptCount <= _options.MaxRetries)
                {
                    try
                    {
                        attemptCount++;
                        var result = await operation(cancellationToken).ConfigureAwait(false);

                        if (attemptCount > 1)
                        {
                            _metrics?.RecordRetrySuccess(_name, attemptCount, TimeSpan.Zero);
                        }

                        return result;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);

                        // Check if the exception is retryable
                        if (_options.RetryableExceptions != null && !_options.RetryableExceptions.Contains(ex.GetType()))
                        {
                            // Non-retryable exception, throw immediately
                            throw;
                        }

                        if (attemptCount > _options.MaxRetries)
                        {
                            break;
                        }

                        _metrics?.RecordRetryAttempt(_name, attemptCount, ex.GetType().Name);

                        // Apply backoff strategy
                        var actualDelay = CalculateDelay(attemptCount, delay);
                        await Task.Delay(actualDelay, cancellationToken).ConfigureAwait(false);

                        // Update delay for next iteration
                        delay = GetNextDelay(delay);
                    }
                }

                _metrics?.RecordRetryExhaustion(_name, TimeSpan.Zero);

                // Throw AggregateException with all collected exceptions
                if (exceptions.Count > 0)
                {
                    throw new AggregateException($"Retry policy '{_name}' exhausted after {attemptCount} attempts", exceptions);
                }

                throw new InvalidOperationException("Retry failed");
            }
            finally
            {
                Interlocked.Exchange(ref _isExecuting, 0);
            }
        }

        public Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(_ => Task.FromResult(operation()), cancellationToken);
        }

        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(async _ =>
            {
                await operation().ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        private TimeSpan CalculateDelay(int attemptNumber, TimeSpan baseDelay)
        {
            var calculatedDelay = baseDelay;

            // Apply jitter
            if (_options.JitterStrategy != JitterStrategy.None)
            {
                var random = Random.Shared;
                if (_options.JitterStrategy == JitterStrategy.Full)
                {
                    calculatedDelay = TimeSpan.FromMilliseconds(random.Next(0, (int)calculatedDelay.TotalMilliseconds));
                }
                else if (_options.JitterStrategy == JitterStrategy.EqualJitter)
                {
                    var half = calculatedDelay.TotalMilliseconds / 2;
                    calculatedDelay = TimeSpan.FromMilliseconds(half + random.Next(0, (int)half));
                }
            }

            // Ensure we don't exceed max delay
            if (calculatedDelay > _options.MaxDelay)
            {
                calculatedDelay = _options.MaxDelay;
            }

            return calculatedDelay;
        }

        private TimeSpan GetNextDelay(TimeSpan currentDelay)
        {
            switch (_options.BackoffStrategy)
            {
                case BackoffStrategy.Fixed:
                    return currentDelay;

                case BackoffStrategy.Linear:
                    return TimeSpan.FromMilliseconds(currentDelay.TotalMilliseconds + _options.InitialDelay.TotalMilliseconds);

                case BackoffStrategy.Exponential:
                    var newDelay = TimeSpan.FromMilliseconds(currentDelay.TotalMilliseconds * _options.BackoffMultiplier);
                    return newDelay > _options.MaxDelay ? _options.MaxDelay : newDelay;

                default:
                    return currentDelay;
            }
        }
    }
}