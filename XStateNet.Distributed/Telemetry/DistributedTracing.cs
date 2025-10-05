using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace XStateNet.Distributed.Telemetry
{
    /// <summary>
    /// High-performance distributed tracing with OpenTelemetry
    /// </summary>
    public sealed class DistributedTracing : IDistributedTracing
    {
        private static readonly ActivitySource ActivitySource = new("XStateNet.Distributed", "1.0.0");

        // Pre-allocated tag arrays to avoid allocations
        private readonly KeyValuePair<string, object?>[] _commonTags;
        private readonly ConcurrentDictionary<string, Activity> _activeActivities;
        private readonly TracerProvider? _tracerProvider;

        // Performance counters without allocation
        private long _totalSpans;
        private long _activeSpans;
        private long _errorSpans;

        public DistributedTracing(DistributedTracingOptions options)
        {
            _activeActivities = new ConcurrentDictionary<string, Activity>();
            _commonTags = new[]
            {
                new KeyValuePair<string, object?>("service.name", options.ServiceName),
                new KeyValuePair<string, object?>("service.version", options.ServiceVersion),
                new KeyValuePair<string, object?>("deployment.environment", options.Environment)
            };

            if (options.Enabled)
            {
                _tracerProvider = BuildTracerProvider(options);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Activity? StartStateMachineActivity(string machineId, string eventName, ActivityKind kind = ActivityKind.Internal)
        {
            if (!ActivitySource.HasListeners())
                return null;

            var activity = ActivitySource.StartActivity(
                $"StateMachine.{eventName}",
                kind,
                Activity.Current?.Context ?? default,
                tags: new[]
                {
                    new KeyValuePair<string, object?>("machine.id", machineId),
                    new KeyValuePair<string, object?>("event.name", eventName),
                    new KeyValuePair<string, object?>("span.kind", kind.ToString())
                });

            if (activity != null)
            {
                Interlocked.Increment(ref _totalSpans);
                Interlocked.Increment(ref _activeSpans);

                foreach (var tag in _commonTags)
                {
                    activity.SetTag(tag.Key, tag.Value);
                }

                _activeActivities.TryAdd(activity.Id!, activity);
            }

            return activity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Activity? StartTransitionActivity(string fromState, string toState, string trigger)
        {
            if (!ActivitySource.HasListeners())
                return null;

            var activity = ActivitySource.StartActivity(
                "StateMachine.Transition",
                ActivityKind.Internal,
                Activity.Current?.Context ?? default);

            activity?.SetTag("transition.from", fromState)
                    .SetTag("transition.to", toState)
                    .SetTag("transition.trigger", trigger)
                    .SetTag("transition.timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (activity != null)
            {
                Interlocked.Increment(ref _totalSpans);
                Interlocked.Increment(ref _activeSpans);
            }

            return activity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordException(Activity? activity, Exception exception, bool escapes = true)
        {
            if (activity == null)
                return;

            Interlocked.Increment(ref _errorSpans);

            var tags = new ActivityTagsCollection
            {
                ["exception.type"] = exception.GetType().FullName,
                ["exception.message"] = exception.Message,
                ["exception.escaped"] = escapes
            };

            if (exception.StackTrace != null)
            {
                tags["exception.stacktrace"] = exception.StackTrace;
            }

            activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EndActivity(Activity? activity, ActivityStatusCode status = ActivityStatusCode.Ok, string? description = null)
        {
            if (activity == null)
                return;

            activity.SetStatus(status, description);
            activity.Dispose();

            Interlocked.Decrement(ref _activeSpans);
            _activeActivities.TryRemove(activity.Id!, out _);
        }

        public TracingContext ExtractContext(IDictionary<string, string> headers)
        {
            var traceparent = headers.TryGetValue("traceparent", out var tp) ? tp : null;
            var tracestate = headers.TryGetValue("tracestate", out var ts) ? ts : null;

            if (string.IsNullOrEmpty(traceparent))
                return TracingContext.Empty;

            return new TracingContext
            {
                TraceParent = traceparent!,
                TraceState = tracestate,
                SpanId = ExtractSpanId(traceparent!),
                TraceId = ExtractTraceId(traceparent!)
            };
        }

        public void InjectContext(Activity? activity, IDictionary<string, string> headers)
        {
            if (activity == null)
                return;

            headers["traceparent"] = activity.Id!;

            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                headers["tracestate"] = activity.TraceStateString;
            }

            headers["trace-id"] = activity.TraceId.ToString();
            headers["span-id"] = activity.SpanId.ToString();
        }

        public TracingMetrics GetMetrics()
        {
            return new TracingMetrics
            {
                TotalSpans = _totalSpans,
                ActiveSpans = _activeSpans,
                ErrorSpans = _errorSpans,
                AverageSpanDuration = CalculateAverageSpanDuration()
            };
        }

        private static TracerProvider BuildTracerProvider(DistributedTracingOptions options)
        {
            var builder = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(options.ServiceName, options.ServiceVersion))
                .AddSource("XStateNet.Distributed")
                .SetSampler(GetSampler(options.SamplingStrategy, options.SamplingRate));

            // Add exporters based on configuration
            // Note: Jaeger and Zipkin exporters have been deprecated in OpenTelemetry 1.9.0
            // Use OTLP exporter instead

            if (options.ExportToOtlp && !string.IsNullOrEmpty(options.OtlpEndpoint))
            {
                builder.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(options.OtlpEndpoint);
                    otlp.Protocol = options.OtlpProtocol == OtlpExportProtocol.Grpc
                        ? OpenTelemetry.Exporter.OtlpExportProtocol.Grpc
                        : OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                });
            }

            if (options.ConsoleExporter)
            {
                builder.AddConsoleExporter();
            }

            return builder.Build()!;
        }

        private static Sampler GetSampler(SamplingStrategy strategy, double rate)
        {
            return strategy switch
            {
                SamplingStrategy.AlwaysOn => new AlwaysOnSampler(),
                SamplingStrategy.AlwaysOff => new AlwaysOffSampler(),
                SamplingStrategy.TraceIdRatio => new TraceIdRatioBasedSampler(rate),
                SamplingStrategy.ParentBased => new ParentBasedSampler(new TraceIdRatioBasedSampler(rate)),
                _ => new AlwaysOnSampler()
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ExtractTraceId(string traceparent)
        {
            // Format: 00-[32 hex chars]-[16 hex chars]-[2 hex chars]
            return traceparent.Length >= 35 ? traceparent.Substring(3, 32) : string.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ExtractSpanId(string traceparent)
        {
            // Format: 00-[32 hex chars]-[16 hex chars]-[2 hex chars]
            return traceparent.Length >= 52 ? traceparent.Substring(36, 16) : string.Empty;
        }

        private static string ExtractHost(string endpoint)
        {
            var parts = endpoint.Split(':');
            return parts.Length > 0 ? parts[0] : "localhost";
        }

        private static int ExtractPort(string endpoint)
        {
            var parts = endpoint.Split(':');
            return parts.Length > 1 && int.TryParse(parts[1], out var port) ? port : 6831;
        }

        private double CalculateAverageSpanDuration()
        {
            if (_activeActivities.IsEmpty)
                return 0;

            var totalDuration = 0.0;
            var count = 0;

            foreach (var activity in _activeActivities.Values)
            {
                if (activity.Duration != TimeSpan.Zero)
                {
                    totalDuration += activity.Duration.TotalMilliseconds;
                    count++;
                }
            }

            return count > 0 ? totalDuration / count : 0;
        }

        public void Dispose()
        {
            foreach (var activity in _activeActivities.Values)
            {
                activity.Dispose();
            }
            _activeActivities.Clear();
            _tracerProvider?.Dispose();
        }
    }

    public interface IDistributedTracing : IDisposable
    {
        Activity? StartStateMachineActivity(string machineId, string eventName, ActivityKind kind = ActivityKind.Internal);
        Activity? StartTransitionActivity(string fromState, string toState, string trigger);
        void RecordException(Activity? activity, Exception exception, bool escapes = true);
        void EndActivity(Activity? activity, ActivityStatusCode status = ActivityStatusCode.Ok, string? description = null);
        TracingContext ExtractContext(IDictionary<string, string> headers);
        void InjectContext(Activity? activity, IDictionary<string, string> headers);
        TracingMetrics GetMetrics();
    }

    public class DistributedTracingOptions
    {
        public bool Enabled { get; set; } = true;
        public string ServiceName { get; set; } = "XStateNet";
        public string ServiceVersion { get; set; } = "1.0.0";
        public string Environment { get; set; } = "production";

        // Sampling
        public SamplingStrategy SamplingStrategy { get; set; } = SamplingStrategy.TraceIdRatio;
        public double SamplingRate { get; set; } = 0.1; // 10% sampling by default

        // Exporters
        public bool ExportToJaeger { get; set; }
        public string? JaegerEndpoint { get; set; }

        public bool ExportToZipkin { get; set; }
        public string? ZipkinEndpoint { get; set; }

        public bool ExportToOtlp { get; set; }
        public string? OtlpEndpoint { get; set; }
        public OtlpExportProtocol OtlpProtocol { get; set; } = OtlpExportProtocol.Grpc;

        public bool ConsoleExporter { get; set; }
    }

    public enum SamplingStrategy
    {
        AlwaysOn,
        AlwaysOff,
        TraceIdRatio,
        ParentBased
    }

    public enum OtlpExportProtocol
    {
        Grpc,
        HttpProtobuf
    }

    public class TracingContext
    {
        public string TraceId { get; set; } = string.Empty;
        public string SpanId { get; set; } = string.Empty;
        public string? TraceParent { get; set; }
        public string? TraceState { get; set; }

        public static TracingContext Empty => new();

        public bool IsValid => !string.IsNullOrEmpty(TraceId) && !string.IsNullOrEmpty(SpanId);
    }

    public class TracingMetrics
    {
        public long TotalSpans { get; set; }
        public long ActiveSpans { get; set; }
        public long ErrorSpans { get; set; }
        public double AverageSpanDuration { get; set; }
    }
}