namespace XStateNet.InterProcess.Service;

/// <summary>
/// Health monitoring service for InterProcess message bus
/// Tracks metrics, performance, and generates alerts
/// </summary>
public class HealthMonitor : BackgroundService
{
    private readonly IInterProcessMessageBus _messageBus;
    private readonly ILogger<HealthMonitor> _logger;
    private readonly IConfiguration _configuration;
    private readonly PerformanceMetrics _metrics = new();

    public HealthMonitor(
        IInterProcessMessageBus messageBus,
        ILogger<HealthMonitor> logger,
        IConfiguration configuration)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _configuration.GetValue<int>("MessageBus:HealthCheckIntervalSeconds", 30);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation("Health Monitor started. Check interval: {Interval} seconds", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);

                var health = _messageBus.GetHealthStatus();
                UpdateMetrics(health);

                LogHealthStatus(health);
                CheckAlerts(health);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in health monitor");
            }
        }
    }

    private void UpdateMetrics(HealthStatus health)
    {
        _metrics.LastCheckAt = DateTime.UtcNow;
        _metrics.ConnectionCount = health.ConnectionCount;
        _metrics.RegisteredMachines = health.RegisteredMachines;
        _metrics.LastActivityAt = health.LastActivityAt;

        // Track peak connections
        if (health.ConnectionCount > _metrics.PeakConnections)
        {
            _metrics.PeakConnections = health.ConnectionCount;
            _metrics.PeakConnectionsAt = DateTime.UtcNow;
        }

        // Calculate uptime
        if (_metrics.StartedAt == default)
        {
            _metrics.StartedAt = DateTime.UtcNow;
        }
        _metrics.Uptime = DateTime.UtcNow - _metrics.StartedAt;
    }

    private void LogHealthStatus(HealthStatus health)
    {
        if (health.IsHealthy)
        {
            _logger.LogInformation(
                "✓ Health: OK | Connections: {Connections} | Machines: {Machines} | Last Activity: {LastActivity:yyyy-MM-dd HH:mm:ss}",
                health.ConnectionCount,
                health.RegisteredMachines,
                health.LastActivityAt);
        }
        else
        {
            _logger.LogWarning(
                "⚠ Health: DEGRADED | Error: {Error}",
                health.ErrorMessage ?? "Unknown");
        }
    }

    private void CheckAlerts(HealthStatus health)
    {
        // Alert: No activity for extended period
        var inactivityThreshold = TimeSpan.FromMinutes(5);
        var timeSinceActivity = DateTime.UtcNow - health.LastActivityAt;

        if (timeSinceActivity > inactivityThreshold)
        {
            _logger.LogWarning(
                "⚠ ALERT: No activity for {Duration} minutes",
                (int)timeSinceActivity.TotalMinutes);
        }

        // Alert: Too many connections (potential resource exhaustion)
        var maxConnections = _configuration.GetValue<int>("MessageBus:MaxConnections", 100);
        if (health.ConnectionCount > maxConnections * 0.8)
        {
            _logger.LogWarning(
                "⚠ ALERT: High connection count: {Count}/{Max} ({Percent:F0}%)",
                health.ConnectionCount,
                maxConnections,
                (double)health.ConnectionCount / maxConnections * 100);
        }
    }

    public PerformanceMetrics GetMetrics() => _metrics;
}

public class PerformanceMetrics
{
    public DateTime StartedAt { get; set; }
    public DateTime LastCheckAt { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ConnectionCount { get; set; }
    public int RegisteredMachines { get; set; }
    public DateTime LastActivityAt { get; set; }
    public int PeakConnections { get; set; }
    public DateTime PeakConnectionsAt { get; set; }
}
