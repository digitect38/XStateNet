using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XStateNet;
using XStateNet.Distributed;

namespace DistributedExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel.Debug);
                    });
                    
                    services.AddHostedService<DistributedWorker>();
                })
                .Build();

            await host.RunAsync();
        }
    }

    public class DistributedWorker : BackgroundService
    {
        private readonly ILogger<DistributedWorker> _logger;
        private readonly IConfiguration _configuration;
        private DistributedStateMachine? _machine;

        public DistributedWorker(ILogger<DistributedWorker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var nodeId = _configuration["NODE_ID"] ?? "default-node";
            var nodeAddress = _configuration["NODE_ADDRESS"] ?? "local://default";
            
            _logger.LogInformation("Starting distributed state machine node: {NodeId} at {Address}", nodeId, nodeAddress);

            // Create a simple traffic light state machine
            var trafficLightJson = @"
            {
                ""id"": ""trafficLight"",
                ""initial"": ""green"",
                ""states"": {
                    ""green"": {
                        ""on"": {
                            ""TIMER"": ""yellow"",
                            ""EMERGENCY"": ""red""
                        },
                        ""entry"": [""turnGreenLight""],
                        ""after"": {
                            ""30000"": ""yellow""
                        }
                    },
                    ""yellow"": {
                        ""on"": {
                            ""TIMER"": ""red"",
                            ""EMERGENCY"": ""red""
                        },
                        ""entry"": [""turnYellowLight""],
                        ""after"": {
                            ""5000"": ""red""
                        }
                    },
                    ""red"": {
                        ""on"": {
                            ""TIMER"": ""green"",
                            ""CLEAR"": ""green""
                        },
                        ""entry"": [""turnRedLight""],
                        ""after"": {
                            ""30000"": ""green""
                        }
                    }
                }
            }";

            // Create action callbacks
            var actions = new ActionMap
            {
                ["turnGreenLight"] = () => _logger.LogInformation("🟢 GREEN light is ON"),
                ["turnYellowLight"] = () => _logger.LogInformation("🟡 YELLOW light is ON"),
                ["turnRedLight"] = () => _logger.LogInformation("🔴 RED light is ON")
            };

            // Create base machine
            var baseMachine = StateMachine.CreateFromScript(trafficLightJson, actions);
            
            // Create distributed machine
            _machine = new DistributedStateMachine(baseMachine, nodeId, nodeAddress, _logger);
            _machine.Start();

            // Discover other nodes periodically
            _ = Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var nodes = await _machine.DiscoverMachinesAsync("*", TimeSpan.FromSeconds(5), stoppingToken);
                        foreach (var node in nodes)
                        {
                            _logger.LogDebug("Discovered node: {NodeId} at {Address}", node.Id, node.Address);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during node discovery");
                    }
                    
                    await Task.Delay(30000, stoppingToken); // Discover every 30 seconds
                }
            });

            // Simulate traffic light changes
            var random = new Random();
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(random.Next(10000, 60000), stoppingToken);
                
                // Occasionally send emergency signal
                if (random.Next(10) == 0)
                {
                    _logger.LogWarning("⚠️ EMERGENCY signal received!");
                    _machine.Send("EMERGENCY");
                }
                
                // Example of sending event to another node
                if (random.Next(5) == 0)
                {
                    var targetNode = $"node{random.Next(1, 4)}";
                    if (targetNode != nodeId)
                    {
                        _logger.LogInformation("Sending synchronization signal to {TargetNode}", targetNode);
                        await _machine.SendToMachineAsync(targetNode, "SYNC");
                    }
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping distributed state machine...");
            _machine?.Stop();
            _machine?.Dispose();
            await base.StopAsync(cancellationToken);
        }
    }
}