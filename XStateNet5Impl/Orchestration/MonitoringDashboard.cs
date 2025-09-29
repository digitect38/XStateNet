using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XStateNet.Orchestration
{
    /// <summary>
    /// Real-time monitoring dashboard for orchestrator operations
    /// </summary>
    public class MonitoringDashboard
    {
        private readonly OrchestratorMetrics _metrics;
        private readonly Timer _refreshTimer;
        private readonly object _lock = new object();
        private bool _isRunning = false;

        public MonitoringDashboard(OrchestratorMetrics metrics)
        {
            _metrics = metrics;
            _refreshTimer = new Timer(RefreshDisplay, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void StartMonitoring(TimeSpan refreshInterval)
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _refreshTimer.Change(TimeSpan.Zero, refreshInterval);
            }
        }

        public void StopMonitoring()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private void RefreshDisplay(object? state)
        {
            try
            {
                var health = _metrics.GetHealthStatus();
                var snapshot = _metrics.GetCurrentMetrics();

                DisplayDashboard(health, snapshot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dashboard] Error refreshing display: {ex.Message}");
            }
        }

        private void DisplayDashboard(HealthStatus health, MetricsSnapshot snapshot)
        {
            var sb = new StringBuilder();

            // Clear screen and position cursor
            Console.SetCursorPosition(0, 0);

            // Header
            sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║                     XStateNet Orchestrator Dashboard                        ║");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");

            // Health Status
            var healthIcon = health.Level switch
            {
                HealthLevel.Healthy => "🟢",
                HealthLevel.Degraded => "🟡",
                HealthLevel.Unhealthy => "🔴",
                _ => "⚪"
            };

            sb.AppendLine($"║ Status: {healthIcon} {health.Level,-12} │ Uptime: {FormatDuration(health.Uptime),-20} ║");
            sb.AppendLine($"║ Throughput: {health.ThroughputPerSecond:F1} evt/s {"",-8} │ Avg Latency: {health.AverageLatencyMs:F1} ms {"",-12} ║");

            if (health.Issues.Any())
            {
                sb.AppendLine("╠──────────────────────────────────────────────────────────────────────────────╣");
                sb.AppendLine("║ Issues:                                                                      ║");
                foreach (var issue in health.Issues.Take(3))
                {
                    sb.AppendLine($"║ • {issue,-75} ║");
                }
            }

            sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");

            // Event Statistics
            sb.AppendLine("║ EVENT STATISTICS                                                             ║");
            sb.AppendLine("╠──────────────────────────────────────────────────────────────────────────────╣");
            sb.AppendLine($"║ Total Events: {snapshot.TotalEvents,-10} │ Success Rate: {snapshot.SuccessRate:F1}% {"",-20} ║");
            sb.AppendLine($"║ Success: {snapshot.Counters.GetValueOrDefault("events.success", 0),-14} │ Failure Rate: {snapshot.FailureRate:F1}% {"",-20} ║");
            sb.AppendLine($"║ Failed: {snapshot.Counters.GetValueOrDefault("events.failed", 0),-15} │ Throttle Rate: {snapshot.ThrottleRate:F1}% {"",-19} ║");
            sb.AppendLine($"║ Timeout: {snapshot.Counters.GetValueOrDefault("events.timeout", 0),-14} │ Throttled: {snapshot.Counters.GetValueOrDefault("events.throttled", 0),-25} ║");

            sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");

            // System Resources
            sb.AppendLine("║ SYSTEM RESOURCES                                                             ║");
            sb.AppendLine("╠──────────────────────────────────────────────────────────────────────────────╣");
            sb.AppendLine($"║ Active Machines: {snapshot.ActiveMachines,-8} │ Pending Requests: {snapshot.PendingRequests,-22} ║");
            sb.AppendLine($"║ Queued Events: {snapshot.QueuedEvents,-10} │ Registered: {snapshot.Counters.GetValueOrDefault("machines.registered", 0),-26} ║");
            sb.AppendLine($"║ Disposed: {snapshot.Counters.GetValueOrDefault("machines.disposed", 0),-13} │                                            ║");

            sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");

            // Performance Metrics
            if (snapshot.Histograms.TryGetValue("latency.events", out var eventLatency))
            {
                sb.AppendLine("║ PERFORMANCE LATENCY (ms)                                                     ║");
                sb.AppendLine("╠──────────────────────────────────────────────────────────────────────────────╣");
                sb.AppendLine($"║ Mean: {eventLatency.Mean:F1} {"",-11} │ P50: {eventLatency.P50:F1} {"",-7} │ P95: {eventLatency.P95:F1} {"",-7} │ P99: {eventLatency.P99:F1} {"",-7} ║");
                sb.AppendLine($"║ Min: {eventLatency.Min:F1} {"",-12} │ Max: {eventLatency.Max:F1} {"",-22} │ Count: {eventLatency.Count,-10} ║");
                sb.AppendLine("╠══════════════════════════════════════════════════════════════════════════════╣");
            }

            // Recent Events
            var recentEvents = _metrics.GetRecentEvents(TimeSpan.FromMinutes(1));
            sb.AppendLine("║ RECENT EVENTS (Last 1 minute)                                               ║");
            sb.AppendLine("╠──────────────────────────────────────────────────────────────────────────────╣");

            if (!recentEvents.Any())
            {
                sb.AppendLine("║ No recent events                                                             ║");
            }
            else
            {
                foreach (var evt in recentEvents.Take(5))
                {
                    var time = evt.Timestamp.ToString("HH:mm:ss");
                    var type = evt.Type;
                    var machine = evt.MachineId.Length > 15 ? evt.MachineId.Substring(0, 12) + "..." : evt.MachineId;
                    var eventName = evt.EventName.Length > 15 ? evt.EventName.Substring(0, 12) + "..." : evt.EventName;

                    sb.AppendLine($"║ {time} │ {type,-15} │ {machine,-15} │ {eventName,-15} ║");
                }
            }

            sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            sb.AppendLine($"Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // Clear any remaining lines
            for (int i = 0; i < 5; i++)
            {
                sb.AppendLine(new string(' ', 80));
            }

            Console.Write(sb.ToString());
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.Seconds}s";
        }

        public void DisplaySummaryReport()
        {
            var health = _metrics.GetHealthStatus();
            var snapshot = _metrics.GetCurrentMetrics();

            Console.WriteLine("\n╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                           ORCHESTRATOR SUMMARY REPORT                       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine($"🎯 Overall Health: {health.Level}");
            Console.WriteLine($"⏱️  Uptime: {FormatDuration(health.Uptime)}");
            Console.WriteLine($"🔄 Total Events Processed: {snapshot.TotalEvents:N0}");
            Console.WriteLine($"✅ Success Rate: {snapshot.SuccessRate:F2}%");
            Console.WriteLine($"🚀 Peak Throughput: {health.ThroughputPerSecond:F1} events/second");
            Console.WriteLine();

            if (snapshot.Histograms.TryGetValue("latency.events", out var latency))
            {
                Console.WriteLine("📊 Performance Metrics:");
                Console.WriteLine($"   • Average Latency: {latency.Mean:F2} ms");
                Console.WriteLine($"   • 95th Percentile: {latency.P95:F2} ms");
                Console.WriteLine($"   • 99th Percentile: {latency.P99:F2} ms");
                Console.WriteLine();
            }

            Console.WriteLine("🔧 Resource Utilization:");
            Console.WriteLine($"   • Active Machines: {snapshot.ActiveMachines}");
            Console.WriteLine($"   • Peak Pending Requests: {snapshot.PendingRequests}");
            Console.WriteLine($"   • Peak Queued Events: {snapshot.QueuedEvents}");
            Console.WriteLine();

            if (health.Issues.Any())
            {
                Console.WriteLine("⚠️  Issues Detected:");
                foreach (var issue in health.Issues)
                {
                    Console.WriteLine($"   • {issue}");
                }
                Console.WriteLine();
            }

            Console.WriteLine("📈 Event Breakdown:");
            Console.WriteLine($"   • Successful: {snapshot.Counters.GetValueOrDefault("events.success", 0):N0}");
            Console.WriteLine($"   • Failed: {snapshot.Counters.GetValueOrDefault("events.failed", 0):N0}");
            Console.WriteLine($"   • Timed Out: {snapshot.Counters.GetValueOrDefault("events.timeout", 0):N0}");
            Console.WriteLine($"   • Throttled: {snapshot.Counters.GetValueOrDefault("events.throttled", 0):N0}");
        }

        public void Dispose()
        {
            StopMonitoring();
            _refreshTimer?.Dispose();
        }
    }

    /// <summary>
    /// Logger for structured orchestrator events
    /// </summary>
    public class OrchestratorLogger
    {
        private readonly string _logLevel;
        private readonly bool _enableStructuredLogging;

        public OrchestratorLogger(string logLevel = "INFO", bool enableStructuredLogging = true)
        {
            _logLevel = logLevel;
            _enableStructuredLogging = enableStructuredLogging;
        }

        public void LogEventProcessed(string machineId, string eventName, TimeSpan duration, bool success)
        {
            if (_enableStructuredLogging)
            {
                var status = success ? "SUCCESS" : "FAILED";
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{_logLevel}] EVENT_PROCESSED " +
                                $"machine_id={machineId} event_name={eventName} duration_ms={duration.TotalMilliseconds:F2} status={status}");
            }
        }

        public void LogMachineRegistered(string machineId, string machineType)
        {
            if (_enableStructuredLogging)
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{_logLevel}] MACHINE_REGISTERED " +
                                $"machine_id={machineId} machine_type={machineType}");
            }
        }

        public void LogHealthCheck(HealthStatus health)
        {
            if (_enableStructuredLogging)
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{_logLevel}] HEALTH_CHECK " +
                                $"status={health.Level} throughput={health.ThroughputPerSecond:F1} " +
                                $"latency_ms={health.AverageLatencyMs:F1} issues_count={health.Issues.Count}");
            }
        }

        public void LogPerformanceAlert(string alertType, string message)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [ALERT] PERFORMANCE_ALERT " +
                            $"type={alertType} message=\"{message}\"");
        }
    }
}