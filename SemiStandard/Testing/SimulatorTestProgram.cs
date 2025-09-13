using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XStateNet.Semi.Transport;
using XStateNet.Semi.Secs;

namespace XStateNet.Semi.Testing
{
    /// <summary>
    /// Test program to demonstrate the equipment simulator
    /// </summary>
    public class SimulatorTestProgram
    {
        public static async Task Main(string[] args)
        {
            System.Console.WriteLine("SEMI Equipment Simulator Test Program");
            System.Console.WriteLine("=====================================\n");

            // Create logger
            using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            var logger = loggerFactory.CreateLogger<SimulatorTestProgram>();

            // Start equipment simulator
            var equipmentEndpoint = new IPEndPoint(IPAddress.Loopback, 5000);
            var simulator = new EquipmentSimulator(equipmentEndpoint, loggerFactory.CreateLogger<EquipmentSimulator>());
            
            // Configure simulator
            simulator.ModelName = "TestEquipment001";
            simulator.SoftwareRevision = "2.0.0";
            simulator.ResponseDelayMs = 100; // Add some delay for realism
            
            // Subscribe to events
            simulator.MessageReceived += (sender, msg) =>
            {
                System.Console.WriteLine($"[SIMULATOR] Received: {msg.SxFy}");
            };
            
            simulator.MessageSent += (sender, msg) =>
            {
                System.Console.WriteLine($"[SIMULATOR] Sent: {msg.SxFy}");
            };

            try
            {
                // Start the simulator in a background task
                System.Console.WriteLine("Starting equipment simulator on port 5000...");
                var simulatorTask = Task.Run(async () => await simulator.StartAsync());
                
                // Give the simulator a moment to start listening
                await Task.Delay(500);
                System.Console.WriteLine("Simulator started successfully!\n");

                // Create host connection
                System.Console.WriteLine("Creating host connection...");
                var hostConnection = new ResilientHsmsConnection(
                    equipmentEndpoint,
                    HsmsConnection.HsmsConnectionMode.Active,
                    loggerFactory.CreateLogger<ResilientHsmsConnection>());

                hostConnection.StateChanged += (sender, state) =>
                {
                    System.Console.WriteLine($"[HOST] Connection state: {state}");
                };

                await hostConnection.ConnectAsync();
                System.Console.WriteLine("Host connected successfully!\n");
                
                // Wait for selection to complete
                await Task.Delay(1000);
                System.Console.WriteLine("Selection completed.\n");

                // Run test scenarios
                await RunTestScenarios(hostConnection, simulator, logger);

                // Keep running for interactive testing
                System.Console.WriteLine("\nSimulator is running. Press any key to stop...");
                System.Console.ReadKey();

                // Cleanup
                await hostConnection.DisconnectAsync();
                hostConnection.Dispose();
                await simulator.StopAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running simulator test");
            }
            finally
            {
                simulator.Dispose();
            }

            System.Console.WriteLine("\nTest program completed.");
        }

        private static async Task RunTestScenarios(
            ResilientHsmsConnection hostConnection,
            EquipmentSimulator simulator,
            ILogger logger)
        {
            System.Console.WriteLine("\n=== Running Test Scenarios ===\n");

            // Test 1: Are You There
            System.Console.WriteLine("Test 1: S1F1 Are You There");
            await Task.Delay(2000); // Give time for selection to fully complete
            await TestAreYouThere(hostConnection, logger);
            await Task.Delay(500);

            // Test 2: Establish Communications
            System.Console.WriteLine("\nTest 2: S1F13 Establish Communications");
            await TestEstablishCommunications(hostConnection, logger);
            await Task.Delay(500);

            // Test 3: Status Variables
            System.Console.WriteLine("\nTest 3: S1F3 Status Variables");
            await TestStatusVariables(hostConnection, logger);
            await Task.Delay(500);

            // Test 4: Equipment Constants
            System.Console.WriteLine("\nTest 4: S2F13 Equipment Constants");
            await TestEquipmentConstants(hostConnection, logger);
            await Task.Delay(500);

            // Test 5: Trigger Alarm
            System.Console.WriteLine("\nTest 5: S5F1 Alarm Report");
            await simulator.TriggerAlarmAsync(1001, "Test Alarm", true);
            await Task.Delay(500);
            await simulator.TriggerAlarmAsync(1001, "Test Alarm", false);
            await Task.Delay(500);

            // Test 6: Trigger Event
            System.Console.WriteLine("\nTest 6: S6F11 Event Report");
            await simulator.TriggerEventAsync(2001, new List<SecsItem>
            {
                new SecsU4(12345),
                new SecsAscii("Test Event Data")
            });
            await Task.Delay(500);

            System.Console.WriteLine("\n=== Test Scenarios Completed ===");
        }

        private static async Task TestAreYouThere(ResilientHsmsConnection connection, ILogger logger)
        {
            try
            {
                var s1f1 = SecsMessageLibrary.S1F1();
                var response = await SendAndReceiveAsync(connection, s1f1);
                
                if (response != null && response.Stream == 1 && response.Function == 2)
                {
                    System.Console.WriteLine("✓ S1F1/F2 successful");
                    if (response.Data is SecsList list && list.Items.Count >= 2)
                    {
                        var mdln = (list.Items[0] as SecsAscii)?.Value;
                        var softrev = (list.Items[1] as SecsAscii)?.Value;
                        System.Console.WriteLine($"  Model: {mdln}, Software: {softrev}");
                    }
                }
                else
                {
                    System.Console.WriteLine("✗ S1F1/F2 failed");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Are You There test");
            }
        }

        private static async Task TestEstablishCommunications(ResilientHsmsConnection connection, ILogger logger)
        {
            try
            {
                var s1f13 = SecsMessageLibrary.S1F13();
                var response = await SendAndReceiveAsync(connection, s1f13);
                
                if (response != null && response.Stream == 1 && response.Function == 14)
                {
                    System.Console.WriteLine("✓ S1F13/F14 successful");
                    if (response.Data is SecsList list && list.Items.Count > 0)
                    {
                        var commack = (list.Items[0] as SecsU1)?.Value;
                        System.Console.WriteLine($"  COMMACK: {commack} ({(commack == 0 ? "Accepted" : "Denied")})");
                    }
                }
                else
                {
                    System.Console.WriteLine("✗ S1F13/F14 failed");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Establish Communications test");
            }
        }

        private static async Task TestStatusVariables(ResilientHsmsConnection connection, ILogger logger)
        {
            try
            {
                var s1f3 = SecsMessageLibrary.S1F3(1, 2, 3, 4, 5, 6);
                var response = await SendAndReceiveAsync(connection, s1f3);
                
                if (response != null && response.Stream == 1 && response.Function == 4)
                {
                    System.Console.WriteLine("✓ S1F3/F4 successful");
                    if (response.Data is SecsList list)
                    {
                        System.Console.WriteLine($"  Received {list.Items.Count} status variables");
                        for (int i = 0; i < list.Items.Count && i < 6; i++)
                        {
                            System.Console.WriteLine($"  SV{i + 1}: {GetItemValue(list.Items[i])}");
                        }
                    }
                }
                else
                {
                    System.Console.WriteLine("✗ S1F3/F4 failed");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Status Variables test");
            }
        }

        private static async Task TestEquipmentConstants(ResilientHsmsConnection connection, ILogger logger)
        {
            try
            {
                var s2f13 = SecsMessageLibrary.S2F13(1, 2, 3);
                var response = await SendAndReceiveAsync(connection, s2f13);
                
                if (response != null && response.Stream == 2 && response.Function == 14)
                {
                    System.Console.WriteLine("✓ S2F13/F14 successful");
                    if (response.Data is SecsList list)
                    {
                        System.Console.WriteLine($"  Received {list.Items.Count} equipment constants");
                        for (int i = 0; i < list.Items.Count && i < 3; i++)
                        {
                            System.Console.WriteLine($"  EC{i + 1}: {GetItemValue(list.Items[i])}");
                        }
                    }
                }
                else
                {
                    System.Console.WriteLine("✗ S2F13/F14 failed");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Equipment Constants test");
            }
        }

        private static async Task<SecsMessage?> SendAndReceiveAsync(
            ResilientHsmsConnection connection,
            SecsMessage message,
            int timeoutMs = 5000)
        {
            var tcs = new TaskCompletionSource<SecsMessage>();
            
            void OnMessageReceived(object? sender, HsmsMessage hsms)
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
            
            connection.MessageReceived += OnMessageReceived;
            
            try
            {
                // Set system bytes - use reasonable range (1 to 65535) for SEMI compatibility
                message.SystemBytes = (uint)Random.Shared.Next(1, 65536);
                
                // Convert and send
                var hsmsMessage = new HsmsMessage
                {
                    Stream = (byte)message.Stream,
                    Function = (byte)message.Function,
                    MessageType = HsmsMessageType.DataMessage,
                    SystemBytes = message.SystemBytes,
                    Data = message.Encode()
                };
                
                System.Console.WriteLine($"  Sending HSMS - Stream: {hsmsMessage.Stream}, Function: {hsmsMessage.Function}, SystemBytes: {hsmsMessage.SystemBytes}");
                
                await connection.SendMessageAsync(hsmsMessage);
                
                using var cts = new CancellationTokenSource(timeoutMs);
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                System.Console.WriteLine("  Timeout waiting for response");
                return null;
            }
            finally
            {
                connection.MessageReceived -= OnMessageReceived;
            }
        }

        private static string GetItemValue(SecsItem item)
        {
            return item switch
            {
                SecsU1 u1 => u1.Value.ToString(),
                SecsU2 u2 => u2.Value.ToString(),
                SecsU4 u4 => u4.Value.ToString(),
                SecsI4 i4 => i4.Value.ToString(),
                SecsAscii ascii => ascii.Value,
                SecsList list => $"List[{list.Items.Count}]",
                _ => item.ToString() ?? "Unknown"
            };
        }
    }
}