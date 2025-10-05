namespace XStateNet.Distributed.Resilience
{
    /// <summary>
    /// XStateNet-specific retry policy implementation
    /// Extends the base RetryPolicy with additional XStateNet features
    /// </summary>
    public class XStateNetRetryPolicy : RetryPolicy
    {
        public XStateNetRetryPolicy(string name, RetryOptions options, IRetryMetrics? metrics = null)
            : base(name, options, metrics)
        {
        }
    }
}