using System;
using System.Text;
using System.Threading.Tasks;
using OrchestratorTestApp;

namespace OrchestratorTestApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Enable Unicode output for emojis and special characters
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("================================================");
            Console.WriteLine("   XStateNet Orchestrator Test Suite");
            Console.WriteLine("================================================\n");

            bool runAutomated = args.Length > 0 && (args[0] == "--auto" || args[0] == "-a");

            if (runAutomated)
            {
                await TestRunner.RunAllTests();
            }
            else if (args.Length > 0 && args[0] == "--simple")
            {
                await SimpleTest.Run();
            }
            else if (args.Length > 0 && args[0] == "--debug")
            {
                await DebugTest.Run();
            }
            else if (args.Length > 0 && args[0] == "--harsh")
            {
                await HarshTests.RunHarshTests();
            }
            else if (args.Length > 0 && args[0] == "--all")
            {
                await TestRunner.RunAllTests();
                Console.WriteLine("\nPress any key to continue to harsh tests...");
                Console.ReadKey();
                await HarshTests.RunHarshTests();
            }
            else if (args.Length > 0 && args[0] == "--1m")
            {
                await Test1MEvents.Run();
            }
            else
            {
                await InteractiveMenu.Run();
            }
        }
    }
}