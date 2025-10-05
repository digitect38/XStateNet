using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using XStateNet.Distributed.EventBus;

namespace XStateNet.Distributed.PubSub
{
    /// <summary>
    /// Simple and efficient event bus implementation using System.Threading.Channels
    /// </summary>
    public sealed class ChannelBasedEventBus : IDisposable
    {
        private readonly ILogger<ChannelBasedEventBus>? _logger;
        private readonly Channel<(string Topic, StateMachineEvent Event)> _channel;
        private readonly ConcurrentDictionary<string, ConcurrentBag<Subscription>> _subscriptions;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;

        private long _totalEventsPublished;
        private long _totalEventsDelivered;
        private long _totalEventsDropped;
        private bool _disposed;

        public ChannelBasedEventBus(ILogger<ChannelBasedEventBus>? logger = null, int capacity = 10000)
        {
            _logger = logger;
            _subscriptions = new ConcurrentDictionary<string, ConcurrentBag<Subscription>>();
            _cancellationTokenSource = new CancellationTokenSource();

            // Create bounded channel with backpressure support
            _channel = Channel.CreateBounded<(string Topic, StateMachineEvent Event)>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            });

            // Start background processing task
            _processingTask = Task.Run(() => ProcessEventsAsync(_cancellationTokenSource.Token));

            _logger?.LogInformation("ChannelBasedEventBus initialized with capacity {Capacity}", capacity);
        }

        /// <summary>
        /// Publish an event to the specified topic
        /// </summary>
        public async Task PublishAsync(string topic, StateMachineEvent evt, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                await _channel.Writer.WriteAsync((topic, evt), cancellationToken);
                Interlocked.Increment(ref _totalEventsPublished);
            }
            catch (ChannelClosedException)
            {
                _logger?.LogWarning("Cannot publish event - channel is closed");
                Interlocked.Increment(ref _totalEventsDropped);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Event publishing cancelled");
                Interlocked.Increment(ref _totalEventsDropped);
            }
        }

        /// <summary>
        /// Try to publish an event without blocking
        /// </summary>
        public bool TryPublish(string topic, StateMachineEvent evt)
        {
            ThrowIfDisposed();

            if (_channel.Writer.TryWrite((topic, evt)))
            {
                Interlocked.Increment(ref _totalEventsPublished);
                return true;
            }

            Interlocked.Increment(ref _totalEventsDropped);
            _logger?.LogDebug("Event dropped - channel is full");
            return false;
        }

        /// <summary>
        /// Subscribe to events on a specific topic
        /// </summary>
        public IDisposable Subscribe(string topic, Action<StateMachineEvent> handler)
        {
            ThrowIfDisposed();

            var subscription = new Subscription(handler);
            var bag = _subscriptions.GetOrAdd(topic, _ => new ConcurrentBag<Subscription>());
            bag.Add(subscription);

            _logger?.LogDebug("Added subscription for topic {Topic}", topic);

            return new SubscriptionDisposable(() =>
            {
                subscription.IsActive = false;
                _logger?.LogDebug("Removed subscription for topic {Topic}", topic);
            });
        }

        /// <summary>
        /// Process events from the channel
        /// </summary>
        private async Task ProcessEventsAsync(CancellationToken cancellationToken)
        {
            _logger?.LogInformation("Event processing started");

            try
            {
                await foreach (var (topic, evt) in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    await DeliverEventAsync(topic, evt);
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Event processing cancelled");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Event processing failed");
            }
            finally
            {
                _logger?.LogInformation("Event processing stopped. Published: {Published}, Delivered: {Delivered}, Dropped: {Dropped}",
                    _totalEventsPublished, _totalEventsDelivered, _totalEventsDropped);
            }
        }

        /// <summary>
        /// Deliver event to all subscribers
        /// </summary>
        private async Task DeliverEventAsync(string topic, StateMachineEvent evt)
        {
            if (!_subscriptions.TryGetValue(topic, out var subscriptions))
            {
                _logger?.LogTrace("No subscribers for topic {Topic}", topic);
                return;
            }

            var deliveryTasks = new List<Task>();

            foreach (var subscription in subscriptions)
            {
                if (!subscription.IsActive)
                    continue;

                // Run handlers asynchronously to prevent blocking
                deliveryTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        subscription.Handler(evt);
                        Interlocked.Increment(ref _totalEventsDelivered);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Subscription handler failed for topic {Topic}", topic);
                    }
                }));
            }

            // Wait for all handlers to complete with timeout
            if (deliveryTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(deliveryTasks).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error waiting for event delivery tasks");
                }
            }
        }

        /// <summary>
        /// Get current statistics
        /// </summary>
        public (long Published, long Delivered, long Dropped) GetStatistics()
        {
            return (_totalEventsPublished, _totalEventsDelivered, _totalEventsDropped);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ChannelBasedEventBus));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _logger?.LogInformation("Disposing ChannelBasedEventBus");

            // Signal shutdown
            _cancellationTokenSource.Cancel();

            // Complete the channel
            _channel.Writer.TryComplete();

            // Wait for processing to complete
            try
            {
                _processingTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error waiting for processing task to complete");
            }

            _cancellationTokenSource.Dispose();

            _logger?.LogInformation("ChannelBasedEventBus disposed");
        }

        /// <summary>
        /// Internal subscription representation
        /// </summary>
        private class Subscription
        {
            public Action<StateMachineEvent> Handler { get; }
            public bool IsActive { get; set; } = true;

            public Subscription(Action<StateMachineEvent> handler)
            {
                Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }
        }

        /// <summary>
        /// Disposable handle for subscriptions
        /// </summary>
        private class SubscriptionDisposable : IDisposable
        {
            private Action? _disposeAction;

            public SubscriptionDisposable(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _disposeAction, null)?.Invoke();
            }
        }
    }

    /// <summary>
    /// Extension methods for dependency injection
    /// </summary>
    public static class ChannelBasedEventBusExtensions
    {
        public static IServiceCollection AddChannelBasedEventBus(
            this IServiceCollection services,
            int capacity = 10000)
        {
            services.AddSingleton<ChannelBasedEventBus>(provider =>
            {
                var logger = provider.GetService<ILogger<ChannelBasedEventBus>>();
                return new ChannelBasedEventBus(logger, capacity);
            });

            return services;
        }
    }
}