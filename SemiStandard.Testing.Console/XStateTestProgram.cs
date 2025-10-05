using Microsoft.Extensions.Logging;
using System.Net;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Transport;

namespace XStateNet.Semi.Testing.Console
{
    /// <summary>
    /// Test program demonstrating XState-based SEMI equipment controller
    /// </summary>
    public class XStateTestProgram
    {
        public static async Task Main(string[] args)
        {
            System.Console.WriteLine("******************************************************");
            System.Console.WriteLine("*   XStateNet SEMI Equipment Controller Test Program *");
            System.Console.WriteLine("*        Using XState Scripts for SEMI Standards     *");
            System.Console.WriteLine("******************************************************\n");


            // Create logger
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger<XStateTestProgram>();

            // Create equipment controller with XState machines
            var equipmentEndpoint = new IPEndPoint(IPAddress.Loopback, 5000);
            var controller = new XStateEquipmentController(equipmentEndpoint,
                loggerFactory.CreateLogger<XStateEquipmentController>())
            {
                ModelName = "XStateNet-EQ001",
                SoftwareRevision = "2.0.0"
            };

            // Subscribe to state changes
            controller.StateChanged += (sender, state) =>
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.WriteLine($"[STATE CHANGE] {state}");
                System.Console.ResetColor();
            };

            controller.MessageReceived += (sender, msg) =>
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine($"[RECEIVED] {msg.SxFy}");
                System.Console.ResetColor();
            };

            controller.MessageSent += (sender, msg) =>
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine($"[SENT] {msg.SxFy}");
                System.Console.ResetColor();
            };

            try
            {
                // Start equipment controller
                System.Console.WriteLine("\n▶ Starting XState Equipment Controller...");
                var controllerTask = controller.StartAsync();

                // Give the controller a moment to start listening
                //await Task.Delay(1000);
                System.Console.WriteLine("✓ Equipment controller is listening...\n");

                // Display initial states
                DisplayMachineStates(controller);

                // Create host connection to test
                System.Console.WriteLine("\n▶ Creating host connection for testing...");
                var hostConnection = new ResilientHsmsConnection(
                    equipmentEndpoint,
                    HsmsConnection.HsmsConnectionMode.Active,
                    loggerFactory.CreateLogger<ResilientHsmsConnection>());

                hostConnection.MessageReceived += async (sender, hsmsMsg) =>
                {
                    if (hsmsMsg.MessageType == HsmsMessageType.DataMessage)
                    {
                        var secsMsg = DecodeSecsMessage(hsmsMsg);
                        if (secsMsg != null)
                        {
                            System.Console.ForegroundColor = ConsoleColor.Magenta;
                            System.Console.WriteLine($"[HOST RECEIVED] {secsMsg.SxFy}");
                            System.Console.ResetColor();
                        }
                    }
                };

                System.Console.WriteLine("  Attempting to connect to equipment...");
                await hostConnection.ConnectAsync();
                System.Console.WriteLine("✓ Host connected successfully!\n");

                // Wait for controller task to complete (it should complete when connection is accepted)
                await controllerTask;
                System.Console.WriteLine("✓ Equipment controller fully initialized!\n");

                // Run test scenarios
                await RunTestScenarios(hostConnection, controller, logger);

                // Keep running until user presses a key
                System.Console.WriteLine("\n═══════════════════════════════════════════════════════");
                System.Console.WriteLine("Press any key to stop the equipment controller...");
                System.Console.ReadKey(true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in test program");
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"\n✗ Error: {ex.Message}");
                System.Console.ResetColor();
            }
            finally
            {
                controller.Dispose();
            }
        }

        private static async Task RunTestScenarios(
            ResilientHsmsConnection hostConnection,
            XStateEquipmentController controller,
            ILogger logger)
        {
            System.Console.WriteLine("********************************************************\n");
            System.Console.WriteLine("                  TEST SCENARIOS                       ");
            System.Console.WriteLine("********************************************************\n");

            // Test 1: Are You There
            System.Console.WriteLine("▶ Test 1: S1F1 Are You There");
            await SendHostMessage(hostConnection, SecsMessageLibrary.S1F1());
            await Task.Delay(500);
            DisplayMachineStates(controller);

            // Test 2: Request Equipment Status
            System.Console.WriteLine("\n▶ Test 2: S1F3 Request Equipment Status");
            var s1f3 = new SecsMessage(1, 3, false)
            {
                SystemBytes = (uint)Random.Shared.Next(1, 65536),
                Data = new SecsList(new SecsItem[]
                {
                    new SecsU4(1), // Control State
                    new SecsU4(2), // Process State
                    new SecsU4(3)  // Alarm State
                })
            };
            await SendHostMessage(hostConnection, s1f3);
            await Task.Delay(500);

            // Test 3: Establish Communication
            System.Console.WriteLine("\n▶ Test 3: S1F13 Establish Communication");
            await SendHostMessage(hostConnection, SecsMessageLibrary.S1F13());
            await Task.Delay(500);
            DisplayMachineStates(controller);

            // Test 4: Request Online
            System.Console.WriteLine("\n▶ Test 4: S1F17 Request Online");
            await SendHostMessage(hostConnection, new SecsMessage(1, 17, false) { SystemBytes = (uint)Random.Shared.Next(1, 65536) });
            await Task.Delay(500);
            DisplayMachineStates(controller);

            // Test 5: Host Command - START
            System.Console.WriteLine("\n▶ Test 5: S2F41 Host Command - START");
            var startCmd = new SecsMessage(2, 41, false)
            {
                SystemBytes = (uint)Random.Shared.Next(1, 65536),
                Data = new SecsList(new SecsItem[]
                {
                    new SecsAscii("START"),
                    new SecsList() // Empty parameter list
                })
            };
            await SendHostMessage(hostConnection, startCmd);
            await Task.Delay(500);
            DisplayMachineStates(controller);

            // Test 6: Carrier Action (E87)
            System.Console.WriteLine("\n▶ Test 6: S3F17 Carrier Action Request");
            var carrierAction = new SecsMessage(3, 17, false)
            {
                SystemBytes = (uint)Random.Shared.Next(1, 65536),
                Data = new SecsList(new SecsItem[]
                {
                    new SecsAscii("LOAD"),
                    new SecsAscii("CARRIER001"),
                    new SecsU1(1) // Load port 1
                })
            };
            await SendHostMessage(hostConnection, carrierAction);
            await Task.Delay(500);
            DisplayMachineStates(controller);

            // Test 7: Control Job (E94)
            System.Console.WriteLine("\n▶ Test 7: S14F1 Control Job Create");
            var controlJob = new SecsMessage(14, 1, false)
            {
                SystemBytes = (uint)Random.Shared.Next(1, 65536),
                Data = new SecsList(new SecsItem[]
                {
                    new SecsAscii("OBJSPEC"),
                    new SecsAscii("JOB001"),
                    new SecsList(new SecsItem[]
                    {
                        new SecsAscii("CARRIER001")
                    })
                })
            };
            await SendHostMessage(hostConnection, controlJob);
            await Task.Delay(500);
            DisplayMachineStates(controller);

            System.Console.WriteLine("\n✓ All test scenarios completed!");

            // Wait to observe state transitions
            System.Console.WriteLine("\n▶ Observing state machine transitions for 30 seconds...");
            for (int i = 0; i < 6; i++)
            {
                await Task.Delay(5000);
                System.Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Status check {i + 1}/6:");
                DisplayMachineStates(controller);
            }
        }

        private static async Task SendHostMessage(ResilientHsmsConnection connection, SecsMessage message)
        {
            var hsmsMessage = new HsmsMessage
            {
                Stream = (byte)message.Stream,
                Function = (byte)message.Function,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = message.SystemBytes,
                Data = message.Encode()
            };

            await connection.SendMessageAsync(hsmsMessage);
            System.Console.ForegroundColor = ConsoleColor.Blue;
            System.Console.WriteLine($"   → Sent {message.SxFy} from host");
            System.Console.ResetColor();
        }

        private static SecsMessage? DecodeSecsMessage(HsmsMessage hsmsMessage)
        {
            try
            {
                var message = new SecsMessage(hsmsMessage.Stream, hsmsMessage.Function, false)
                {
                    SystemBytes = hsmsMessage.SystemBytes
                };

                if (hsmsMessage.Data != null && hsmsMessage.Data.Length > 0)
                {
                    using var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(hsmsMessage.Data));
                    message.Data = SecsItem.Decode(reader);
                }

                return message;
            }
            catch
            {
                return null;
            }
        }

        private static void DisplayMachineStates(XStateEquipmentController controller)
        {
            System.Console.WriteLine("\n");
            System.Console.WriteLine("┌──────────────────────────────────────────────────────────────────────┐");
            System.Console.WriteLine("│                      CURRENT STATE MACHINES                          │");
            System.Console.WriteLine("├──────────────────────────────────────────────────────────────────────┤");

            var states = controller.GetAllMachineStates();
            foreach (var (machine, state) in states)
            {
                System.Console.WriteLine($"│ {machine,-20} : {state,-45} │");
            }

            System.Console.WriteLine("└──────────────────────────────────────────────────────────────────────┘");
        }
    }
}