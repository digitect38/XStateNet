using System;
using System.Threading.Tasks;
using XStateNet.GPU.Examples;

namespace XStateNet.GPU
{
    public class TestGPUImplementation
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Testing XStateNet-GPU Implementation ===\n");

            try
            {
                // Test 1: Small scale IoT simulation
                Console.WriteLine("Test 1: Small-scale IoT Device Simulation (1,000 devices)");
                Console.WriteLine("------------------------------------------------");
                await IoTDeviceSimulation.RunSimulationAsync(1_000);
                Console.WriteLine("\n");

                // Test 2: Medium scale IoT simulation
                Console.WriteLine("Test 2: Medium-scale IoT Device Simulation (10,000 devices)");
                Console.WriteLine("------------------------------------------------");
                await IoTDeviceSimulation.RunSimulationAsync(10_000);
                Console.WriteLine("\n");

                // Test 3: Trading strategy simulation
                Console.WriteLine("Test 3: Trading Strategy Simulation (5,000 strategies)");
                Console.WriteLine("------------------------------------------------");
                await TradingStrategySimulation.RunSimulationAsync(5_000);
                Console.WriteLine("\n");

                // Test 4: Large scale if GPU memory allows
                Console.WriteLine("Test 4: Large-scale IoT Device Simulation (100,000 devices)");
                Console.WriteLine("------------------------------------------------");
                await IoTDeviceSimulation.RunSimulationAsync(100_000);

                Console.WriteLine("\n=== All GPU Tests Completed Successfully ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}