using System;
using System.Threading.Tasks;
using XStateNet.GPU.Examples;

namespace XStateNet.GPU.Demo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== XStateNet-GPU Demo ===");
            Console.WriteLine("This demo shows massively parallel state machine execution on GPU");
            Console.WriteLine();

            try
            {
                // Run IoT Device Simulation
                Console.WriteLine("1. IoT Device Simulation");
                Console.WriteLine("-------------------------");
                await IoTDeviceSimulation.RunSimulationAsync(1000); // Start with 1000 devices

                Console.WriteLine("\nContinuing to trading simulation...");
                await Task.Delay(1000); // Brief pause instead of ReadKey
                Console.WriteLine();

                // Run Trading Strategy Simulation
                Console.WriteLine("2. Trading Strategy Simulation");
                Console.WriteLine("-------------------------------");
                await TradingStrategySimulation.RunSimulationAsync(5000); // 5000 strategies

                Console.WriteLine("\nDemo completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                Console.WriteLine($"Details: {ex.StackTrace}");
            }
        }
    }
}