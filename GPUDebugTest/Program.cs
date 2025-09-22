using System;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.GPU.Integration;
using XStateNet.GPU.Core;

class TestGPUDebug
{
    static async Task Main()
    {
        Console.WriteLine("=== GPU State Machine Debug Test ===");
        Console.WriteLine();

        string trafficLightJson = @"{
            ""id"": ""TrafficLight"",
            ""initial"": ""red"",
            ""states"": {
                ""red"": {
                    ""on"": {
                        ""TIMER"": ""yellow""
                    }
                },
                ""yellow"": {
                    ""on"": {
                        ""TIMER"": ""green""
                    }
                },
                ""green"": {
                    ""on"": {
                        ""TIMER"": ""red""
                    }
                }
            }
        }";

        try
        {
            // Test CPU state machine first
            Console.WriteLine("Testing CPU state machine:");
            var cpuMachine = StateMachine.CreateFromScript(trafficLightJson);
            cpuMachine.Start();

            Console.WriteLine($"  Initial state: {GetSimpleStateName(cpuMachine.GetActiveStateString())}");

            cpuMachine.Send("TIMER");
            Console.WriteLine($"  After TIMER: {GetSimpleStateName(cpuMachine.GetActiveStateString())}");

            cpuMachine.Send("TIMER");
            Console.WriteLine($"  After 2nd TIMER: {GetSimpleStateName(cpuMachine.GetActiveStateString())}");

            cpuMachine.Send("TIMER");
            Console.WriteLine($"  After 3rd TIMER: {GetSimpleStateName(cpuMachine.GetActiveStateString())}");

            cpuMachine.Dispose();
            Console.WriteLine();

            // Test GPU bridge
            Console.WriteLine("Testing GPU bridge:");
            var bridge = new XStateNetGPUBridge(trafficLightJson);
            await bridge.InitializeAsync(10);

            Console.WriteLine("Initial states:");
            for (int i = 0; i < 3; i++)
            {
                string state = bridge.GetState(i);
                Console.WriteLine($"  Instance {i}: '{state}'");
            }

            // Send individual event to instance 0
            Console.WriteLine("\nSending TIMER to instance 0:");
            bridge.Send(0, "TIMER");
            await bridge.ProcessEventsAsync();
            Console.WriteLine($"  Instance 0: '{bridge.GetState(0)}'");

            // Send another event to instance 0
            Console.WriteLine("\nSending 2nd TIMER to instance 0:");
            bridge.Send(0, "TIMER");
            await bridge.ProcessEventsAsync();
            Console.WriteLine($"  Instance 0: '{bridge.GetState(0)}'");

            // Broadcast to all
            Console.WriteLine("\nBroadcasting TIMER to all instances:");
            await bridge.BroadcastAsync("TIMER");
            await bridge.ProcessEventsAsync();

            Console.WriteLine("States after broadcast:");
            for (int i = 0; i < 3; i++)
            {
                Console.WriteLine($"  Instance {i}: '{bridge.GetState(i)}'");
            }

            // Validate consistency
            Console.WriteLine("\nValidating consistency with CPU implementation:");
            bool isConsistent = await bridge.ValidateConsistencyAsync();
            Console.WriteLine($"Consistency check: {(isConsistent ? "PASSED" : "FAILED")}");

            // Get state distribution
            Console.WriteLine("\nState distribution:");
            var distribution = await bridge.GetStateDistributionAsync();
            foreach (var kvp in distribution)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value} instances");
            }

            bridge.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
        }

        Console.WriteLine("\n=== Test Complete ===");
    }

    static string GetSimpleStateName(string fullStateName)
    {
        if (string.IsNullOrEmpty(fullStateName)) return "";

        // Remove the # prefix if present
        if (fullStateName.StartsWith("#"))
        {
            fullStateName = fullStateName.Substring(1);
        }

        // Extract state name after last dot
        int lastDot = fullStateName.LastIndexOf('.');
        return lastDot >= 0 ? fullStateName.Substring(lastDot + 1) : fullStateName;
    }
}