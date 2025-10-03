namespace XStateNet.InterProcess.Service;

/// <summary>
/// Background service for InterProcess message bus
/// Runs as Windows Service for production or console app for testing
/// </summary>
public class InterProcessMessageBusWorker : BackgroundService
{
    private readonly IInterProcessMessageBus _messageBus;
    private readonly ILogger<InterProcessMessageBusWorker> _logger;
    private readonly IConfiguration _configuration;

    public InterProcessMessageBusWorker(
        IInterProcessMessageBus messageBus,
        ILogger<InterProcessMessageBusWorker> logger,
        IConfiguration configuration)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InterProcess Message Bus Service starting...");

        try
        {
            // Start the message bus
            await _messageBus.StartAsync(stoppingToken);

            _logger.LogInformation("InterProcess Message Bus Service started successfully");

            // Monitor health status
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                var health = _messageBus.GetHealthStatus();
                _logger.LogInformation(
                    "Health Check - Connections: {Connections}, Machines: {Machines}, Last Activity: {LastActivity}",
                    health.ConnectionCount,
                    health.RegisteredMachines,
                    health.LastActivityAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in InterProcess Message Bus Service");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("InterProcess Message Bus Service stopping...");

        await _messageBus.StopAsync(cancellationToken);

        _logger.LogInformation("InterProcess Message Bus Service stopped");

        await base.StopAsync(cancellationToken);
    }
}
