using System.Collections.Concurrent;

namespace XStateNet.Distributed.Metrics
{
    public interface IPrometheusMetrics
    {
        void RecordHttpRequest(string method, string endpoint, int statusCode, double duration);
        void RecordDatabaseQuery(string operation, string table, double duration, bool success);
        void RecordCacheOperation(string operation, bool hit, double duration);
        void IncrementCounter(string name, double value = 1, params string[] labels);
        void SetGauge(string name, double value, params string[] labels);
        void ObserveHistogram(string name, double value, params string[] labels);
        void ObserveSummary(string name, double value, params string[] labels);
        ConcurrentDictionary<string, double> GetCurrentMetrics();
    }

    public class MetricsOptions
    {
        public bool EnableMetrics { get; set; } = true;
        public int MetricsPort { get; set; } = 9090;
        public string MetricsPath { get; set; } = "/metrics";
    }
}