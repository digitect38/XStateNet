using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XStateNet;
using XStateNet.Distributed.Channels;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Extensions;
using XStateNet.Distributed.StateMachines;

namespace XStateNet.Distributed.Examples
{
    /// <summary>
    /// Comprehensive example demonstrating all resilience features
    /// </summary>
    public class ResilienceExample
    {
        // Example state machine states
        public enum OrderState
        {
            Idle,
            Processing,
            Validated,
            Shipped,
            Delivered,
            Failed,
            Cancelled
        }

        // Example state machine events
        public enum OrderEvent
        {
            Submit,
            Validate,
            Ship,
            Deliver,
            Cancel,
            Retry,
            Timeout
        }

        public class Order
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string CustomerId { get; set; } = "";
            public decimal Amount { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public OrderState State { get; set; } = OrderState.Idle;
        }

        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Add resilience components
                    services.AddXStateNetResilience(context.Configuration);
                    services.AddXStateNetChannels(context.Configuration);

                    // Add timeout-protected state machine
                    // Note: In actual implementation, configure the state machine here

                    // Add the example service
                    services.AddHostedService<ResilienceExampleService>();
                })
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.resilience.json", optional: true);
                })
                .Build();

            await host.RunAsync();
        }

        private static IStateMachine CreateOrderStateMachine(IServiceProvider services)
        {
            var logger = services.GetService<ILogger<ResilienceExample>>();

            // Create a simple order processing state machine
            // Note: This is a simplified example - actual implementation would use XStateNet's StateMachines
            // The StateMachines would be configured with states and transitions
            return null; // Placeholder - actual implementation needed

        }
    }

    public class ResilienceExampleService : BackgroundService
    {
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IDeadLetterQueue _dlq;
        private readonly ITimeoutProtection _timeoutProtection;
        private readonly IStateMachine _stateMachine;
        private readonly IBoundedChannelManagerFactory _channelFactory;
        private readonly ILogger<ResilienceExampleService> _logger;

        public ResilienceExampleService(
            ICircuitBreaker circuitBreaker,
            IRetryPolicy retryPolicy,
            IDeadLetterQueue dlq,
            ITimeoutProtection timeoutProtection,
            IStateMachine stateMachine,
            IBoundedChannelManagerFactory channelFactory,
            ILogger<ResilienceExampleService> logger)
        {
            _circuitBreaker = circuitBreaker;
            _retryPolicy = retryPolicy;
            _dlq = dlq;
            _timeoutProtection = timeoutProtection;
            _stateMachine = stateMachine;
            _channelFactory = channelFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting Resilience Example Service");

            // Create a bounded channel for order processing
            var orderChannel = _channelFactory.Create<ResilienceExample.Order>(new CustomBoundedChannelOptions
            {
                Capacity = 100,
                BackpressureStrategy = BackpressureStrategy.Wait,
                EnableMonitoring = true
            });

            // Start order processor
            var processorTask = ProcessOrdersAsync(orderChannel, stoppingToken);

            // Start order generator
            var generatorTask = GenerateOrdersAsync(orderChannel, stoppingToken);

            // Start DLQ processor
            var dlqTask = ProcessDeadLettersAsync(stoppingToken);

            await Task.WhenAll(processorTask, generatorTask, dlqTask);
        }

        private async Task GenerateOrdersAsync(
            BoundedChannelManager<ResilienceExample.Order> orderChannel,
            CancellationToken cancellationToken)
        {
            var random = new Random();
            var orderCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var order = new ResilienceExample.Order
                    {
                        CustomerId = $"CUST{random.Next(1000):D4}",
                        Amount = (decimal)(random.NextDouble() * 1000)
                    };

                    // Use bounded channel with backpressure
                    var written = await orderChannel.WriteAsync(order, cancellationToken);

                    if (written)
                    {
                        orderCount++;
                        _logger.LogDebug("Generated order {OrderId} (Total: {Count})", order.Id, orderCount);
                    }
                    else
                    {
                        _logger.LogWarning("Channel full, applying backpressure");
                    }

                    await Task.Delay(random.Next(100, 500), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating order");
                }
            }
        }

        private async Task ProcessOrdersAsync(
            BoundedChannelManager<ResilienceExample.Order> orderChannel,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read batch of orders
                    var orders = await orderChannel.ReadBatchAsync(10, TimeSpan.FromSeconds(5), cancellationToken);

                    foreach (var order in orders)
                    {
                        await ProcessSingleOrderAsync(order, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing orders");
                }
            }
        }

        private async Task ProcessSingleOrderAsync(ResilienceExample.Order order, CancellationToken cancellationToken)
        {
            try
            {
                // Process with circuit breaker protection
                await _circuitBreaker.ExecuteAsync(async () =>
                {
                    // Process with retry policy
                    await _retryPolicy.ExecuteAsync(async (ct) =>
                    {
                        // Process with timeout protection
                        await _timeoutProtection.ExecuteAsync(async (timeoutToken) =>
                        {
                            // Process order with state machine
                            // Note: Actual implementation would use proper state machine events
                            _logger.LogInformation("Processing order {OrderId} through state machine", order.Id);

                            // Simulate processing steps
                            await Task.Delay(100, timeoutToken);

                            // Simulate potential failure (10% chance)
                            if (Random.Shared.Next(10) == 0)
                            {
                                throw new InvalidOperationException($"Failed to process order {order.Id}");
                            }

                            await Task.Delay(200, timeoutToken);

                            _logger.LogInformation("Successfully processed order {OrderId}", order.Id);
                            return Task.CompletedTask;
                        }, TimeSpan.FromSeconds(5), "ProcessOrder", cancellationToken);

                        return Task.CompletedTask;
                    }, cancellationToken);

                    return Task.CompletedTask;
                }, cancellationToken);
            }
            catch (CircuitBreakerOpenException)
            {
                _logger.LogWarning("Circuit breaker is open, sending order {OrderId} to DLQ", order.Id);
                await _dlq.EnqueueAsync(order, "OrderProcessor", "Circuit breaker open");
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning("Order {OrderId} processing timed out, sending to DLQ", order.Id);
                await _dlq.EnqueueAsync(order, "OrderProcessor", "Timeout", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process order {OrderId}, sending to DLQ", order.Id);
                await _dlq.EnqueueAsync(order, "OrderProcessor", ex.Message, ex);
            }
        }

        private async Task ProcessDeadLettersAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Check for messages ready for retry
                    // Note: GetMessagesReadyForRetryAsync would need to be implemented
                    var readyForRetry = new List<DeadLetterEntry>();

                    foreach (var entry in readyForRetry)
                    {
                        _logger.LogInformation("Retrying dead letter {MessageId} (Attempt {Attempt})",
                            entry.Id, entry.RetryCount + 1);

                        // Process this specific entry
                        // Note: In a real implementation, you would deserialize MessageData
                        // For now, just log the retry attempt
                        _logger.LogInformation("Would retry dead letter {MessageId} of type {Type}",
                            entry.Id, entry.MessageType);
                    }

                    // Log DLQ statistics
                    // Note: GetStatisticsAsync would need to be implemented
                    var stats = new DeadLetterStatistics();
                    if (stats.TotalMessages > 0)
                    {
                        _logger.LogInformation("DLQ Statistics - Total: {Total}, Pending: {Pending}, Failed: {Failed}",
                            stats.TotalMessages, stats.PendingRetry, stats.PermanentlyFailed);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing dead letters");
                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                }
            }
        }
    }
}