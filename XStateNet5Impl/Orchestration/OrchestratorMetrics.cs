using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace XStateNet.Orchestration
{
    /// <summary>
    /// Comprehensive metrics collection and monitoring for EventBusOrchestrator
    /// </summary>
    public class OrchestratorMetrics
    {
        private readonly ConcurrentDictionary<string, MetricCounter> _counters = new();
        private readonly ConcurrentDictionary<string, MetricGauge> _gauges = new();
        private readonly ConcurrentDictionary<string, MetricHistogram> _histograms = new();
        private readonly ConcurrentQueue<MetricEvent> _events = new();
        private readonly Stopwatch _uptime = Stopwatch.StartNew();

        // Performance counters
        private long _totalEvents = 0;
        private long _successfulEvents = 0;
        private long _failedEvents = 0;
        private long _timeoutEvents = 0;
        private long _throttledEvents = 0;

        // Current state gauges
        private int _activeMachines = 0;
        private int _pendingRequests = 0;
        private int _queuedEvents = 0;

        // Health indicators
        private double _averageLatency = 0;
        private double _throughputPerSecond = 0;
        private DateTime _lastHealthCheck = DateTime.UtcNow;

        public OrchestratorMetrics()
        {
            InitializeMetrics();
        }

        private void InitializeMetrics()
        {
            // Initialize core counters
            _counters["events.total"] = new MetricCounter("Total Events Processed");
            _counters["events.success"] = new MetricCounter("Successful Events");
            _counters["events.failed"] = new MetricCounter("Failed Events");
            _counters["events.timeout"] = new MetricCounter("Timed Out Events");
            _counters["events.throttled"] = new MetricCounter("Throttled Events");
            _counters["machines.registered"] = new MetricCounter("Machines Registered");
            _counters["machines.disposed"] = new MetricCounter("Machines Disposed");

            // Initialize gauges
            _gauges["machines.active"] = new MetricGauge("Active Machines", () => _activeMachines);
            _gauges["requests.pending"] = new MetricGauge("Pending Requests", () => _pendingRequests);
            _gauges["events.queued"] = new MetricGauge("Queued Events", () => _queuedEvents);
            _gauges["system.uptime"] = new MetricGauge("System Uptime (seconds)", () => _uptime.Elapsed.TotalSeconds);

            // Initialize histograms for latency tracking
            _histograms["latency.events"] = new MetricHistogram("Event Processing Latency");
            _histograms["latency.requests"] = new MetricHistogram("Request Round-trip Latency");
            _histograms["throughput.events"] = new MetricHistogram("Events Per Second");
        }

        #region Event Recording

        public void RecordEventStart(Guid eventId, string machineId, string eventName)
        {
            var evt = new MetricEvent
            {
                Id = eventId,
                Type = "event.start",
                MachineId = machineId,
                EventName = eventName,
                Timestamp = DateTime.UtcNow
            };

            _events.Enqueue(evt);
            Interlocked.Increment(ref _totalEvents);
            _counters["events.total"].Increment();
        }

        public void RecordEventSuccess(Guid eventId, TimeSpan duration)
        {
            Interlocked.Increment(ref _successfulEvents);
            _counters["events.success"].Increment();
            _histograms["latency.events"].Record(duration.TotalMilliseconds);

            var evt = new MetricEvent
            {
                Id = eventId,
                Type = "event.success",
                Duration = duration,
                Timestamp = DateTime.UtcNow
            };
            _events.Enqueue(evt);
        }

        public void RecordEventFailure(Guid eventId, string reason, TimeSpan duration)
        {
            Interlocked.Increment(ref _failedEvents);
            _counters["events.failed"].Increment();

            var evt = new MetricEvent
            {
                Id = eventId,
                Type = "event.failure",
                Reason = reason,
                Duration = duration,
                Timestamp = DateTime.UtcNow
            };
            _events.Enqueue(evt);
        }

        public void RecordEventTimeout(Guid eventId, TimeSpan duration)
        {
            Interlocked.Increment(ref _timeoutEvents);
            _counters["events.timeout"].Increment();

            var evt = new MetricEvent
            {
                Id = eventId,
                Type = "event.timeout",
                Duration = duration,
                Timestamp = DateTime.UtcNow
            };
            _events.Enqueue(evt);
        }

        public void RecordEventThrottled(string machineId, string eventName)
        {
            Interlocked.Increment(ref _throttledEvents);
            _counters["events.throttled"].Increment();

            var evt = new MetricEvent
            {
                Type = "event.throttled",
                MachineId = machineId,
                EventName = eventName,
                Timestamp = DateTime.UtcNow
            };
            _events.Enqueue(evt);
        }

        #endregion

        #region Machine Lifecycle

        public void RecordMachineRegistered(string machineId, string machineType)
        {
            Interlocked.Increment(ref _activeMachines);
            _counters["machines.registered"].Increment();

            var evt = new MetricEvent
            {
                Type = "machine.registered",
                MachineId = machineId,
                MachineType = machineType,
                Timestamp = DateTime.UtcNow
            };
            _events.Enqueue(evt);
        }

        public void RecordMachineDisposed(string machineId)
        {
            Interlocked.Decrement(ref _activeMachines);
            _counters["machines.disposed"].Increment();

            var evt = new MetricEvent
            {
                Type = "machine.disposed",
                MachineId = machineId,
                Timestamp = DateTime.UtcNow
            };
            _events.Enqueue(evt);
        }

        #endregion

        #region Real-time State Updates

        public void UpdatePendingRequests(int count)
        {
            Interlocked.Exchange(ref _pendingRequests, count);
        }

        public void UpdateQueuedEvents(int count)
        {
            Interlocked.Exchange(ref _queuedEvents, count);
        }

        #endregion

        #region Health Metrics

        public HealthStatus GetHealthStatus()
        {
            var now = DateTime.UtcNow;
            var timeSinceLastCheck = now - _lastHealthCheck;

            // Calculate throughput over last interval
            var recentEvents = _events.Where(e => e.Timestamp > _lastHealthCheck).Count();
            var currentThroughput = recentEvents / Math.Max(timeSinceLastCheck.TotalSeconds, 1);

            // Calculate average latency from recent events
            var recentLatencies = _histograms["latency.events"].GetRecentSamples(TimeSpan.FromMinutes(1));
            var avgLatency = recentLatencies.Any() ? recentLatencies.Average() : 0;

            // Determine health level
            var health = HealthLevel.Healthy;
            var issues = new List<string>();

            if (avgLatency > 1000) // > 1 second average latency
            {
                health = HealthLevel.Degraded;
                issues.Add($"High latency: {avgLatency:F1}ms");
            }

            if (_throttledEvents > _successfulEvents * 0.1) // > 10% throttling
            {
                health = HealthLevel.Degraded;
                issues.Add($"High throttling rate: {(_throttledEvents * 100.0 / Math.Max(_totalEvents, 1)):F1}%");
            }

            if (_failedEvents > _successfulEvents * 0.05) // > 5% failure rate
            {
                health = HealthLevel.Unhealthy;
                issues.Add($"High failure rate: {(_failedEvents * 100.0 / Math.Max(_totalEvents, 1)):F1}%");
            }

            if (_pendingRequests > 1000) // Too many pending requests
            {
                health = HealthLevel.Unhealthy;
                issues.Add($"High pending requests: {_pendingRequests}");
            }

            _lastHealthCheck = now;

            return new HealthStatus
            {
                Level = health,
                Timestamp = now,
                Uptime = _uptime.Elapsed,
                ThroughputPerSecond = currentThroughput,
                AverageLatencyMs = avgLatency,
                Issues = issues,
                Metrics = GetCurrentMetrics()
            };
        }

        #endregion

        #region Metric Retrieval

        public MetricsSnapshot GetCurrentMetrics()
        {
            return new MetricsSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Counters = _counters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value),
                Gauges = _gauges.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetValue()),
                Histograms = _histograms.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetSummary()),

                // Derived metrics
                TotalEvents = _totalEvents,
                SuccessRate = _totalEvents > 0 ? (_successfulEvents * 100.0 / _totalEvents) : 0,
                FailureRate = _totalEvents > 0 ? (_failedEvents * 100.0 / _totalEvents) : 0,
                ThrottleRate = _totalEvents > 0 ? (_throttledEvents * 100.0 / _totalEvents) : 0,

                ActiveMachines = _activeMachines,
                PendingRequests = _pendingRequests,
                QueuedEvents = _queuedEvents,

                UptimeSeconds = _uptime.Elapsed.TotalSeconds
            };
        }

        public List<MetricEvent> GetRecentEvents(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _events.Where(e => e.Timestamp > cutoff).OrderByDescending(e => e.Timestamp).ToList();
        }

        #endregion

        #region Cleanup

        public void Cleanup(TimeSpan retentionPeriod)
        {
            var cutoff = DateTime.UtcNow - retentionPeriod;

            // Clean up old events
            var tempEvents = new List<MetricEvent>();
            while (_events.TryDequeue(out var evt))
            {
                if (evt.Timestamp > cutoff)
                {
                    tempEvents.Add(evt);
                }
            }

            foreach (var evt in tempEvents)
            {
                _events.Enqueue(evt);
            }

            // Clean up histogram data
            foreach (var histogram in _histograms.Values)
            {
                histogram.Cleanup(retentionPeriod);
            }
        }

        #endregion
    }

    #region Supporting Types

    public class MetricCounter
    {
        private long _value = 0;
        public string Description { get; }
        public long Value => _value;

        public MetricCounter(string description)
        {
            Description = description;
        }

        public void Increment() => Interlocked.Increment(ref _value);
        public void Add(long value) => Interlocked.Add(ref _value, value);
    }

    public class MetricGauge
    {
        public string Description { get; }
        private readonly Func<double> _valueProvider;

        public MetricGauge(string description, Func<double> valueProvider)
        {
            Description = description;
            _valueProvider = valueProvider;
        }

        public double GetValue() => _valueProvider();
    }

    public class MetricHistogram
    {
        public string Description { get; }
        private readonly ConcurrentQueue<HistogramSample> _samples = new();

        public MetricHistogram(string description)
        {
            Description = description;
        }

        public void Record(double value)
        {
            _samples.Enqueue(new HistogramSample
            {
                Value = value,
                Timestamp = DateTime.UtcNow
            });
        }

        public List<double> GetRecentSamples(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return _samples.Where(s => s.Timestamp > cutoff).Select(s => s.Value).ToList();
        }

        public HistogramSummary GetSummary()
        {
            var recentSamples = GetRecentSamples(TimeSpan.FromMinutes(5));
            if (!recentSamples.Any())
            {
                return new HistogramSummary();
            }

            recentSamples.Sort();
            return new HistogramSummary
            {
                Count = recentSamples.Count,
                Min = recentSamples.First(),
                Max = recentSamples.Last(),
                Mean = recentSamples.Average(),
                P50 = GetPercentile(recentSamples, 0.5),
                P95 = GetPercentile(recentSamples, 0.95),
                P99 = GetPercentile(recentSamples, 0.99)
            };
        }

        private double GetPercentile(List<double> sortedSamples, double percentile)
        {
            if (!sortedSamples.Any()) return 0;
            var index = (int)Math.Ceiling(sortedSamples.Count * percentile) - 1;
            return sortedSamples[Math.Max(0, Math.Min(index, sortedSamples.Count - 1))];
        }

        public void Cleanup(TimeSpan retentionPeriod)
        {
            var cutoff = DateTime.UtcNow - retentionPeriod;
            var tempSamples = new List<HistogramSample>();

            while (_samples.TryDequeue(out var sample))
            {
                if (sample.Timestamp > cutoff)
                {
                    tempSamples.Add(sample);
                }
            }

            foreach (var sample in tempSamples)
            {
                _samples.Enqueue(sample);
            }
        }
    }

    public class MetricEvent
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = "";
        public string MachineId { get; set; } = "";
        public string MachineType { get; set; } = "";
        public string EventName { get; set; } = "";
        public string Reason { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class HistogramSample
    {
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class HistogramSummary
    {
        public int Count { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
    }

    public class MetricsSnapshot
    {
        public DateTime Timestamp { get; set; }
        public Dictionary<string, long> Counters { get; set; } = new();
        public Dictionary<string, double> Gauges { get; set; } = new();
        public Dictionary<string, HistogramSummary> Histograms { get; set; } = new();

        public long TotalEvents { get; set; }
        public double SuccessRate { get; set; }
        public double FailureRate { get; set; }
        public double ThrottleRate { get; set; }

        public int ActiveMachines { get; set; }
        public int PendingRequests { get; set; }
        public int QueuedEvents { get; set; }

        public double UptimeSeconds { get; set; }
    }

    public class HealthStatus
    {
        public HealthLevel Level { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan Uptime { get; set; }
        public double ThroughputPerSecond { get; set; }
        public double AverageLatencyMs { get; set; }
        public List<string> Issues { get; set; } = new();
        public MetricsSnapshot Metrics { get; set; } = new();
    }

    public enum HealthLevel
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    #endregion
}