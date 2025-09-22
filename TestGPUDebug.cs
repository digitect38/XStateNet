using System;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.GPU.Integration;

class TestGPUDebug
{
    static async Task Main()
    {
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

        var bridge = new XStateNetGPUBridge(trafficLightJson);
        await bridge.InitializeAsync(10);

        Console.WriteLine("Initial states:");
        for (int i = 0; i < 3; i++)
        {
            Console.WriteLine($"  Instance {i}: {bridge.GetState(i)}");
        }

        // Send events
        await bridge.BroadcastAsync("TIMER");
        await bridge.ProcessEventsAsync();

        Console.WriteLine("\nAfter TIMER event:");
        for (int i = 0; i < 3; i++)
        {
            Console.WriteLine($"  Instance {i}: {bridge.GetState(i)}");
        }

        // Validate consistency
        Console.WriteLine("\nValidating consistency:");
        bool isConsistent = await bridge.ValidateConsistencyAsync();
        Console.WriteLine($"Consistency check: {(isConsistent ? "PASSED" : "FAILED")}");

        bridge.Dispose();
    }
}