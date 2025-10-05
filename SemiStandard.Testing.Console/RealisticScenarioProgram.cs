using Microsoft.Extensions.Logging;
using System.Net;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Testing;
using XStateNet.Semi.Transport;
using HsmsMessage = XStateNet.Semi.Transport.HsmsMessage;
using HsmsMessageType = XStateNet.Semi.Transport.HsmsMessageType;

namespace SemiStandard.Testing.Console
{
    public class RealisticScenarioProgram
    {
        private static RealisticEquipmentSimulator? _simulator;
        private static ResilientHsmsConnection? _hostConnection;
        private static ILogger<RealisticScenarioProgram>? _logger;
        private static CancellationTokenSource _cts = new();

        public static async Task Run(string[] args)
        {
            System.Console.WriteLine("===========================================");
            System.Console.WriteLine("  Photolithography Equipment Simulator");
            System.Console.WriteLine("  Model: ASML-XT1900Gi");
            System.Console.WriteLine("  Realistic Production Scenario");
            System.Console.WriteLine("===========================================\n");

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            _logger = loggerFactory.CreateLogger<RealisticScenarioProgram>();

            var endpoint = new IPEndPoint(IPAddress.Loopback, 5555);

            // Start simulator
            _simulator = new RealisticEquipmentSimulator(endpoint, loggerFactory.CreateLogger<RealisticEquipmentSimulator>());

            _simulator.MessageReceived += (sender, msg) =>
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine($"[SIM RX] {msg.SxFy}");
                System.Console.ResetColor();
            };

            _simulator.MessageSent += (sender, msg) =>
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.WriteLine($"[SIM TX] {msg.SxFy}");
                System.Console.ResetColor();
            };

            _ = Task.Run(() => _simulator.StartAsync(_cts.Token));

            _logger.LogInformation($"Simulator listening on {endpoint}");

            await Task.Delay(1000);

            // Connect host
            _hostConnection = new ResilientHsmsConnection(
                endpoint,
                HsmsConnection.HsmsConnectionMode.Active,
                loggerFactory.CreateLogger<ResilientHsmsConnection>());

            // Set up event handler for unsolicited messages (alarms, events)
            _hostConnection.MessageReceived += OnHostMessageReceived;

            await _hostConnection.ConnectAsync();

            _logger.LogInformation("Host connected to simulator");

            // Wait for selection
            await Task.Delay(1500);

            // Run the realistic scenario
            await RunRealisticScenario();

            System.Console.WriteLine("\nPress any key to exit...");
            System.Console.ReadKey();

            _cts.Cancel();
            await _simulator.StopAsync();
            _simulator?.Dispose();
            await _hostConnection.DisconnectAsync();
            _hostConnection?.Dispose();
        }

        private static async Task RunRealisticScenario()
        {
            try
            {
                System.Console.WriteLine("\n--- Starting Realistic Production Scenario ---\n");

                // Step 1: Establish communication
                await Task.Delay(500);
                System.Console.WriteLine("1. Establishing communication...");
                var response = await SendMessage(SecsMessageLibrary.S1F13());
                LogResponse("S1F13", response);

                // Step 2: Go online
                await Task.Delay(500);
                System.Console.WriteLine("\n2. Going online...");
                response = await SendMessage(new SecsMessage(1, 17, true));
                LogResponse("S1F17", response);

                // Step 3: Initialize equipment
                await Task.Delay(500);
                System.Console.WriteLine("\n3. Initializing equipment...");
                response = await SendHostCommand("INIT");
                LogResponse("INIT", response);

                await Task.Delay(2500);

                // Step 4: Set to REMOTE mode
                System.Console.WriteLine("\n4. Setting equipment to REMOTE mode...");
                response = await SendHostCommand("REMOTE");
                LogResponse("REMOTE", response);

                // Step 5: Load first carrier
                await Task.Delay(500);
                System.Console.WriteLine("\n5. Loading Carrier LOT001 to Load Port 1...");
                response = await SendCarrierAction("LOT001", "LOAD", 1);
                LogResponse("LOAD LOT001", response);

                // Wait for auto-mapping to complete
                await Task.Delay(3500);

                // Step 6: Load recipe
                await Task.Delay(500);
                System.Console.WriteLine("\n6. Loading recipe PROC_193NM_STD...");
                response = await LoadRecipe("PROC_193NM_STD");
                LogResponse("Load Recipe", response);

                // Step 7: Start processing
                await Task.Delay(500);
                System.Console.WriteLine("\n7. Starting processing...");
                response = await SendHostCommand("START");
                LogResponse("START", response);

                System.Console.WriteLine("\n8. Processing wafers (this will take about 30 seconds)...");
                System.Console.WriteLine("   Watch for event reports showing wafer processing progress...\n");

                // Let processing run for a while
                await Task.Delay(10000);

                // Step 9: Demonstrate pause/resume
                System.Console.WriteLine("\n9. Pausing processing...");
                response = await SendHostCommand("PAUSE");
                LogResponse("PAUSE", response);

                await Task.Delay(3000);

                System.Console.WriteLine("\n10. Resuming processing...");
                response = await SendHostCommand("RESUME");
                LogResponse("RESUME", response);

                // Step 11: Load second carrier while first is processing
                await Task.Delay(5000);
                System.Console.WriteLine("\n11. Loading second carrier LOT002 to Load Port 2...");
                response = await SendCarrierAction("LOT002", "LOAD", 2);
                LogResponse("LOAD LOT002", response);

                // Wait for remaining processing
                await Task.Delay(20000);

                // Step 12: Stop processing
                System.Console.WriteLine("\n12. Stopping processing...");
                response = await SendHostCommand("STOP");
                LogResponse("STOP", response);

                await Task.Delay(2000);

                // Step 13: Unload carriers
                System.Console.WriteLine("\n13. Unloading carrier LOT001...");
                response = await SendCarrierAction("LOT001", "UNLOAD", 1);
                LogResponse("UNLOAD LOT001", response);

                await Task.Delay(500);
                System.Console.WriteLine("\n14. Unloading carrier LOT002...");
                response = await SendCarrierAction("LOT002", "UNLOAD", 2);
                LogResponse("UNLOAD LOT002", response);

                // Step 15: Request equipment status
                await Task.Delay(500);
                System.Console.WriteLine("\n15. Requesting equipment status...");
                response = await SendMessage(SecsMessageLibrary.S1F3(1, 2, 3));
                LogResponse("Status Request", response);

                System.Console.WriteLine("\n--- Production Scenario Complete ---");
                System.Console.WriteLine("\nThe simulator has demonstrated:");
                System.Console.WriteLine("- Equipment initialization and state management");
                System.Console.WriteLine("- Carrier loading/unloading with auto-mapping");
                System.Console.WriteLine("- Recipe management");
                System.Console.WriteLine("- Wafer processing with realistic timing");
                System.Console.WriteLine("- Event reporting and alarms");
                System.Console.WriteLine("- Pause/resume functionality");
                System.Console.WriteLine("- Concurrent carrier handling");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during scenario execution");
            }
        }

        private static async Task<SecsMessage?> SendMessage(SecsMessage message)
        {
            if (_hostConnection == null) return null;

            var tcs = new TaskCompletionSource<SecsMessage>();

            void Handler(object? sender, HsmsMessage hsms)
            {
                if (hsms.SystemBytes == message.SystemBytes)
                {
                    var response = SecsMessage.Decode(
                        hsms.Stream,
                        hsms.Function,
                        hsms.Data ?? Array.Empty<byte>(),
                        false);
                    response.SystemBytes = hsms.SystemBytes;
                    tcs.TrySetResult(response);
                }
            }

            _hostConnection.MessageReceived += Handler;

            try
            {
                // Set system bytes
                message.SystemBytes = (uint)Random.Shared.Next(1, 65536);

                // Convert to HSMS and send
                var hsmsMessage = new HsmsMessage
                {
                    Stream = message.Stream,
                    Function = message.Function,
                    MessageType = HsmsMessageType.DataMessage,
                    SystemBytes = message.SystemBytes,
                    Data = message.Encode()
                };

                await _hostConnection.SendMessageAsync(hsmsMessage);

                using var cts = new CancellationTokenSource(5000);
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Message timeout");
                return null;
            }
            finally
            {
                _hostConnection.MessageReceived -= Handler;
            }
        }

        private static async Task<SecsMessage?> SendHostCommand(string command)
        {
            var message = new SecsMessage(2, 41, true)
            {
                Data = new SecsList(
                    new SecsAscii(command),
                    new SecsList() // Empty parameter list
                )
            };

            return await SendMessage(message);
        }

        private static async Task<SecsMessage?> SendCarrierAction(string carrierId, string action, int loadPort)
        {
            var message = new SecsMessage(3, 17, true)
            {
                Data = new SecsList(
                    new SecsAscii(carrierId),
                    new SecsAscii(action),
                    new SecsU4((uint)loadPort)
                )
            };

            return await SendMessage(message);
        }

        private static async Task<SecsMessage?> LoadRecipe(string recipeId)
        {
            var message = new SecsMessage(7, 1, true)
            {
                Data = new SecsList(
                    new SecsAscii(recipeId),
                    new SecsList() // Empty parameter list
                )
            };

            return await SendMessage(message);
        }

        private static void OnHostMessageReceived(object? sender, HsmsMessage hsmsMessage)
        {
            // Decode HSMS to SECS
            var message = SecsMessage.Decode(
                hsmsMessage.Stream,
                hsmsMessage.Function,
                hsmsMessage.Data ?? Array.Empty<byte>(),
                false);
            message.SystemBytes = hsmsMessage.SystemBytes;

            // Handle alarm reports
            if (message.Stream == 5 && message.Function == 1)
            {
                if (message.Data is SecsList list && list.Items.Count >= 3)
                {
                    var alarmSet = (list.Items[0] as SecsU1)?.Value == 1;
                    var alarmId = (list.Items[1] as SecsU4)?.Value ?? 0;
                    var alarmText = (list.Items[2] as SecsAscii)?.Value ?? "";

                    var stateStr = alarmSet ? "SET" : "CLEAR";
                    System.Console.ForegroundColor = alarmSet ? ConsoleColor.Yellow : ConsoleColor.Green;
                    System.Console.WriteLine($"   [ALARM {stateStr}] ID: {alarmId}, Text: {alarmText}");
                    System.Console.ResetColor();
                }
            }
            // Handle event reports
            else if (message.Stream == 6 && message.Function == 11)
            {
                if (message.Data is SecsList list && list.Items.Count >= 2)
                {
                    var eventId = (list.Items[0] as SecsU4)?.Value ?? 0;

                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    System.Console.Write($"   [EVENT {eventId}]");

                    // Print event data pairs
                    for (int i = 1; i < list.Items.Count - 1; i += 2)
                    {
                        var key = (list.Items[i] as SecsAscii)?.Value ?? "";
                        var value = GetItemValue(list.Items[i + 1]);
                        System.Console.Write($" {key}: {value}");
                    }

                    System.Console.WriteLine();
                    System.Console.ResetColor();
                }
            }
        }

        private static string GetItemValue(SecsItem item)
        {
            return item switch
            {
                SecsAscii ascii => ascii.Value,
                SecsU1 u1 => u1.Value.ToString(),
                SecsU2 u2 => u2.Value.ToString(),
                SecsU4 u4 => u4.Value.ToString(),
                SecsI4 i4 => i4.Value.ToString(),
                SecsF4 f4 => f4.Value.ToString("F2"),
                SecsF8 f8 => f8.Value.ToString("F2"),
                SecsBoolean b => b.Value.ToString(),
                _ => item.ToString() ?? ""
            };
        }

        private static void LogResponse(string operation, SecsMessage? response)
        {
            if (response == null)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"   {operation}: No response received");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Green;

                // Check for standard response codes
                if (response.Data is SecsList list && list.Items.Count > 0)
                {
                    if (list.Items[0] is SecsU1 u1)
                    {
                        var code = u1.Value;
                        var status = code == 0 ? "SUCCESS" : $"ERROR (Code: {code})";
                        System.Console.WriteLine($"   {operation}: {status}");
                    }
                    else
                    {
                        System.Console.WriteLine($"   {operation}: Response S{response.Stream}F{response.Function} received");
                    }
                }
                else if (response.Data is SecsU1 singleU1)
                {
                    var code = singleU1.Value;
                    var status = code == 0 ? "SUCCESS" : $"ERROR (Code: {code})";
                    System.Console.WriteLine($"   {operation}: {status}");
                }
                else
                {
                    System.Console.WriteLine($"   {operation}: Response S{response.Stream}F{response.Function} received");
                }
            }
            System.Console.ResetColor();
        }
    }
}