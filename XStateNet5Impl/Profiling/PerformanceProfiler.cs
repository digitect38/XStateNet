using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace XStateNet.Profiling
{
    /// <summary>
    /// Advanced performance profiling tools for state machines and orchestrators
    /// </summary>
    public class PerformanceProfiler : IDisposable
    {
        private readonly ConcurrentQueue<ProfileSample> _samples = new();
        private readonly ConcurrentDictionary<string, MethodProfile> _methodProfiles = new();
        private readonly ConcurrentDictionary<string, MachineProfile> _machineProfiles = new();
        private readonly Timer _flushTimer;
        private readonly ProfilingConfiguration _config;
        private volatile bool _isEnabled = true;
        private volatile bool _disposed = false;

        public event EventHandler<ProfilingSampleEventArgs>? SampleCollected;
        public event EventHandler<PerformanceAlertEventArgs>? PerformanceAlert;

        public PerformanceProfiler(ProfilingConfiguration? config = null)
        {
            _config = config ?? new ProfilingConfiguration();
            _flushTimer = new Timer(FlushSamples, null, _config.FlushInterval, _config.FlushInterval);
        }

        /// <summary>
        /// Enable or disable profiling
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
        }

        /// <summary>
        /// Start profiling a method execution
        /// </summary>
        public IDisposable ProfileMethod(string methodName, string? context = null)
        {
            if (!_isEnabled) return new DisposableProfiler(() => { });

            var stopwatch = Stopwatch.StartNew();
            var startTime = DateTime.UtcNow;
            var threadId = Thread.CurrentThread.ManagedThreadId;

            return new DisposableProfiler(() =>
            {
                stopwatch.Stop();
                var duration = stopwatch.Elapsed;

                RecordMethodExecution(methodName, context, duration, startTime, threadId);
            });
        }

        /// <summary>
        /// Profile an async operation
        /// </summary>
        public async Task<T> ProfileAsync<T>(string operationName, Func<Task<T>> operation, string? context = null)
        {
            if (!_isEnabled)
                return await operation();

            using (ProfileMethod(operationName, context))
            {
                return await operation();
            }
        }

        /// <summary>
        /// Profile a synchronous operation
        /// </summary>
        public T Profile<T>(string operationName, Func<T> operation, string? context = null)
        {
            if (!_isEnabled)
                return operation();

            using (ProfileMethod(operationName, context))
            {
                return operation();
            }
        }

        /// <summary>
        /// Record a custom metric
        /// </summary>
        public void RecordMetric(string metricName, double value, string? unit = null, Dictionary<string, string>? tags = null)
        {
            if (!_isEnabled) return;

            var sample = new ProfileSample
            {
                Timestamp = DateTime.UtcNow,
                Type = SampleType.Metric,
                MethodName = metricName,
                Duration = TimeSpan.Zero,
                Value = value,
                Unit = unit,
                Tags = tags ?? new Dictionary<string, string>(),
                ThreadId = Thread.CurrentThread.ManagedThreadId,
                MemoryBefore = GC.GetTotalMemory(false),
                MemoryAfter = GC.GetTotalMemory(false)
            };

            _samples.Enqueue(sample);
            OnSampleCollected(sample);
        }

        /// <summary>
        /// Start profiling a state machine
        /// </summary>
        public void StartMachineProfiling(string machineId)
        {
            if (!_isEnabled) return;

            _machineProfiles.AddOrUpdate(machineId,
                new MachineProfile
                {
                    MachineId = machineId,
                    StartTime = DateTime.UtcNow,
                    EventCounts = new ConcurrentDictionary<string, long>(),
                    StateDurations = new ConcurrentDictionary<string, List<TimeSpan>>(),
                    TransitionTimes = new ConcurrentQueue<TimeSpan>(),
                    LastActivity = DateTime.UtcNow
                },
                (key, existing) =>
                {
                    existing.LastActivity = DateTime.UtcNow;
                    return existing;
                });
        }

        /// <summary>
        /// Record state machine event
        /// </summary>
        public void RecordMachineEvent(string machineId, string eventName, string currentState, TimeSpan processingTime)
        {
            if (!_isEnabled) return;

            if (_machineProfiles.TryGetValue(machineId, out var profile))
            {
                profile.EventCounts.AddOrUpdate(eventName, 1, (key, count) => count + 1);
                profile.TotalEvents++;
                profile.LastActivity = DateTime.UtcNow;

                // Record state duration
                if (!profile.StateDurations.ContainsKey(currentState))
                {
                    profile.StateDurations[currentState] = new List<TimeSpan>();
                }
                profile.StateDurations[currentState].Add(processingTime);

                // Check for performance alerts
                CheckPerformanceAlerts(machineId, eventName, processingTime);
            }
        }

        /// <summary>
        /// Record state transition
        /// </summary>
        public void RecordStateTransition(string machineId, string fromState, string toState, TimeSpan transitionTime)
        {
            if (!_isEnabled) return;

            if (_machineProfiles.TryGetValue(machineId, out var profile))
            {
                profile.TransitionTimes.Enqueue(transitionTime);
                profile.TotalTransitions++;
                profile.LastActivity = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Get current profiling statistics
        /// </summary>
        public ProfilingStatistics GetStatistics()
        {
            var statistics = new ProfilingStatistics
            {
                GeneratedAt = DateTime.UtcNow,
                TotalSamples = _samples.Count,
                MethodProfiles = _methodProfiles.Values.ToList(),
                MachineProfiles = _machineProfiles.Values.ToList(),
                ConfigurationUsed = _config
            };

            // Calculate summary statistics
            var recentSamples = _samples.ToArray().TakeLast(1000).ToArray();
            if (recentSamples.Any())
            {
                statistics.AverageExecutionTime = recentSamples.Average(s => s.Duration.TotalMilliseconds);
                statistics.MaxExecutionTime = recentSamples.Max(s => s.Duration.TotalMilliseconds);
                statistics.MinExecutionTime = recentSamples.Min(s => s.Duration.TotalMilliseconds);

                var sortedDurations = recentSamples.Select(s => s.Duration.TotalMilliseconds).OrderBy(d => d).ToArray();
                statistics.P50ExecutionTime = GetPercentile(sortedDurations, 0.5);
                statistics.P95ExecutionTime = GetPercentile(sortedDurations, 0.95);
                statistics.P99ExecutionTime = GetPercentile(sortedDurations, 0.99);
            }

            return statistics;
        }

        /// <summary>
        /// Generate comprehensive profiling report
        /// </summary>
        public ProfilingReport GenerateReport()
        {
            var statistics = GetStatistics();
            var hotspots = IdentifyPerformanceHotspots();
            var recommendations = GenerateOptimizationRecommendations(statistics, hotspots);

            return new ProfilingReport
            {
                GeneratedAt = DateTime.UtcNow,
                ProfilingDuration = DateTime.UtcNow - _config.StartTime,
                Statistics = statistics,
                PerformanceHotspots = hotspots,
                OptimizationRecommendations = recommendations,
                MethodProfilesAnalysis = AnalyzeMethodProfiles(),
                MachineProfilesAnalysis = AnalyzeMachineProfiles()
            };
        }

        /// <summary>
        /// Export profiling data to various formats
        /// </summary>
        public async Task ExportDataAsync(string outputDirectory, ExportFormat format = ExportFormat.All)
        {
            Directory.CreateDirectory(outputDirectory);

            var report = GenerateReport();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (format.HasFlag(ExportFormat.Json))
            {
                var jsonPath = Path.Combine(outputDirectory, $"profiling_report_{timestamp}.json");
                var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var json = JsonSerializer.Serialize(report, options);
                await File.WriteAllTextAsync(jsonPath, json);
            }

            if (format.HasFlag(ExportFormat.Csv))
            {
                await ExportToCsv(outputDirectory, timestamp, report);
            }

            if (format.HasFlag(ExportFormat.Html))
            {
                await ExportToHtml(outputDirectory, timestamp, report);
            }

            if (format.HasFlag(ExportFormat.FlameGraph))
            {
                await ExportFlameGraph(outputDirectory, timestamp);
            }
        }

        /// <summary>
        /// Start real-time profiling dashboard
        /// </summary>
        public async Task StartRealtimeDashboard(CancellationToken cancellationToken = default)
        {
            Console.WriteLine("🔥 XStateNet Real-time Performance Profiler");
            Console.WriteLine("==========================================");
            Console.WriteLine("Press 'q' to quit, 'r' to reset stats, 's' to save report");
            Console.WriteLine();

            var lastSampleCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).KeyChar;
                    switch (key)
                    {
                        case 'q':
                            return;
                        case 'r':
                            ResetStatistics();
                            Console.WriteLine("\n✅ Statistics reset");
                            break;
                        case 's':
                            var report = GenerateReport();
                            var fileName = $"profile_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                            await File.WriteAllTextAsync(fileName, json);
                            Console.WriteLine($"\n💾 Report saved to {fileName}");
                            break;
                    }
                }

                var currentSampleCount = _samples.Count;
                if (currentSampleCount != lastSampleCount || currentSampleCount % 100 == 0)
                {
                    DisplayRealtimeStats();
                    lastSampleCount = currentSampleCount;
                }

                await Task.Delay(1000, cancellationToken);
            }
        }

        // Private methods
        private void RecordMethodExecution(string methodName, string? context, TimeSpan duration, DateTime startTime, int threadId)
        {
            var sample = new ProfileSample
            {
                Timestamp = startTime,
                Type = SampleType.MethodExecution,
                MethodName = methodName,
                Context = context,
                Duration = duration,
                ThreadId = threadId,
                MemoryBefore = 0, // Would need to be captured at start
                MemoryAfter = GC.GetTotalMemory(false)
            };

            _samples.Enqueue(sample);

            // Update method profile
            _methodProfiles.AddOrUpdate(methodName,
                new MethodProfile
                {
                    MethodName = methodName,
                    CallCount = 1,
                    TotalTime = duration,
                    MinTime = duration,
                    MaxTime = duration,
                    LastCalled = startTime
                },
                (key, existing) =>
                {
                    existing.CallCount++;
                    existing.TotalTime += duration;
                    existing.MinTime = TimeSpan.FromTicks(Math.Min(existing.MinTime.Ticks, duration.Ticks));
                    existing.MaxTime = TimeSpan.FromTicks(Math.Max(existing.MaxTime.Ticks, duration.Ticks));
                    existing.LastCalled = startTime;
                    return existing;
                });

            OnSampleCollected(sample);

            // Check for slow method execution
            if (duration.TotalMilliseconds > _config.SlowMethodThresholdMs)
            {
                OnPerformanceAlert(new PerformanceAlert
                {
                    AlertType = AlertType.SlowMethod,
                    MethodName = methodName,
                    Value = duration.TotalMilliseconds,
                    Threshold = _config.SlowMethodThresholdMs,
                    Message = $"Slow method execution: {methodName} took {duration.TotalMilliseconds:F2}ms"
                });
            }
        }

        private void CheckPerformanceAlerts(string machineId, string eventName, TimeSpan processingTime)
        {
            if (processingTime.TotalMilliseconds > _config.SlowEventThresholdMs)
            {
                OnPerformanceAlert(new PerformanceAlert
                {
                    AlertType = AlertType.SlowEvent,
                    MachineId = machineId,
                    EventName = eventName,
                    Value = processingTime.TotalMilliseconds,
                    Threshold = _config.SlowEventThresholdMs,
                    Message = $"Slow event processing: {eventName} in {machineId} took {processingTime.TotalMilliseconds:F2}ms"
                });
            }
        }

        private void FlushSamples(object? state)
        {
            if (_disposed) return;

            // Keep only recent samples to prevent memory growth
            var currentCount = _samples.Count;
            if (currentCount > _config.MaxSamples)
            {
                var samplesToRemove = currentCount - _config.MaxSamples / 2;
                for (int i = 0; i < samplesToRemove; i++)
                {
                    _samples.TryDequeue(out _);
                }
            }
        }

        private void DisplayRealtimeStats()
        {
            Console.Clear();
            Console.WriteLine("🔥 XStateNet Real-time Performance Profiler");
            Console.WriteLine("==========================================");

            var stats = GetStatistics();

            Console.WriteLine($"📊 Total Samples: {stats.TotalSamples:N0}");
            Console.WriteLine($"⏱️  Avg Execution: {stats.AverageExecutionTime:F2}ms");
            Console.WriteLine($"🚀 P95 Execution: {stats.P95ExecutionTime:F2}ms");
            Console.WriteLine($"⚡ P99 Execution: {stats.P99ExecutionTime:F2}ms");
            Console.WriteLine();

            // Top methods by total time
            Console.WriteLine("🔥 Top Methods by Total Time:");
            var topMethods = stats.MethodProfiles
                .OrderByDescending(m => m.TotalTime.TotalMilliseconds)
                .Take(5);

            foreach (var method in topMethods)
            {
                Console.WriteLine($"   {method.MethodName}: {method.TotalTime.TotalMilliseconds:F0}ms total, {method.CallCount} calls");
            }

            Console.WriteLine();

            // Active machines
            Console.WriteLine("🎯 Active Machines:");
            var activeMachines = stats.MachineProfiles
                .Where(m => m.LastActivity > DateTime.UtcNow.AddMinutes(-1))
                .Take(5);

            foreach (var machine in activeMachines)
            {
                Console.WriteLine($"   {machine.MachineId}: {machine.TotalEvents} events, {machine.TotalTransitions} transitions");
            }

            Console.WriteLine();
            Console.WriteLine("Press 'q' to quit, 'r' to reset stats, 's' to save report");
        }

        private List<PerformanceHotspot> IdentifyPerformanceHotspots()
        {
            var hotspots = new List<PerformanceHotspot>();

            // Method hotspots
            foreach (var method in _methodProfiles.Values)
            {
                var avgTime = method.TotalTime.TotalMilliseconds / method.CallCount;

                if (avgTime > _config.SlowMethodThresholdMs || method.TotalTime.TotalMilliseconds > 1000)
                {
                    hotspots.Add(new PerformanceHotspot
                    {
                        Type = HotspotType.SlowMethod,
                        Name = method.MethodName,
                        TotalTime = method.TotalTime.TotalMilliseconds,
                        AverageTime = avgTime,
                        CallCount = method.CallCount,
                        Impact = CalculateImpact(method.TotalTime.TotalMilliseconds, method.CallCount)
                    });
                }
            }

            // Machine hotspots
            foreach (var machine in _machineProfiles.Values)
            {
                if (machine.TotalEvents > 0)
                {
                    var avgTransitionTime = machine.TransitionTimes.Any()
                        ? machine.TransitionTimes.Average(t => t.TotalMilliseconds)
                        : 0;

                    if (avgTransitionTime > _config.SlowEventThresholdMs)
                    {
                        hotspots.Add(new PerformanceHotspot
                        {
                            Type = HotspotType.SlowMachine,
                            Name = machine.MachineId,
                            AverageTime = avgTransitionTime,
                            CallCount = machine.TotalEvents,
                            Impact = CalculateImpact(avgTransitionTime * machine.TotalEvents, machine.TotalEvents)
                        });
                    }
                }
            }

            return hotspots.OrderByDescending(h => h.Impact).ToList();
        }

        private List<OptimizationRecommendation> GenerateOptimizationRecommendations(ProfilingStatistics stats, List<PerformanceHotspot> hotspots)
        {
            var recommendations = new List<OptimizationRecommendation>();

            foreach (var hotspot in hotspots.Take(5))
            {
                switch (hotspot.Type)
                {
                    case HotspotType.SlowMethod:
                        recommendations.Add(new OptimizationRecommendation
                        {
                            Priority = hotspot.Impact > 1000 ? RecommendationPriority.High : RecommendationPriority.Medium,
                            Category = "Method Optimization",
                            Title = $"Optimize slow method: {hotspot.Name}",
                            Description = $"Method {hotspot.Name} has high execution time ({hotspot.AverageTime:F2}ms avg) and is called frequently ({hotspot.CallCount} times).",
                            Suggestions = new List<string>
                            {
                                "Profile the method internally to identify bottlenecks",
                                "Consider caching expensive computations",
                                "Use async/await for I/O operations",
                                "Optimize database queries or API calls",
                                "Consider parallel processing for CPU-intensive operations"
                            }
                        });
                        break;

                    case HotspotType.SlowMachine:
                        recommendations.Add(new OptimizationRecommendation
                        {
                            Priority = RecommendationPriority.Medium,
                            Category = "State Machine Optimization",
                            Title = $"Optimize state machine: {hotspot.Name}",
                            Description = $"State machine {hotspot.Name} has slow transition times ({hotspot.AverageTime:F2}ms avg).",
                            Suggestions = new List<string>
                            {
                                "Review action implementations for performance",
                                "Minimize I/O operations in state actions",
                                "Consider batching state transitions",
                                "Optimize guard conditions",
                                "Use object pooling for frequently created objects"
                            }
                        });
                        break;
                }
            }

            return recommendations;
        }

        private MethodProfileAnalysis AnalyzeMethodProfiles()
        {
            var methods = _methodProfiles.Values.ToList();

            return new MethodProfileAnalysis
            {
                TotalMethods = methods.Count,
                MostCalledMethod = methods.OrderByDescending(m => m.CallCount).FirstOrDefault(),
                SlowestMethod = methods.OrderByDescending(m => m.MaxTime).FirstOrDefault(),
                HighestTotalTimeMethod = methods.OrderByDescending(m => m.TotalTime).FirstOrDefault(),
                AverageCallsPerMethod = methods.Any() ? methods.Average(m => m.CallCount) : 0,
                AverageExecutionTime = methods.Any() ? methods.Average(m => m.TotalTime.TotalMilliseconds / m.CallCount) : 0
            };
        }

        private MachineProfileAnalysis AnalyzeMachineProfiles()
        {
            var machines = _machineProfiles.Values.ToList();

            return new MachineProfileAnalysis
            {
                TotalMachines = machines.Count,
                MostActiveMachine = machines.OrderByDescending(m => m.TotalEvents).FirstOrDefault(),
                AverageEventsPerMachine = machines.Any() ? machines.Average(m => m.TotalEvents) : 0,
                AverageTransitionsPerMachine = machines.Any() ? machines.Average(m => m.TotalTransitions) : 0,
                TotalEvents = machines.Sum(m => m.TotalEvents),
                TotalTransitions = machines.Sum(m => m.TotalTransitions)
            };
        }

        private async Task ExportToCsv(string outputDirectory, string timestamp, ProfilingReport report)
        {
            var csvPath = Path.Combine(outputDirectory, $"profiling_methods_{timestamp}.csv");
            var csv = new StringBuilder();
            csv.AppendLine("MethodName,CallCount,TotalTime(ms),AverageTime(ms),MinTime(ms),MaxTime(ms),LastCalled");

            foreach (var method in report.Statistics.MethodProfiles)
            {
                var avgTime = method.TotalTime.TotalMilliseconds / method.CallCount;
                csv.AppendLine($"{method.MethodName},{method.CallCount},{method.TotalTime.TotalMilliseconds:F2},{avgTime:F2},{method.MinTime.TotalMilliseconds:F2},{method.MaxTime.TotalMilliseconds:F2},{method.LastCalled:yyyy-MM-dd HH:mm:ss}");
            }

            await File.WriteAllTextAsync(csvPath, csv.ToString());
        }

        private async Task ExportToHtml(string outputDirectory, string timestamp, ProfilingReport report)
        {
            var htmlPath = Path.Combine(outputDirectory, $"profiling_report_{timestamp}.html");
            var html = GenerateHtmlReport(report);
            await File.WriteAllTextAsync(htmlPath, html);
        }

        private async Task ExportFlameGraph(string outputDirectory, string timestamp)
        {
            // Generate flame graph data format
            var flameGraphPath = Path.Combine(outputDirectory, $"flamegraph_data_{timestamp}.txt");
            var flameData = new StringBuilder();

            foreach (var method in _methodProfiles.Values)
            {
                var avgTime = method.TotalTime.TotalMilliseconds / method.CallCount;
                flameData.AppendLine($"{method.MethodName} {avgTime:F0}");
            }

            await File.WriteAllTextAsync(flameGraphPath, flameData.ToString());
        }

        private string GenerateHtmlReport(ProfilingReport report)
        {
            var html = new StringBuilder();
            html.Append($@"<!DOCTYPE html>
<html>
<head>
    <title>XStateNet Profiling Report</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 40px; }}
        .header {{ text-align: center; border-bottom: 2px solid #ccc; padding-bottom: 20px; }}
        .section {{ margin: 20px 0; }}
        .metric {{ background: #f5f5f5; padding: 10px; margin: 5px 0; border-left: 4px solid #007acc; }}
        table {{ width: 100%; border-collapse: collapse; }}
        th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
        th {{ background-color: #f2f2f2; }}
        .hotspot-high {{ background-color: #ffebee; }}
        .hotspot-medium {{ background-color: #fff3e0; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>🔥 XStateNet Profiling Report</h1>
        <p>Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC</p>
        <p>Duration: {report.ProfilingDuration.TotalMinutes:F1} minutes</p>
    </div>

    <div class='section'>
        <h2>📊 Performance Statistics</h2>
        <div class='metric'>Total Samples: {report.Statistics.TotalSamples:N0}</div>
        <div class='metric'>Average Execution Time: {report.Statistics.AverageExecutionTime:F2}ms</div>
        <div class='metric'>P95 Execution Time: {report.Statistics.P95ExecutionTime:F2}ms</div>
        <div class='metric'>P99 Execution Time: {report.Statistics.P99ExecutionTime:F2}ms</div>
    </div>

    <div class='section'>
        <h2>🔥 Performance Hotspots</h2>
        <table>
            <tr><th>Name</th><th>Type</th><th>Total Time</th><th>Average Time</th><th>Call Count</th><th>Impact</th></tr>");

            foreach (var hotspot in report.PerformanceHotspots.Take(10))
            {
                var rowClass = hotspot.Impact > 1000 ? "hotspot-high" : "hotspot-medium";
                html.Append($@"
            <tr class='{rowClass}'>
                <td>{hotspot.Name}</td>
                <td>{hotspot.Type}</td>
                <td>{hotspot.TotalTime:F2}ms</td>
                <td>{hotspot.AverageTime:F2}ms</td>
                <td>{hotspot.CallCount}</td>
                <td>{hotspot.Impact:F0}</td>
            </tr>");
            }

            html.Append(@"
        </table>
    </div>

    <div class='section'>
        <h2>💡 Optimization Recommendations</h2>");

            foreach (var recommendation in report.OptimizationRecommendations)
            {
                html.Append($@"
        <div class='metric'>
            <h3>{recommendation.Title}</h3>
            <p><strong>Priority:</strong> {recommendation.Priority}</p>
            <p><strong>Category:</strong> {recommendation.Category}</p>
            <p>{recommendation.Description}</p>
            <ul>");

                foreach (var suggestion in recommendation.Suggestions)
                {
                    html.Append($"<li>{suggestion}</li>");
                }

                html.Append("</ul></div>");
            }

            html.Append(@"
    </div>
</body>
</html>");

            return html.ToString();
        }

        private void OnSampleCollected(ProfileSample sample)
        {
            SampleCollected?.Invoke(this, new ProfilingSampleEventArgs { Sample = sample });
        }

        private void OnPerformanceAlert(PerformanceAlert alert)
        {
            PerformanceAlert?.Invoke(this, new PerformanceAlertEventArgs { Alert = alert });
        }

        private void ResetStatistics()
        {
            while (_samples.TryDequeue(out _)) { }
            _methodProfiles.Clear();
            _machineProfiles.Clear();
        }

        private static double GetPercentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 0) return 0;

            double n = sortedValues.Length;
            double index = percentile * (n - 1);
            int lowerIndex = (int)Math.Floor(index);
            int upperIndex = (int)Math.Ceiling(index);

            if (lowerIndex == upperIndex)
                return sortedValues[lowerIndex];

            double weight = index - lowerIndex;
            return sortedValues[lowerIndex] * (1 - weight) + sortedValues[upperIndex] * weight;
        }

        private static double CalculateImpact(double totalTime, long callCount)
        {
            // Simple impact calculation: total time * frequency factor
            return totalTime * Math.Log10(Math.Max(1, callCount));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _flushTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    // Helper class for method profiling
    internal class DisposableProfiler : IDisposable
    {
        private readonly Action _onDispose;

        public DisposableProfiler(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            _onDispose?.Invoke();
        }
    }

    // Configuration and data models
    public class ProfilingConfiguration
    {
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(10);
        public int MaxSamples { get; set; } = 100000;
        public double SlowMethodThresholdMs { get; set; } = 100.0;
        public double SlowEventThresholdMs { get; set; } = 50.0;
        public bool EnableMemoryTracking { get; set; } = true;
        public bool EnableThreadTracking { get; set; } = true;
    }

    public class ProfileSample
    {
        public DateTime Timestamp { get; set; }
        public SampleType Type { get; set; }
        public string MethodName { get; set; } = "";
        public string? Context { get; set; }
        public TimeSpan Duration { get; set; }
        public double Value { get; set; }
        public string? Unit { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new();
        public int ThreadId { get; set; }
        public long MemoryBefore { get; set; }
        public long MemoryAfter { get; set; }
    }

    public class MethodProfile
    {
        public string MethodName { get; set; } = "";
        public long CallCount { get; set; }
        public TimeSpan TotalTime { get; set; }
        public TimeSpan MinTime { get; set; }
        public TimeSpan MaxTime { get; set; }
        public DateTime LastCalled { get; set; }
    }

    public class MachineProfile
    {
        public string MachineId { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime LastActivity { get; set; }
        public long TotalEvents { get; set; }
        public long TotalTransitions { get; set; }
        public ConcurrentDictionary<string, long> EventCounts { get; set; } = new();
        public ConcurrentDictionary<string, List<TimeSpan>> StateDurations { get; set; } = new();
        public ConcurrentQueue<TimeSpan> TransitionTimes { get; set; } = new();
    }

    public class ProfilingStatistics
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalSamples { get; set; }
        public double AverageExecutionTime { get; set; }
        public double MaxExecutionTime { get; set; }
        public double MinExecutionTime { get; set; }
        public double P50ExecutionTime { get; set; }
        public double P95ExecutionTime { get; set; }
        public double P99ExecutionTime { get; set; }
        public List<MethodProfile> MethodProfiles { get; set; } = new();
        public List<MachineProfile> MachineProfiles { get; set; } = new();
        public ProfilingConfiguration ConfigurationUsed { get; set; } = new();
    }

    public class ProfilingReport
    {
        public DateTime GeneratedAt { get; set; }
        public TimeSpan ProfilingDuration { get; set; }
        public ProfilingStatistics Statistics { get; set; } = new();
        public List<PerformanceHotspot> PerformanceHotspots { get; set; } = new();
        public List<OptimizationRecommendation> OptimizationRecommendations { get; set; } = new();
        public MethodProfileAnalysis MethodProfilesAnalysis { get; set; } = new();
        public MachineProfileAnalysis MachineProfilesAnalysis { get; set; } = new();
    }

    public class PerformanceHotspot
    {
        public HotspotType Type { get; set; }
        public string Name { get; set; } = "";
        public double TotalTime { get; set; }
        public double AverageTime { get; set; }
        public long CallCount { get; set; }
        public double Impact { get; set; }
    }

    public class OptimizationRecommendation
    {
        public RecommendationPriority Priority { get; set; }
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Suggestions { get; set; } = new();
    }

    public class MethodProfileAnalysis
    {
        public int TotalMethods { get; set; }
        public MethodProfile? MostCalledMethod { get; set; }
        public MethodProfile? SlowestMethod { get; set; }
        public MethodProfile? HighestTotalTimeMethod { get; set; }
        public double AverageCallsPerMethod { get; set; }
        public double AverageExecutionTime { get; set; }
    }

    public class MachineProfileAnalysis
    {
        public int TotalMachines { get; set; }
        public MachineProfile? MostActiveMachine { get; set; }
        public double AverageEventsPerMachine { get; set; }
        public double AverageTransitionsPerMachine { get; set; }
        public long TotalEvents { get; set; }
        public long TotalTransitions { get; set; }
    }

    public class PerformanceAlert
    {
        public AlertType AlertType { get; set; }
        public string? MachineId { get; set; }
        public string? MethodName { get; set; }
        public string? EventName { get; set; }
        public double Value { get; set; }
        public double Threshold { get; set; }
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // Event args
    public class ProfilingSampleEventArgs : EventArgs
    {
        public ProfileSample Sample { get; set; } = new();
    }

    public class PerformanceAlertEventArgs : EventArgs
    {
        public PerformanceAlert Alert { get; set; } = new();
    }

    // Enums
    public enum SampleType
    {
        MethodExecution,
        StateTransition,
        EventProcessing,
        Metric
    }

    public enum HotspotType
    {
        SlowMethod,
        SlowMachine,
        HighMemoryUsage,
        FrequentGC
    }

    public enum AlertType
    {
        SlowMethod,
        SlowEvent,
        HighMemoryUsage,
        HighCpuUsage
    }

    public enum RecommendationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    [Flags]
    public enum ExportFormat
    {
        Json = 1,
        Csv = 2,
        Html = 4,
        FlameGraph = 8,
        All = Json | Csv | Html | FlameGraph
    }
}