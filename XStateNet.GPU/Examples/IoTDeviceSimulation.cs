using System;
using System.Diagnostics;
using System.Threading.Tasks;
using XStateNet.GPU.Core;

namespace XStateNet.GPU.Examples
{
    /// <summary>
    /// Example: Simulate millions of IoT devices using GPU state machines
    /// Each device has states: Disconnected, Connecting, Connected, Transmitting, Error
    /// </summary>
    public class IoTDeviceSimulation
    {
        public static async Task RunSimulationAsync(int deviceCount = 100_000)
        {
            Console.WriteLine("=== XStateNet-GPU IoT Device Simulation ===");
            Console.WriteLine($"Simulating {deviceCount:N0} IoT devices on GPU");
            Console.WriteLine();

            // Define the IoT device state machine
            var definition = CreateIoTDeviceDefinition();

            // Create GPU state machine pool
            using var pool = new GPUStateMachinePool();

            // Initialize pool with devices
            await pool.InitializeAsync(deviceCount, definition);

            // Display GPU info
            Console.WriteLine($"GPU: {pool.AcceleratorName}");
            Console.WriteLine($"Available Memory: {pool.AvailableMemory / (1024 * 1024)} MB");
            Console.WriteLine($"Max Threads per Group: {pool.MaxThreadsPerGroup}");
            Console.WriteLine($"Warp Size: {pool.WarpSize}");
            Console.WriteLine();

            // Simulate device lifecycle
            var sw = Stopwatch.StartNew();

            // Phase 1: Connect all devices
            Console.WriteLine("Phase 1: Connecting devices...");
            for (int i = 0; i < deviceCount; i++)
            {
                pool.SendEvent(i, "CONNECT");
            }
            await pool.ProcessEventsAsync();

            // Phase 2: Devices successfully connected
            Console.WriteLine("Phase 2: Devices connected, starting transmission...");
            for (int i = 0; i < deviceCount; i++)
            {
                pool.SendEvent(i, "CONNECTED");
            }
            await pool.ProcessEventsAsync();

            // Phase 3: Start transmitting data
            for (int i = 0; i < deviceCount; i++)
            {
                pool.SendEvent(i, "START_TRANSMIT");
            }
            await pool.ProcessEventsAsync();

            // Phase 4: Simulate some errors (10% of devices)
            Console.WriteLine("Phase 3: Simulating errors on 10% of devices...");
            var errorCount = deviceCount / 10;
            var random = new Random();
            for (int i = 0; i < errorCount; i++)
            {
                var deviceId = random.Next(deviceCount);
                pool.SendEvent(deviceId, "ERROR");
            }
            await pool.ProcessEventsAsync();

            // Phase 5: Complete transmission for remaining devices
            Console.WriteLine("Phase 4: Completing transmissions...");
            for (int i = 0; i < deviceCount; i++)
            {
                pool.SendEvent(i, "TRANSMIT_COMPLETE");
            }
            await pool.ProcessEventsAsync();

            sw.Stop();

            // Get state distribution
            var distribution = await pool.GetStateDistributionAsync();

            Console.WriteLine();
            Console.WriteLine($"Simulation completed in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Events processed: {deviceCount * 4 + errorCount}");
            Console.WriteLine($"Events per second: {(deviceCount * 4 + errorCount) * 1000.0 / sw.ElapsedMilliseconds:N0}");
            Console.WriteLine();

            Console.WriteLine("Final state distribution:");
            foreach (var kvp in distribution)
            {
                var percentage = (kvp.Value * 100.0 / deviceCount);
                Console.WriteLine($"  {kvp.Key}: {kvp.Value:N0} ({percentage:F1}%)");
            }

            // Performance metrics
            var metrics = pool.GetMetrics();
            Console.WriteLine();
            Console.WriteLine("Performance Metrics:");
            Console.WriteLine($"  Memory Used: {metrics.MemoryUsed / (1024 * 1024):F2} MB");
            Console.WriteLine($"  Max Parallelism: {metrics.MaxParallelism:N0} threads");
            Console.WriteLine($"  Throughput: {(deviceCount * 4 + errorCount) * 1000.0 / sw.ElapsedMilliseconds:N0} events/sec");
        }

        private static GPUStateMachineDefinition CreateIoTDeviceDefinition()
        {
            var definition = new GPUStateMachineDefinition("IoTDevice", 5, 6);

            // Define states
            definition.StateNames[0] = "Disconnected";
            definition.StateNames[1] = "Connecting";
            definition.StateNames[2] = "Connected";
            definition.StateNames[3] = "Transmitting";
            definition.StateNames[4] = "Error";

            // Define events
            definition.EventNames[0] = "CONNECT";
            definition.EventNames[1] = "CONNECTED";
            definition.EventNames[2] = "START_TRANSMIT";
            definition.EventNames[3] = "TRANSMIT_COMPLETE";
            definition.EventNames[4] = "ERROR";
            definition.EventNames[5] = "RESET";

            // Define transition table
            definition.TransitionTable = new[]
            {
                // From Disconnected
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 1 }, // CONNECT -> Connecting

                // From Connecting
                new TransitionEntry { FromState = 1, EventType = 1, ToState = 2 }, // CONNECTED -> Connected
                new TransitionEntry { FromState = 1, EventType = 4, ToState = 4 }, // ERROR -> Error

                // From Connected
                new TransitionEntry { FromState = 2, EventType = 2, ToState = 3 }, // START_TRANSMIT -> Transmitting
                new TransitionEntry { FromState = 2, EventType = 4, ToState = 4 }, // ERROR -> Error

                // From Transmitting
                new TransitionEntry { FromState = 3, EventType = 3, ToState = 2 }, // TRANSMIT_COMPLETE -> Connected
                new TransitionEntry { FromState = 3, EventType = 4, ToState = 4 }, // ERROR -> Error

                // From Error
                new TransitionEntry { FromState = 4, EventType = 5, ToState = 0 }, // RESET -> Disconnected
            };

            return definition;
        }
    }

    /// <summary>
    /// Example: Trading strategy execution on GPU
    /// Each strategy instance runs independently
    /// </summary>
    public class TradingStrategySimulation
    {
        public static async Task RunSimulationAsync(int strategyCount = 10_000)
        {
            Console.WriteLine("=== XStateNet-GPU Trading Strategy Simulation ===");
            Console.WriteLine($"Running {strategyCount:N0} trading strategies on GPU");
            Console.WriteLine();

            var definition = CreateTradingStrategyDefinition();

            using var pool = new GPUStateMachinePool();
            await pool.InitializeAsync(strategyCount, definition);

            var sw = Stopwatch.StartNew();
            var random = new Random();

            // Simulate market events
            for (int tick = 0; tick < 100; tick++)
            {
                // Generate random market events for each strategy
                for (int i = 0; i < strategyCount; i++)
                {
                    var eventType = random.Next(4);
                    string eventName = eventType switch
                    {
                        0 => "SIGNAL_BUY",
                        1 => "SIGNAL_SELL",
                        2 => "PRICE_TARGET_HIT",
                        _ => "STOP_LOSS_HIT"
                    };

                    pool.SendEvent(i, eventName);
                }

                await pool.ProcessEventsAsync();
            }

            sw.Stop();

            var distribution = await pool.GetStateDistributionAsync();

            Console.WriteLine($"Simulation completed in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Market ticks simulated: 100");
            Console.WriteLine($"Total events processed: {strategyCount * 100:N0}");
            Console.WriteLine($"Events per second: {strategyCount * 100 * 1000.0 / sw.ElapsedMilliseconds:N0}");
            Console.WriteLine();

            Console.WriteLine("Final position distribution:");
            foreach (var kvp in distribution)
            {
                var percentage = (kvp.Value * 100.0 / strategyCount);
                Console.WriteLine($"  {kvp.Key}: {kvp.Value:N0} ({percentage:F1}%)");
            }
        }

        private static GPUStateMachineDefinition CreateTradingStrategyDefinition()
        {
            var definition = new GPUStateMachineDefinition("TradingStrategy", 6, 5);

            // Define states
            definition.StateNames[0] = "Idle";
            definition.StateNames[1] = "WaitingForSignal";
            definition.StateNames[2] = "LongPosition";
            definition.StateNames[3] = "ShortPosition";
            definition.StateNames[4] = "ClosingPosition";
            definition.StateNames[5] = "RiskManagement";

            // Define events
            definition.EventNames[0] = "SIGNAL_BUY";
            definition.EventNames[1] = "SIGNAL_SELL";
            definition.EventNames[2] = "PRICE_TARGET_HIT";
            definition.EventNames[3] = "STOP_LOSS_HIT";
            definition.EventNames[4] = "POSITION_CLOSED";

            // Define transition table
            definition.TransitionTable = new[]
            {
                // From Idle/WaitingForSignal
                new TransitionEntry { FromState = 0, EventType = 0, ToState = 2 }, // BUY -> Long
                new TransitionEntry { FromState = 0, EventType = 1, ToState = 3 }, // SELL -> Short
                new TransitionEntry { FromState = 1, EventType = 0, ToState = 2 }, // BUY -> Long
                new TransitionEntry { FromState = 1, EventType = 1, ToState = 3 }, // SELL -> Short

                // From LongPosition
                new TransitionEntry { FromState = 2, EventType = 2, ToState = 4 }, // TARGET -> Closing
                new TransitionEntry { FromState = 2, EventType = 3, ToState = 5 }, // STOP -> Risk

                // From ShortPosition
                new TransitionEntry { FromState = 3, EventType = 2, ToState = 4 }, // TARGET -> Closing
                new TransitionEntry { FromState = 3, EventType = 3, ToState = 5 }, // STOP -> Risk

                // From ClosingPosition
                new TransitionEntry { FromState = 4, EventType = 4, ToState = 0 }, // CLOSED -> Idle

                // From RiskManagement
                new TransitionEntry { FromState = 5, EventType = 4, ToState = 1 }, // CLOSED -> Waiting
            };

            return definition;
        }
    }
}