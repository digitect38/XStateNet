using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.Distributed.Metrics;
using XStateNet.Distributed.Resilience;
using XStateNet.Distributed.Security;
using XStateNet.Distributed.Telemetry;

namespace XStateNet.Distributed.Monitoring
{
    /// <summary>
    /// Integrated monitoring system combining metrics, tracing, health checks, and alerts
    /// </summary>
    public sealed class IntegratedMonitoringSystem : IMonitoringSystem
    {
        private readonly IMetricsCollector _metrics;
        private readonly IDistributedTracing _tracing;
        private readonly ISecurityLayer _security;
        private readonly MonitoringOptions _options;

        // Health check management
        private readonly ConcurrentDictionary<string, HealthCheckRegistration> _healthChecks;
        private readonly Timer _healthCheckTimer;

        // Alert management
        private readonly ConcurrentDictionary<string, Alert> _activeAlerts;
        private readonly ConcurrentQueue<Alert> _alertHistory;

        // System metrics
        private readonly SystemMetricsCollector _systemMetrics;

        // Circuit breakers for monitoring resilience
        private readonly ConcurrentDictionary<string, ICircuitBreaker> _monitoringCircuitBreakers;

        // Performance tracking
        private long _totalHealthChecks;
        private long _failedHealthChecks;
        private long _alertsTriggered;
        private long _alertsResolved;

        public IntegratedMonitoringSystem(
            IMetricsCollector metrics,
            IDistributedTracing tracing,
            ISecurityLayer security,
            MonitoringOptions options)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _tracing = tracing ?? throw new ArgumentNullException(nameof(tracing));
            _security = security ?? throw new ArgumentNullException(nameof(security));
            _options = options ?? throw new ArgumentNullException(nameof(options));

            _healthChecks = new ConcurrentDictionary<string, HealthCheckRegistration>(StringComparer.Ordinal);
            _activeAlerts = new ConcurrentDictionary<string, Alert>(StringComparer.Ordinal);
            _alertHistory = new ConcurrentQueue<Alert>();
            _systemMetrics = new SystemMetricsCollector();
            _monitoringCircuitBreakers = new ConcurrentDictionary<string, ICircuitBreaker>();

            // Initialize circuit breakers for critical monitoring paths
            InitializeMonitoringCircuitBreakers();

            // Start health check timer
            _healthCheckTimer = new Timer(
                RunHealthChecksAsync,
                null,
                TimeSpan.FromSeconds(10),
                _options.HealthCheckInterval);

            // Register default health checks
            RegisterDefaultHealthChecks();
        }

        public void RegisterHealthCheck(string name, Func<CancellationToken, Task<HealthCheckResult>> checkFunc, HealthCheckOptions? options = null)
        {
            var registration = new HealthCheckRegistration
            {
                Name = name,
                CheckFunction = checkFunc,
                Options = options ?? new HealthCheckOptions(),
                LastCheckTime = DateTime.MinValue,
                LastResult = HealthCheckResult.Healthy()
            };

            _healthChecks.TryAdd(name, registration);
        }

        public async Task<HealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default)
        {
            var results = new ConcurrentDictionary<string, HealthCheckResult>();
            var tasks = new List<Task>();

            foreach (var registration in _healthChecks.Values)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var result = await ExecuteHealthCheckAsync(registration, cancellationToken);
                    results.TryAdd(registration.Name, result);
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            var overallStatus = CalculateOverallStatus(results.Values);

            return new HealthReport
            {
                Status = overallStatus,
                Timestamp = DateTime.UtcNow,
                Results = new ConcurrentDictionary<string, HealthCheckResult>(
                    results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)),
                Duration = TimeSpan.FromMilliseconds(results.Values.Sum(r => r.Duration.TotalMilliseconds))
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrackStateMachineEvent(string machineId, string eventName, Action<Activity?> operation)
        {
            var activity = _tracing.StartStateMachineActivity(machineId, eventName);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                operation(activity);
                _metrics.RecordEventProcessed(machineId, eventName, ProcessingStatus.Success);
                _metrics.RecordEventProcessingTime(machineId, eventName, stopwatch.Elapsed);
                _tracing.EndActivity(activity);
            }
            catch (Exception ex)
            {
                _metrics.RecordEventProcessed(machineId, eventName, ProcessingStatus.Failure);
                _metrics.RecordError(machineId, ex.GetType().Name, "StateMachine");
                _tracing.RecordException(activity, ex);
                _tracing.EndActivity(activity, ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> operation, string? machineId = null)
        {
            var activity = _tracing.StartStateMachineActivity(machineId ?? "system", operationName);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await operation();
                _metrics.RecordRequestMetrics("operation", operationName, 200, stopwatch.Elapsed);
                _tracing.EndActivity(activity);
                return result;
            }
            catch (Exception ex)
            {
                _metrics.RecordRequestMetrics("operation", operationName, 500, stopwatch.Elapsed);
                _metrics.RecordError(machineId ?? "system", ex.GetType().Name, operationName);
                _tracing.RecordException(activity, ex);
                _tracing.EndActivity(activity, ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        public void TriggerAlert(Alert alert)
        {
            alert.Id = Guid.NewGuid().ToString();
            alert.TriggeredAt = DateTime.UtcNow;
            alert.Status = AlertStatus.Active;

            if (_activeAlerts.TryAdd(alert.Id, alert))
            {
                Interlocked.Increment(ref _alertsTriggered);

                // Record in metrics
                _metrics.RecordError(alert.Source, alert.Severity.ToString(), "Alert");

                // Add to history (keep last 1000 alerts)
                _alertHistory.Enqueue(alert);
                while (_alertHistory.Count > 1000)
                {
                    _alertHistory.TryDequeue(out _);
                }

                // Execute alert actions
                ExecuteAlertActions(alert);
            }
        }

        public void ResolveAlert(string alertId, string? resolution = null)
        {
            if (_activeAlerts.TryRemove(alertId, out var alert))
            {
                alert.ResolvedAt = DateTime.UtcNow;
                alert.Status = AlertStatus.Resolved;
                alert.Resolution = resolution;

                Interlocked.Increment(ref _alertsResolved);
                _alertHistory.Enqueue(alert);
            }
        }

        public MonitoringDashboard GetDashboard()
        {
            var systemMetrics = _systemMetrics.Collect();
            var securityMetrics = _security.GetMetrics();
            var tracingMetrics = _tracing.GetMetrics();

            return new MonitoringDashboard
            {
                Timestamp = DateTime.UtcNow,
                SystemMetrics = systemMetrics,
                SecurityMetrics = new DashboardSecurityMetrics
                {
                    AuthenticationSuccessRate = securityMetrics.AuthenticationAttempts > 0
                        ? (double)securityMetrics.AuthenticationSuccesses / securityMetrics.AuthenticationAttempts
                        : 1.0,
                    ActiveSessions = securityMetrics.CachedTokens,
                    RateLimitHits = securityMetrics.RateLimitHits
                },
                TracingMetrics = new DashboardTracingMetrics
                {
                    TotalSpans = tracingMetrics.TotalSpans,
                    ActiveSpans = tracingMetrics.ActiveSpans,
                    ErrorRate = tracingMetrics.TotalSpans > 0
                        ? (double)tracingMetrics.ErrorSpans / tracingMetrics.TotalSpans
                        : 0.0,
                    AverageSpanDuration = tracingMetrics.AverageSpanDuration
                },
                HealthStatus = GetQuickHealthStatus(),
                ActiveAlerts = _activeAlerts.Values.ToList(),
                RecentAlerts = _alertHistory.Take(10).ToList(),
                HealthCheckSummary = new HealthCheckSummary
                {
                    TotalChecks = _totalHealthChecks,
                    FailedChecks = _failedHealthChecks,
                    SuccessRate = _totalHealthChecks > 0
                        ? 1.0 - ((double)_failedHealthChecks / _totalHealthChecks)
                        : 1.0
                }
            };
        }

        private async Task<HealthCheckResult> ExecuteHealthCheckAsync(
            HealthCheckRegistration registration,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            Interlocked.Increment(ref _totalHealthChecks);

            try
            {
                // Use circuit breaker for resilience
                if (_monitoringCircuitBreakers.TryGetValue($"health_{registration.Name}", out var circuitBreaker))
                {
                    var result = await circuitBreaker.ExecuteAsync(
                        () => registration.CheckFunction(cancellationToken),
                        cancellationToken);

                    result.Duration = stopwatch.Elapsed;
                    registration.LastResult = result;
                    registration.LastCheckTime = DateTime.UtcNow;

                    if (result.Status != HealthStatus.Healthy)
                    {
                        Interlocked.Increment(ref _failedHealthChecks);
                    }

                    return result;
                }
                else
                {
                    var result = await registration.CheckFunction(cancellationToken);
                    result.Duration = stopwatch.Elapsed;
                    return result;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedHealthChecks);
                return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}");
            }
        }

        private void RunHealthChecksAsync(object? state)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var report = await GetHealthReportAsync();

                    // Update metrics
                    _metrics.SetActiveMachines("health_checks", _healthChecks.Count);

                    // Check for alerts
                    foreach (var result in report.Results)
                    {
                        if (result.Value.Status == HealthStatus.Unhealthy)
                        {
                            var alert = new Alert
                            {
                                Title = $"Health Check Failed: {result.Key}",
                                Description = result.Value.Description ?? "Health check returned unhealthy status",
                                Severity = AlertSeverity.Warning,
                                Source = result.Key,
                                Tags = new[] { "health", "automated" }
                            };

                            TriggerAlert(alert);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _metrics.RecordError("monitoring", ex.GetType().Name, "HealthCheck");
                }
            });
        }

        private void ExecuteAlertActions(Alert alert)
        {
            // Log the alert
            Debug.WriteLine($"[ALERT] {alert.Severity}: {alert.Title} - {alert.Description}");

            // Add to metrics
            _metrics.RecordError(alert.Source, $"Alert_{alert.Severity}", "Monitoring");

            // Execute configured actions based on severity
            if (alert.Severity == AlertSeverity.Critical && _options.EnableAutoRemediation)
            {
                // Trigger auto-remediation if configured
                _ = Task.Run(() => ExecuteRemediationAsync(alert));
            }
        }

        private async Task ExecuteRemediationAsync(Alert alert)
        {
            try
            {
                // Implement auto-remediation logic here
                await Task.Delay(100); // Placeholder

                // Record remediation attempt
                _metrics.RecordEventProcessed("remediation", alert.Source, ProcessingStatus.Success);
            }
            catch (Exception ex)
            {
                _metrics.RecordError("remediation", ex.GetType().Name, alert.Source);
            }
        }

        private void InitializeMonitoringCircuitBreakers()
        {
            var circuitBreakerOptions = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                BreakDuration = TimeSpan.FromSeconds(30),
                SuccessCountInHalfOpen = 2
            };

            foreach (var healthCheck in _options.CriticalHealthChecks)
            {
                _monitoringCircuitBreakers[$"health_{healthCheck}"] =
                    new CircuitBreaker($"health_{healthCheck}", circuitBreakerOptions);
            }
        }

        private void RegisterDefaultHealthChecks()
        {
            // Memory health check
            RegisterHealthCheck("memory", async ct =>
            {
                var process = Process.GetCurrentProcess();
                var memoryMB = process.WorkingSet64 / (1024 * 1024);

                if (memoryMB > _options.MemoryThresholdMB)
                {
                    return HealthCheckResult.Unhealthy($"Memory usage too high: {memoryMB}MB");
                }

                return HealthCheckResult.Healthy($"Memory usage: {memoryMB}MB");
            });

            // CPU health check
            RegisterHealthCheck("cpu", async ct =>
            {
                var cpuUsage = await _systemMetrics.GetCpuUsageAsync();

                if (cpuUsage > _options.CpuThresholdPercent)
                {
                    return HealthCheckResult.Degraded($"CPU usage high: {cpuUsage:F1}%");
                }

                return HealthCheckResult.Healthy($"CPU usage: {cpuUsage:F1}%");
            });
        }

        private HealthStatus CalculateOverallStatus(IEnumerable<HealthCheckResult> results)
        {
            var hasUnhealthy = false;
            var hasDegraded = false;

            foreach (var result in results)
            {
                if (result.Status == HealthStatus.Unhealthy)
                    hasUnhealthy = true;
                else if (result.Status == HealthStatus.Degraded)
                    hasDegraded = true;
            }

            if (hasUnhealthy) return HealthStatus.Unhealthy;
            if (hasDegraded) return HealthStatus.Degraded;
            return HealthStatus.Healthy;
        }

        private HealthStatus GetQuickHealthStatus()
        {
            var unhealthyCount = _healthChecks.Values.Count(hc => hc.LastResult.Status == HealthStatus.Unhealthy);
            var degradedCount = _healthChecks.Values.Count(hc => hc.LastResult.Status == HealthStatus.Degraded);

            if (unhealthyCount > 0) return HealthStatus.Unhealthy;
            if (degradedCount > 0) return HealthStatus.Degraded;
            return HealthStatus.Healthy;
        }

        public void Dispose()
        {
            _healthCheckTimer?.Dispose();

            foreach (var circuitBreaker in _monitoringCircuitBreakers.Values)
            {
                circuitBreaker?.Dispose();
            }

            _systemMetrics?.Dispose();
        }
    }

    public interface IMonitoringSystem : IDisposable
    {
        void RegisterHealthCheck(string name, Func<CancellationToken, Task<HealthCheckResult>> checkFunc, HealthCheckOptions? options = null);
        Task<HealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default);
        void TrackStateMachineEvent(string machineId, string eventName, Action<Activity?> operation);
        Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> operation, string? machineId = null);
        void TriggerAlert(Alert alert);
        void ResolveAlert(string alertId, string? resolution = null);
        MonitoringDashboard GetDashboard();
    }

    public class MonitoringOptions
    {
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
        public bool EnableAutoRemediation { get; set; } = false;
        public int MemoryThresholdMB { get; set; } = 2048;
        public double CpuThresholdPercent { get; set; } = 80.0;
        public List<string> CriticalHealthChecks { get; set; } = new();
    }

    public class HealthCheckRegistration
    {
        public string Name { get; set; } = string.Empty;
        public Func<CancellationToken, Task<HealthCheckResult>> CheckFunction { get; set; } = null!;
        public HealthCheckOptions Options { get; set; } = new();
        public DateTime LastCheckTime { get; set; }
        public HealthCheckResult LastResult { get; set; } = HealthCheckResult.Healthy();
    }

    public class HealthCheckOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
        public int FailureThreshold { get; set; } = 3;
        public bool IsCritical { get; set; } = false;
    }

    public class HealthCheckResult
    {
        public HealthStatus Status { get; set; }
        public string? Description { get; set; }
        public ConcurrentDictionary<string, object> Data { get; set; } = new();
        public TimeSpan Duration { get; set; }

        public static HealthCheckResult Healthy(string? description = null) =>
            new() { Status = HealthStatus.Healthy, Description = description };

        public static HealthCheckResult Degraded(string? description = null) =>
            new() { Status = HealthStatus.Degraded, Description = description };

        public static HealthCheckResult Unhealthy(string? description = null) =>
            new() { Status = HealthStatus.Unhealthy, Description = description };
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    public class HealthReport
    {
        public HealthStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
        public ConcurrentDictionary<string, HealthCheckResult> Results { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }

    public class Alert
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTime TriggeredAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public AlertStatus Status { get; set; }
        public string? Resolution { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public enum AlertStatus
    {
        Active,
        Acknowledged,
        Resolved
    }

    public class MonitoringDashboard
    {
        public DateTime Timestamp { get; set; }
        public SystemMetrics SystemMetrics { get; set; } = new();
        public DashboardSecurityMetrics SecurityMetrics { get; set; } = new();
        public DashboardTracingMetrics TracingMetrics { get; set; } = new();
        public HealthStatus HealthStatus { get; set; }
        public List<Alert> ActiveAlerts { get; set; } = new();
        public List<Alert> RecentAlerts { get; set; } = new();
        public HealthCheckSummary HealthCheckSummary { get; set; } = new();
    }

    public class DashboardSecurityMetrics
    {
        public double AuthenticationSuccessRate { get; set; }
        public int ActiveSessions { get; set; }
        public long RateLimitHits { get; set; }
    }

    public class DashboardTracingMetrics
    {
        public long TotalSpans { get; set; }
        public long ActiveSpans { get; set; }
        public double ErrorRate { get; set; }
        public double AverageSpanDuration { get; set; }
    }

    public class HealthCheckSummary
    {
        public long TotalChecks { get; set; }
        public long FailedChecks { get; set; }
        public double SuccessRate { get; set; }
    }

    public class SystemMetrics
    {
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public int ThreadCount { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    internal class SystemMetricsCollector : IDisposable
    {
        private readonly Process _process;
        private DateTime _lastCpuCheck;
        private TimeSpan _lastTotalProcessorTime;

        public SystemMetricsCollector()
        {
            _process = Process.GetCurrentProcess();
            _lastCpuCheck = DateTime.UtcNow;
            _lastTotalProcessorTime = _process.TotalProcessorTime;
        }

        public SystemMetrics Collect()
        {
            return new SystemMetrics
            {
                CpuUsage = CalculateCpuUsage(),
                MemoryUsage = _process.WorkingSet64,
                ThreadCount = _process.Threads.Count,
                Uptime = DateTime.UtcNow - _process.StartTime
            };
        }

        public async Task<double> GetCpuUsageAsync()
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = _process.TotalProcessorTime;

            await Task.Delay(100);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = _process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;

            return (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100;
        }

        private double CalculateCpuUsage()
        {
            var currentTime = DateTime.UtcNow;
            var currentTotalProcessorTime = _process.TotalProcessorTime;

            var timeDiff = currentTime - _lastCpuCheck;
            var cpuDiff = currentTotalProcessorTime - _lastTotalProcessorTime;

            _lastCpuCheck = currentTime;
            _lastTotalProcessorTime = currentTotalProcessorTime;

            if (timeDiff.TotalMilliseconds > 0)
            {
                return (cpuDiff.TotalMilliseconds / (Environment.ProcessorCount * timeDiff.TotalMilliseconds)) * 100;
            }

            return 0;
        }

        public void Dispose()
        {
            _process?.Dispose();
        }
    }
}