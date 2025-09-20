using System;
using System.Threading;

namespace XStateNet.Distributed.Resilience
{
    public class TimeoutSettings
    {
        public TimeSpan? Timeout { get; set; }
        public string? OperationName { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }
}