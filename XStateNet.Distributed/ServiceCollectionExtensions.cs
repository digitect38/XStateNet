using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XStateNet.Distributed.EventBus;
using XStateNet.Distributed.Orchestration;
using XStateNet.Distributed.Registry;

namespace XStateNet.Distributed
{
    /// <summary>
    /// Extension methods for configuring distributed state machine services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds distributed state machine services with Redis state store
        /// </summary>
        public static IServiceCollection AddDistributedStateMachine(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Add Redis distributed cache
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = configuration.GetConnectionString("Redis") ?? "localhost:6379";
                options.InstanceName = "XStateNet:";
            });

            // Register distributed state store
            services.AddSingleton<IDistributedStateStore, RedisStateStore>();

            // Register orchestrator as hosted service
            services.AddSingleton<DistributedStateMachineOrchestratorV2>();
            services.AddSingleton<IStateMachineOrchestrator>(provider =>
                provider.GetRequiredService<DistributedStateMachineOrchestratorV2>());
            services.AddHostedService(provider =>
                provider.GetRequiredService<DistributedStateMachineOrchestratorV2>());

            // Register event bus
            services.AddSingleton<IStateMachineEventBus, InMemoryEventBus>();

            // Register registry
            services.AddSingleton<IStateMachineRegistry, RedisStateMachineRegistry>();

            return services;
        }

        /// <summary>
        /// Adds distributed state machine services with custom state store
        /// </summary>
        public static IServiceCollection AddDistributedStateMachine<TStateStore>(
            this IServiceCollection services,
            IConfiguration configuration)
            where TStateStore : class, IDistributedStateStore
        {
            // Add Redis distributed cache
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = configuration.GetConnectionString("Redis") ?? "localhost:6379";
                options.InstanceName = "XStateNet:";
            });

            // Register custom state store
            services.AddSingleton<IDistributedStateStore, TStateStore>();

            // Register orchestrator as hosted service
            services.AddSingleton<DistributedStateMachineOrchestratorV2>();
            services.AddSingleton<IStateMachineOrchestrator>(provider =>
                provider.GetRequiredService<DistributedStateMachineOrchestratorV2>());
            services.AddHostedService(provider =>
                provider.GetRequiredService<DistributedStateMachineOrchestratorV2>());

            // Register event bus
            services.AddSingleton<IStateMachineEventBus, InMemoryEventBus>();

            // Register registry
            services.AddSingleton<IStateMachineRegistry, RedisStateMachineRegistry>();

            return services;
        }

        /// <summary>
        /// Configures Redis connection options
        /// </summary>
        public static IServiceCollection ConfigureRedis(
            this IServiceCollection services,
            Action<RedisOptions> configureOptions)
        {
            services.Configure(configureOptions);
            return services;
        }
    }

    /// <summary>
    /// Redis configuration options
    /// </summary>
    public class RedisOptions
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public string InstanceName { get; set; } = "XStateNet:";
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public int ConnectRetry { get; set; } = 3;
        public bool AbortOnConnectFail { get; set; } = false;
        public bool AllowAdmin { get; set; } = false;
        public bool Ssl { get; set; } = false;
    }
}