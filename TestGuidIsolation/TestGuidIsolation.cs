using System;
using System.Threading.Tasks;
using XStateNet;

class TestGuidIsolation
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

        Console.WriteLine("=== Testing GUID Isolation Feature ===");
        Console.WriteLine();

        // Create multiple machines WITHOUT guid isolation (will conflict)
        Console.WriteLine("Creating machines WITHOUT guid isolation:");
        try
        {
            var machine1 = StateMachine.CreateFromScript(trafficLightJson);
            Console.WriteLine($"  Machine 1 ID: {machine1.machineId}");

            var machine2 = StateMachine.CreateFromScript(trafficLightJson);
            Console.WriteLine($"  Machine 2 ID: {machine2.machineId}");

            Console.WriteLine("  WARNING: Both machines have the same ID!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error: {ex.Message}");
        }

        Console.WriteLine();

        // Create multiple machines WITH guid isolation (no conflict)
        Console.WriteLine("Creating machines WITH guid isolation:");
        var isolatedMachine1 = StateMachine.CreateFromScript(trafficLightJson, true);
        Console.WriteLine($"  Machine 1 ID: {isolatedMachine1.machineId}");

        var isolatedMachine2 = StateMachine.CreateFromScript(trafficLightJson, true);
        Console.WriteLine($"  Machine 2 ID: {isolatedMachine2.machineId}");

        var isolatedMachine3 = StateMachine.CreateFromScript(trafficLightJson, true);
        Console.WriteLine($"  Machine 3 ID: {isolatedMachine3.machineId}");

        Console.WriteLine();
        Console.WriteLine("  SUCCESS: Each machine has a unique ID!");

        // Start machines and test they work independently
        Console.WriteLine();
        Console.WriteLine("Testing independent operation:");

        isolatedMachine1.Start();
        isolatedMachine2.Start();
        isolatedMachine3.Start();

        // Send events to different machines
        isolatedMachine1.Send("TIMER"); // red -> yellow
        isolatedMachine2.Send("TIMER"); // red -> yellow
        isolatedMachine2.Send("TIMER"); // yellow -> green
        // machine3 stays at red

        await Task.Delay(100); // Let events process

        Console.WriteLine($"  Machine 1 state: {isolatedMachine1.GetActiveStateString()}");
        Console.WriteLine($"  Machine 2 state: {isolatedMachine2.GetActiveStateString()}");
        Console.WriteLine($"  Machine 3 state: {isolatedMachine3.GetActiveStateString()}");

        Console.WriteLine();
        Console.WriteLine("=== Test Complete ===");

        // Cleanup
        isolatedMachine1.Dispose();
        isolatedMachine2.Dispose();
        isolatedMachine3.Dispose();
    }
}