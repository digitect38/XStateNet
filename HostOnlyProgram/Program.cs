using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Transport;

namespace XStateNet.Semi.Testing.Console
{
    /// <summary>
    /// Host-only program that connects to equipment simulator
    /// </summary>
    public class HostOnlyProgram
    {
        public static async Task Main(string[] args)
        {
            System.Console.WriteLine("===========================================");
            System.Console.WriteLine("       SEMI HOST (Client) Only");
            System.Console.WriteLine("===========================================");
            System.Console.WriteLine();

            // Setup logging
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<HostOnlyProgram>();

            var cts = new CancellationTokenSource();

            // Create host connection (Active mode - initiates connection to equipment)
            var hostEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5000);
            System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connecting to equipment at {hostEndpoint}...");

            var hostConnection = new ResilientHsmsConnection(
                hostEndpoint,
                HsmsConnection.HsmsConnectionMode.Active  // Active = Client (Host)
            );

            try
            {
                await hostConnection.ConnectAsync(cts.Token);
                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ Connected to equipment!");
                System.Console.WriteLine();

                // Send initial messages
                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S1F13 (Establish Communication)...");
                await SendMessage(hostConnection, SecsMessageLibrary.S1F13());
                await Task.Delay(1000);

                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S1F1 (Are You There)...");
                await SendMessage(hostConnection, SecsMessageLibrary.S1F1());
                await Task.Delay(1000);

                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S1F17 (Request Online)...");
                await SendMessage(hostConnection, new SecsMessage(1, 17, false));
                await Task.Delay(1000);

                // Interactive mode
                System.Console.WriteLine();
                System.Console.WriteLine("═══════════════════════════════════════════════");
                System.Console.WriteLine("Host is running. Commands:");
                System.Console.WriteLine("  1 - Send S1F1 (Are You There)");
                System.Console.WriteLine("  2 - Send S1F13 (Establish Communication)");
                System.Console.WriteLine("  3 - Send S1F17 (Request Online)");
                System.Console.WriteLine("  4 - Send S1F3 (Equipment Status Request)");
                System.Console.WriteLine("  5 - Send S2F41 (Host Command - START)");
                System.Console.WriteLine("  6 - Send S2F41 (Host Command - STOP)");
                System.Console.WriteLine("  Q - Quit");
                System.Console.WriteLine("═══════════════════════════════════════════════");
                System.Console.WriteLine();

                // Handle keyboard input
                Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        var key = System.Console.ReadKey(true);

                        switch (key.KeyChar)
                        {
                            case '1':
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S1F1...");
                                await SendMessage(hostConnection, SecsMessageLibrary.S1F1());
                                break;

                            case '2':
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S1F13...");
                                await SendMessage(hostConnection, SecsMessageLibrary.S1F13());
                                break;

                            case '3':
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S1F17...");
                                await SendMessage(hostConnection, new SecsMessage(1, 17, false));
                                break;

                            case '4':
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S1F3...");
                                await SendMessage(hostConnection, new SecsMessage(1, 3, true));
                                break;

                            case '5':
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S2F41 (START)...");
                                await SendMessage(hostConnection, CreateHostCommand("START"));
                                break;

                            case '6':
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending S2F41 (STOP)...");
                                await SendMessage(hostConnection, CreateHostCommand("STOP"));
                                break;

                            case 'q':
                            case 'Q':
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Shutting down host...");
                                cts.Cancel();
                                break;

                            default:
                                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Unknown command: {key.KeyChar}");
                                break;
                        }
                    }
                });

                // Keep running until cancelled
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Error: {ex.Message}");
                logger.LogError(ex, "Host connection error");
            }
            finally
            {
                await hostConnection.DisconnectAsync();
                System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Disconnected.");
            }
        }

        private static async Task SendMessage(ResilientHsmsConnection connection, SecsMessage message)
        {
            try
            {
                // Convert SecsMessage to HsmsMessage
                var hsmsMessage = new HsmsMessage
                {
                    MessageType = HsmsMessageType.DataMessage,
                    Stream = message.Stream,
                    Function = message.Function,
                    SystemBytes = (uint)Random.Shared.Next(1, 65536)
                };

                await connection.SendMessageAsync(hsmsMessage);
                System.Console.WriteLine($"    → Sent S{message.Stream}F{message.Function}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"    ✗ Failed: {ex.Message}");
            }
        }

        private static SecsMessage CreateHostCommand(string command)
        {
            var msg = new SecsMessage(2, 41, true);
            msg.SystemBytes = (uint)Random.Shared.Next(1, 65536);
            // Note: In real implementation, would add command data
            return msg;
        }
    }
}