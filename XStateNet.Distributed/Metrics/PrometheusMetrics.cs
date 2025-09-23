using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Prometheus;

namespace XStateNet.Distributed.Metrics
{
    /// <summary>
    /// High-performance Prometheus metrics collection
    /// </summary>
    public sealed class PrometheusMetricsCollector : IMetricsCollector
    {
        // Counters - for monotonically increasing values
        private readonly Counter _eventProcessedTotal;
        private readonly Counter _stateTransitionTotal;
        private readonly Counter _errorTotal;
        private readonly Counter _retryTotal;
        private readonly Counter _circuitBreakerTripsTotal;

        // Gauges - for values that can go up and down
        private readonly Gauge _activeMachines;
        private readonly Gauge _activeConnections;
        private readonly Gauge _queueDepth;
        private readonly Gauge _memoryUsage;
        private readonly Gauge _cpuUsage;

        // Histograms - for measuring distributions
        private readonly Histogram _eventProcessingDuration;
        private readonly Histogram _stateTransitionDuration;
        private readonly Histogram _requestDuration;
        private readonly Histogram _messageSize;

        // Summaries - for calculating quantiles
        private readonly Summary _eventLatency;

        // Custom registry for isolation
        private readonly CollectorRegistry _registry;
        private readonly MetricServer? _metricServer;

        // Performance optimization: pre-allocated label values
        private readonly ConcurrentDictionary<string, string[]> _labelCache;
        private readonly ReaderWriterLockSlim _labelCacheLock;

        public PrometheusMetricsCollector(PrometheusMetricsOptions options)
        {
            _registry = options.UseDefaultRegistry ? Prometheus.Metrics.DefaultRegistry : new CollectorRegistry();
            _labelCache = new ConcurrentDictionary<string, string[]>(StringComparer.Ordinal);
            _labelCacheLock = new ReaderWriterLockSlim();

            var factory = options.UseDefaultRegistry ? Prometheus.Metrics.WithCustomRegistry(_registry) : Prometheus.Metrics.WithCustomRegistry(_registry);

            // Initialize Counters
            _eventProcessedTotal = factory.CreateCounter(
                "xstatenet_events_processed_total",
                "Total number of events processed",
                new[] { "machine_id", "event_type", "status" });

            _stateTransitionTotal = factory.CreateCounter(
                "xstatenet_state_transitions_total",
                "Total number of state transitions",
                new[] { "machine_id", "from_state", "to_state" });

            _errorTotal = factory.CreateCounter(
                "xstatenet_errors_total",
                "Total number of errors",
                new[] { "machine_id", "error_type", "component" });

            _retryTotal = factory.CreateCounter(
                "xstatenet_retries_total",
                "Total number of retry attempts",
                new[] { "policy_name", "attempt", "result" });

            _circuitBreakerTripsTotal = factory.CreateCounter(
                "xstatenet_circuit_breaker_trips_total",
                "Total number of circuit breaker trips",
                new[] { "breaker_name", "from_state", "to_state" });

            // Initialize Gauges
            _activeMachines = factory.CreateGauge(
                "xstatenet_active_machines",
                "Number of active state machines",
                new[] { "node_id" });

            _activeConnections = factory.CreateGauge(
                "xstatenet_active_connections",
                "Number of active connections",
                new[] { "transport_type" });

            _queueDepth = factory.CreateGauge(
                "xstatenet_queue_depth",
                "Current queue depth",
                new[] { "queue_name" });

            _memoryUsage = factory.CreateGauge(
                "xstatenet_memory_usage_bytes",
                "Memory usage in bytes",
                new[] { "type" });

            _cpuUsage = factory.CreateGauge(
                "xstatenet_cpu_usage_percent",
                "CPU usage percentage",
                new[] { "core" });

            // Initialize Histograms
            _eventProcessingDuration = factory.CreateHistogram(
                "xstatenet_event_processing_duration_seconds",
                "Event processing duration in seconds",
                new[] { "machine_id", "event_type" },
                new HistogramConfiguration
                {
                    Buckets = Histogram.ExponentialBuckets(0.001, 2, 10) // 1ms to ~1s
                });

            _stateTransitionDuration = factory.CreateHistogram(
                "xstatenet_state_transition_duration_seconds",
                "State transition duration in seconds",
                new[] { "machine_id", "from_state", "to_state" },
                new HistogramConfiguration
                {
                    Buckets = Histogram.ExponentialBuckets(0.0001, 2, 12) // 0.1ms to ~400ms
                });

            _requestDuration = factory.CreateHistogram(
                "xstatenet_request_duration_seconds",
                "Request duration in seconds",
                new[] { "method", "endpoint", "status_code" },
                new HistogramConfiguration
                {
                    Buckets = Histogram.LinearBuckets(0.005, 0.005, 20) // 5ms increments up to 100ms
                });

            _messageSize = factory.CreateHistogram(
                "xstatenet_message_size_bytes",
                "Message size in bytes",
                new[] { "message_type", "direction" },
                new HistogramConfiguration
                {
                    Buckets = Histogram.ExponentialBuckets(100, 10, 8) // 100B to 100MB
                });

            // Initialize Summary
            _eventLatency = factory.CreateSummary(
                "xstatenet_event_latency_seconds",
                "Event latency in seconds",
                new[] { "machine_id", "event_type" },
                new SummaryConfiguration
                {
                    Objectives = new[]
                    {
                        new QuantileEpsilonPair(0.5, 0.05),   // Median
                        new QuantileEpsilonPair(0.9, 0.01),   // 90th percentile
                        new QuantileEpsilonPair(0.95, 0.005), // 95th percentile
                        new QuantileEpsilonPair(0.99, 0.001)  // 99th percentile
                    },
                    MaxAge = TimeSpan.FromMinutes(5),
                    AgeBuckets = 5
                });

            // Start metric server if enabled
            if (options.EnableHttpServer)
            {
                _metricServer = new MetricServer(
                    hostname: options.HttpServerHostname,
                    port: options.HttpServerPort,
                    url: options.HttpServerUrl,
                    registry: _registry);
                _metricServer.Start();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordEventProcessed(string machineId, string eventType, ProcessingStatus status)
        {
            var labels = GetOrCreateLabels($"{machineId}:{eventType}:{status}", machineId, eventType, status.ToString());
            _eventProcessedTotal.WithLabels(labels).Inc();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordStateTransition(string machineId, string fromState, string toState, TimeSpan duration)
        {
            var labels = GetOrCreateLabels($"{machineId}:{fromState}:{toState}", machineId, fromState, toState);
            _stateTransitionTotal.WithLabels(labels).Inc();
            _stateTransitionDuration.WithLabels(labels).Observe(duration.TotalSeconds);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordError(string machineId, string errorType, string component)
        {
            var labels = GetOrCreateLabels($"{machineId}:{errorType}:{component}", machineId, errorType, component);
            _errorTotal.WithLabels(labels).Inc();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordEventProcessingTime(string machineId, string eventType, TimeSpan duration)
        {
            var labels = GetOrCreateLabels($"{machineId}:{eventType}", machineId, eventType);
            _eventProcessingDuration.WithLabels(labels).Observe(duration.TotalSeconds);
            _eventLatency.WithLabels(labels).Observe(duration.TotalSeconds);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetActiveMachines(string nodeId, int count)
        {
            _activeMachines.WithLabels(nodeId).Set(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetActiveConnections(string transportType, int count)
        {
            _activeConnections.WithLabels(transportType).Set(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetQueueDepth(string queueName, int depth)
        {
            _queueDepth.WithLabels(queueName).Set(depth);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordRetry(string policyName, int attempt, bool success)
        {
            var result = success ? "success" : "failure";
            var labels = GetOrCreateLabels($"{policyName}:{attempt}:{result}", policyName, attempt.ToString(), result);
            _retryTotal.WithLabels(labels).Inc();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordCircuitBreakerStateChange(string breakerName, string fromState, string toState)
        {
            var labels = GetOrCreateLabels($"{breakerName}:{fromState}:{toState}", breakerName, fromState, toState);
            _circuitBreakerTripsTotal.WithLabels(labels).Inc();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordRequestMetrics(string method, string endpoint, int statusCode, TimeSpan duration)
        {
            var labels = GetOrCreateLabels(
                $"{method}:{endpoint}:{statusCode}",
                method,
                endpoint,
                statusCode.ToString());
            _requestDuration.WithLabels(labels).Observe(duration.TotalSeconds);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordMessageSize(string messageType, MessageDirection direction, long sizeBytes)
        {
            var labels = GetOrCreateLabels($"{messageType}:{direction}", messageType, direction.ToString());
            _messageSize.WithLabels(labels).Observe(sizeBytes);
        }

        public void UpdateSystemMetrics()
        {
            var process = Process.GetCurrentProcess();

            // Memory metrics
            _memoryUsage.WithLabels("working_set").Set(process.WorkingSet64);
            _memoryUsage.WithLabels("private").Set(process.PrivateMemorySize64);
            _memoryUsage.WithLabels("virtual").Set(process.VirtualMemorySize64);

            // CPU metrics (simplified - for production use performance counters)
            _cpuUsage.WithLabels("total").Set(process.TotalProcessorTime.TotalSeconds);
        }

        public async Task<string> GetMetricsAsync()
        {
            using var stream = new System.IO.MemoryStream();
            await _registry.CollectAndExportAsTextAsync(stream);
            stream.Position = 0;
            using var reader = new System.IO.StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string[] GetOrCreateLabels(string key, params string[] labels)
        {
            // Fast path: check if labels are cached
            _labelCacheLock.EnterReadLock();
            try
            {
                if (_labelCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }
            finally
            {
                _labelCacheLock.ExitReadLock();
            }

            // Slow path: create and cache labels
            _labelCacheLock.EnterWriteLock();
            try
            {
                // Double-check after acquiring write lock
                if (_labelCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                // Clone labels to ensure they're not modified
                var labelsCopy = new string[labels.Length];
                Array.Copy(labels, labelsCopy, labels.Length);

                // Cache with size limit to prevent unbounded growth
                if (_labelCache.Count < 10000)
                {
                    _labelCache[key] = labelsCopy;
                }

                return labelsCopy;
            }
            finally
            {
                _labelCacheLock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _metricServer?.Stop();
            _metricServer?.Dispose();
            _labelCacheLock?.Dispose();
        }
    }

    public interface IMetricsCollector : IDisposable
    {
        void RecordEventProcessed(string machineId, string eventType, ProcessingStatus status);
        void RecordStateTransition(string machineId, string fromState, string toState, TimeSpan duration);
        void RecordError(string machineId, string errorType, string component);
        void RecordEventProcessingTime(string machineId, string eventType, TimeSpan duration);
        void SetActiveMachines(string nodeId, int count);
        void SetActiveConnections(string transportType, int count);
        void SetQueueDepth(string queueName, int depth);
        void RecordRetry(string policyName, int attempt, bool success);
        void RecordCircuitBreakerStateChange(string breakerName, string fromState, string toState);
        void RecordRequestMetrics(string method, string endpoint, int statusCode, TimeSpan duration);
        void RecordMessageSize(string messageType, MessageDirection direction, long sizeBytes);
        void UpdateSystemMetrics();
        Task<string> GetMetricsAsync();
    }

    public class PrometheusMetricsOptions
    {
        public bool EnableHttpServer { get; set; } = true;
        public string HttpServerHostname { get; set; } = "localhost";
        public int HttpServerPort { get; set; } = 9090;
        public string HttpServerUrl { get; set; } = "metrics/";
        public bool UseDefaultRegistry { get; set; } = false;
    }

    public enum ProcessingStatus
    {
        Success,
        Failure,
        Timeout,
        Rejected
    }

    public enum MessageDirection
    {
        Inbound,
        Outbound
    }
}