using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using XStateNet.Distributed.Channels;
using XStateNet.Distributed.Metrics;
using XStateNet.Distributed.Monitoring;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Security;
using XStateNet.Distributed.StateMachines;
using XStateNet.Distributed.Telemetry;

namespace XStateNet.Distributed.Extensions
{
    public static class ResilienceServiceExtensions
    {
        public static IServiceCollection AddXStateNetResilience(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Circuit Breaker
            services.AddSingleton<ICircuitBreaker>(sp =>
            {
                var options = configuration.GetSection("Resilience:CircuitBreaker")
                    .Get<CircuitBreakerOptions>() ?? new CircuitBreakerOptions();
                var logger = sp.GetService<ILogger<CircuitBreaker>>();
                return new CircuitBreaker("default", options, null);
            });

            // Retry Policy
            services.AddSingleton<IRetryPolicy>(sp =>
            {
                var options = configuration.GetSection("Resilience:RetryPolicy")
                    .Get<RetryOptions>() ?? new RetryOptions();
                return new RetryPolicy("default", options, null);
            });

            // Dead Letter Queue with Redis storage
            services.AddSingleton<IDeadLetterStorage>(sp =>
            {
                var redisConfig = configuration.GetSection("Redis").Get<RedisConfiguration>()
                    ?? new RedisConfiguration();

                var redisConnection = ConnectionMultiplexer.Connect(redisConfig.ConnectionString);

                var options = configuration.GetSection("Resilience:DeadLetterQueue:Storage")
                    .Get<RedisDeadLetterStorageOptions>() ?? new RedisDeadLetterStorageOptions();

                var logger = sp.GetService<ILogger<RedisDeadLetterStorage>>();
                return new RedisDeadLetterStorage(redisConnection, options, logger);
            });

            services.AddSingleton<IDeadLetterQueue>(sp =>
            {
                var storage = sp.GetRequiredService<IDeadLetterStorage>();
                var options = configuration.GetSection("Resilience:DeadLetterQueue")
                    .Get<DeadLetterQueueOptions>() ?? new DeadLetterQueueOptions();
                var logger = sp.GetService<ILogger<DeadLetterQueue>>();
                return new DeadLetterQueue(options, storage, null, logger);
            });

            // Timeout Protection
            services.AddSingleton<ITimeoutProtection>(sp =>
            {
                var options = configuration.GetSection("Resilience:TimeoutProtection")
                    .Get<TimeoutOptions>() ?? new TimeoutOptions();
                var logger = sp.GetService<ILogger<TimeoutProtection>>();
                return new TimeoutProtection(options, null, logger);
            });

            // Distributed Tracing
            services.AddSingleton<IDistributedTracing>(sp =>
            {
                var options = configuration.GetSection("Monitoring:Tracing")
                    .Get<DistributedTracingOptions>() ?? new DistributedTracingOptions();
                var logger = sp.GetService<ILogger<DistributedTracing>>();
                return new DistributedTracing(options);
            });

            // Prometheus Metrics
            services.AddSingleton<IMetricsCollector>(sp =>
            {
                var options = configuration.GetSection("Monitoring:Metrics")
                    .Get<PrometheusMetricsOptions>() ?? new PrometheusMetricsOptions();
                return new PrometheusMetricsCollector(options);
            });

            // Security Layer
            services.AddSingleton<ISecurityLayer>(sp =>
            {
                var options = configuration.GetSection("Security")
                    .Get<SecurityOptions>() ?? new SecurityOptions();
                var logger = sp.GetService<ILogger<SecurityLayer>>();
                return new SecurityLayer(options);
            });

            // Note: IntegratedMonitoringSystem requires IMetricsCollector which is different from IPrometheusMetrics
            // This would need to be properly implemented based on actual requirements

            return services;
        }

        public static IServiceCollection AddXStateNetChannels(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Register factory for creating bounded channels
            services.AddSingleton<IBoundedChannelManagerFactory>(sp =>
            {
                var logger = sp.GetService<ILogger<BoundedChannelManager<object>>>();
                return new BoundedChannelManagerFactory(configuration, logger);
            });

            // Register factory for creating priority channels
            services.AddSingleton<IPriorityChannelManagerFactory>(sp =>
            {
                var logger = sp.GetService<ILogger<PriorityChannelManager<object>>>();
                return new PriorityChannelManagerFactory(configuration, logger);
            });

            return services;
        }

        public static IServiceCollection AddTimeoutProtectedStateMachine(
            this IServiceCollection services,
            Func<IServiceProvider, IStateMachine> stateMachineFactory,
            string stateMachineName = "Default",
            bool registerWithOrchestrator = false)
        {
            services.AddSingleton<TimeoutProtectedStateMachine>(sp =>
            {
                var stateMachine = stateMachineFactory(sp);
                var timeoutProtection = sp.GetRequiredService<ITimeoutProtection>();
                var dlq = sp.GetService<IDeadLetterQueue>();
                var options = sp.GetRequiredService<IConfiguration>()
                    .GetSection($"StateMachines:{stateMachineName}:TimeoutProtection")
                    .Get<TimeoutProtectedStateMachineOptions>() ?? new TimeoutProtectedStateMachineOptions();
                var logger = sp.GetService<ILogger<TimeoutProtectedStateMachine>>();
                var orchestrator = registerWithOrchestrator ? sp.GetService<XStateNet.Orchestration.EventBusOrchestrator>() : null;

                return new TimeoutProtectedStateMachine(
                    stateMachine, timeoutProtection, dlq, options, logger, orchestrator);
            });

            return services;
        }

        /// <summary>
        /// Add a timeout-protected state machine that participates in orchestrated communication
        /// </summary>
        public static IServiceCollection AddOrchestratedTimeoutProtectedStateMachine(
            this IServiceCollection services,
            Func<IServiceProvider, IStateMachine> stateMachineFactory,
            string stateMachineName = "Default",
            int? channelGroupId = null)
        {
            services.AddSingleton<TimeoutProtectedStateMachine>(sp =>
            {
                var stateMachine = stateMachineFactory(sp);
                var timeoutProtection = sp.GetRequiredService<ITimeoutProtection>();
                var dlq = sp.GetService<IDeadLetterQueue>();
                var options = sp.GetRequiredService<IConfiguration>()
                    .GetSection($"StateMachines:{stateMachineName}:TimeoutProtection")
                    .Get<TimeoutProtectedStateMachineOptions>() ?? new TimeoutProtectedStateMachineOptions();
                var logger = sp.GetService<ILogger<TimeoutProtectedStateMachine>>();
                var orchestrator = sp.GetRequiredService<XStateNet.Orchestration.EventBusOrchestrator>();

                var protectedMachine = new TimeoutProtectedStateMachine(
                    stateMachine, timeoutProtection, dlq, options, logger);

                // Register with orchestrator explicitly with optional channel group
                protectedMachine.RegisterWithOrchestrator(orchestrator, channelGroupId);

                return protectedMachine;
            });

            return services;
        }
    }

    // Factory interfaces
    public interface IBoundedChannelManagerFactory
    {
        BoundedChannelManager<T> Create<T>(CustomBoundedChannelOptions options);
    }

    public interface IPriorityChannelManagerFactory
    {
        PriorityChannelManager<T> Create<T>(PriorityChannelOptions options);
    }

    // Factory implementations
    public class BoundedChannelManagerFactory : IBoundedChannelManagerFactory
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger? _logger;

        public BoundedChannelManagerFactory(IConfiguration configuration, ILogger? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public BoundedChannelManager<T> Create<T>(CustomBoundedChannelOptions options)
        {
            var typedLogger = _logger as ILogger<BoundedChannelManager<T>>;
            return new BoundedChannelManager<T>("default", options, null, typedLogger);
        }
    }

    public class PriorityChannelManagerFactory : IPriorityChannelManagerFactory
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger? _logger;

        public PriorityChannelManagerFactory(IConfiguration configuration, ILogger? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public PriorityChannelManager<T> Create<T>(PriorityChannelOptions options)
        {
            var typedLogger = _logger as ILogger<PriorityChannelManager<T>>;
            return new PriorityChannelManager<T>("default", options, typedLogger);
        }
    }

    // Configuration classes
    public class RedisConfiguration
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public int ConnectTimeout { get; set; } = 5000;
        public int SyncTimeout { get; set; } = 5000;
        public bool AllowAdmin { get; set; } = false;
        public bool AbortOnConnectFail { get; set; } = false;
    }

    public class ResilienceConfiguration
    {
        public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
        public RetryOptions RetryPolicy { get; set; } = new();
        public DeadLetterQueueOptions DeadLetterQueue { get; set; } = new();
        public TimeoutProtectionOptions TimeoutProtection { get; set; } = new();
    }

    public class MonitoringConfiguration
    {
        public DistributedTracingOptions Tracing { get; set; } = new();
        public MetricsOptions Metrics { get; set; } = new();
        public MonitoringOptions Monitoring { get; set; } = new();
    }

    // Options classes for configuration
    // Note: TracingOptions removed - use DistributedTracingOptions from Telemetry namespace instead

    public class MetricsOptions
    {
        public bool EnableMetrics { get; set; } = true;
        public int MetricsPort { get; set; } = 9090;
        public string MetricsPath { get; set; } = "/metrics";
    }

    public class TimeoutProtectionOptions
    {
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public bool EnableAdaptiveTimeout { get; set; } = true;
        public double AdaptiveTimeoutMultiplier { get; set; } = 1.5;
        public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan MinTimeout { get; set; } = TimeSpan.FromSeconds(1);
    }
}