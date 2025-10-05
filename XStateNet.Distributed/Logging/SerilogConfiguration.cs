using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Collections.Concurrent;
using ILogger = Serilog.ILogger;

namespace XStateNet.Distributed.Logging
{
    /// <summary>
    /// Serilog configuration for structured logging
    /// </summary>
    public static class SerilogConfiguration
    {
        /// <summary>
        /// Configures Serilog for the host
        /// </summary>
        public static IHostBuilder UseSerilog(this IHostBuilder hostBuilder)
        {
            return hostBuilder.UseSerilog((context, services, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithThreadId()
                    .Enrich.WithProperty("Application", "XStateNet.Distributed")
                    .WriteTo.Console(
                        restrictedToMinimumLevel: LogEventLevel.Information,
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                    .WriteTo.File(
                        new CompactJsonFormatter(),
                        "logs/xstatenet-.json",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        fileSizeLimitBytes: 100 * 1024 * 1024) // 100MB
                    .WriteTo.Debug(restrictedToMinimumLevel: LogEventLevel.Debug)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .MinimumLevel.Override("XStateNet", LogEventLevel.Debug);
            });
        }

        /// <summary>
        /// Adds Serilog to service collection with custom configuration
        /// </summary>
        public static IServiceCollection AddStructuredLogging(
            this IServiceCollection services,
            Action<LoggerConfiguration>? configureLogger = null)
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "XStateNet.Distributed")
                .WriteTo.Console(
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/xstatenet-.json",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .MinimumLevel.Debug()
                .CreateLogger();

            if (configureLogger != null)
            {
                var configuration = new LoggerConfiguration();
                configureLogger(configuration);
                Log.Logger = configuration.CreateLogger();
            }

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });

            return services;
        }

        /// <summary>
        /// Creates a logger with contextual properties
        /// </summary>
        public static ILogger CreateContextualLogger<T>(
            string machineId,
            string? correlationId = null,
            string? operationType = null)
        {
            var logger = Log.ForContext<T>();

            if (!string.IsNullOrEmpty(machineId))
                logger = logger.ForContext("MachineId", machineId);

            if (!string.IsNullOrEmpty(correlationId))
                logger = logger.ForContext("CorrelationId", correlationId);

            if (!string.IsNullOrEmpty(operationType))
                logger = logger.ForContext("OperationType", operationType);

            return logger;
        }

        /// <summary>
        /// Extension method to add structured logging properties
        /// </summary>
        public static ILogger WithProperties(
            this ILogger logger,
            params (string Key, object? Value)[] properties)
        {
            foreach (var (key, value) in properties)
            {
                logger = logger.ForContext(key, value);
            }

            return logger;
        }
    }

    /// <summary>
    /// Common logging events for distributed state machines
    /// </summary>
    public static class LoggingEvents
    {
        // State Machine Events (1000-1999)
        public const int StateMachineStarted = 1000;
        public const int StateMachineStopped = 1001;
        public const int StateTransition = 1002;
        public const int EventReceived = 1003;
        public const int EventProcessed = 1004;
        public const int EventFailed = 1005;
        public const int StateEntered = 1006;
        public const int StateExited = 1007;

        // Distributed Events (2000-2999)
        public const int MessageSent = 2000;
        public const int MessageReceived = 2001;
        public const int MessageFailed = 2002;
        public const int NodeJoined = 2003;
        public const int NodeLeft = 2004;
        public const int LeaderElected = 2005;
        public const int ConsensusReached = 2006;
        public const int PartitionDetected = 2007;

        // Orchestration Events (3000-3999)
        public const int WorkflowStarted = 3000;
        public const int WorkflowCompleted = 3001;
        public const int WorkflowFailed = 3002;
        public const int SagaStarted = 3003;
        public const int SagaCompleted = 3004;
        public const int SagaCompensating = 3005;
        public const int SagaCompensated = 3006;
        public const int StepExecuted = 3007;
        public const int StepFailed = 3008;

        // Resilience Events (4000-4999)
        public const int RetryAttempt = 4000;
        public const int RetrySucceeded = 4001;
        public const int RetryExhausted = 4002;
        public const int CircuitOpened = 4003;
        public const int CircuitClosed = 4004;
        public const int CircuitHalfOpen = 4005;
        public const int TimeoutOccurred = 4006;
        public const int DeadLetterQueued = 4007;

        // Storage Events (5000-5999)
        public const int StateStored = 5000;
        public const int StateRetrieved = 5001;
        public const int StateDeleted = 5002;
        public const int StorageConnectionLost = 5003;
        public const int StorageConnectionRestored = 5004;
        public const int IndexUpdated = 5005;
        public const int CleanupPerformed = 5006;

        // Performance Events (6000-6999)
        public const int HighLatencyDetected = 6000;
        public const int HighThroughput = 6001;
        public const int ResourceExhaustion = 6002;
        public const int BackpressureApplied = 6003;
        public const int ThrottlingActivated = 6004;

        // Security Events (7000-7999)
        public const int AuthenticationFailed = 7000;
        public const int AuthorizationDenied = 7001;
        public const int InvalidToken = 7002;
        public const int SuspiciousActivity = 7003;
    }

    /// <summary>
    /// Structured logging extensions
    /// </summary>
    public static class StructuredLoggingExtensions
    {
        public static void LogStateTransition(
            this ILogger logger,
            string machineId,
            string fromState,
            string toState,
            string eventName,
            TimeSpan duration)
        {
            logger.ForContext("MachineId", machineId)
                  .ForContext("FromState", fromState)
                  .ForContext("ToState", toState)
                  .ForContext("EventName", eventName)
                  .ForContext("Duration", duration.TotalMilliseconds)
                  .Information("State transition: {FromState} -> {ToState} via {EventName}");
        }

        public static void LogWorkflowExecution(
            this ILogger logger,
            string workflowId,
            string status,
            TimeSpan executionTime,
            int stepsCompleted,
            int totalSteps)
        {
            logger.ForContext("WorkflowId", workflowId)
                  .ForContext("Status", status)
                  .ForContext("ExecutionTime", executionTime.TotalMilliseconds)
                  .ForContext("StepsCompleted", stepsCompleted)
                  .ForContext("TotalSteps", totalSteps)
                  .ForContext("CompletionPercentage", (double)stepsCompleted / totalSteps * 100)
                  .Information("Workflow {WorkflowId} {Status} - {StepsCompleted}/{TotalSteps} steps");
        }

        public static void LogSagaCompensation(
            this ILogger logger,
            string sagaId,
            string stepName,
            bool success,
            string? errorMessage = null)
        {
            var contextLogger = logger
                .ForContext("SagaId", sagaId)
                .ForContext("StepName", stepName)
                .ForContext("Success", success);

            if (success)
            {
                contextLogger.Information("Saga compensation succeeded for step {StepName}");
            }
            else
            {
                contextLogger
                    .ForContext("ErrorMessage", errorMessage)
                    .Error("Saga compensation failed for step {StepName}: {ErrorMessage}", stepName, errorMessage);
            }
        }

        public static void LogRetryAttempt(
            this ILogger logger,
            string operationName,
            int attemptNumber,
            int maxAttempts,
            TimeSpan nextDelay,
            Exception? exception = null)
        {
            logger.ForContext("OperationName", operationName)
                  .ForContext("AttemptNumber", attemptNumber)
                  .ForContext("MaxAttempts", maxAttempts)
                  .ForContext("NextDelay", nextDelay.TotalMilliseconds)
                  .ForContext("Exception", exception?.GetType().Name)
                  .Warning(exception, "Retry attempt {AttemptNumber}/{MaxAttempts} for {OperationName}");
        }

        public static void LogCircuitBreakerStateChange(
            this ILogger logger,
            string serviceName,
            string previousState,
            string newState,
            int failureCount,
            TimeSpan? breakDuration = null)
        {
            logger.ForContext("ServiceName", serviceName)
                  .ForContext("PreviousState", previousState)
                  .ForContext("NewState", newState)
                  .ForContext("FailureCount", failureCount)
                  .ForContext("BreakDuration", breakDuration?.TotalSeconds)
                  .Warning("Circuit breaker state changed: {PreviousState} -> {NewState}");
        }

        public static void LogPerformanceMetric(
            this ILogger logger,
            string metricName,
            double value,
            string unit,
            ConcurrentDictionary<string, object>? tags = null)
        {
            var contextLogger = logger
                .ForContext("MetricName", metricName)
                .ForContext("Value", value)
                .ForContext("Unit", unit);

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    contextLogger = contextLogger.ForContext(tag.Key, tag.Value);
                }
            }

            contextLogger.Debug("Performance metric: {MetricName} = {Value} {Unit}");
        }
    }
}