using System;
using System.Threading.Tasks;

namespace SemiStandard.Testing.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "xstate":
                        await XStateNet.Semi.Testing.Console.XStateTestProgram.Main(args);
                        break;
                    case "realistic":
                    case "real":
                        await RealisticScenarioProgram.Run(args);
                        break;
                    default:
                        ShowHelp();
                        break;
                }
            }
            else
            {
                ShowHelp();
            }
        }
        
        static void ShowHelp()
        {
            System.Console.WriteLine("SEMI Standard Testing Console");
            System.Console.WriteLine("Usage: dotnet run -- <scenario>");
            System.Console.WriteLine();
            System.Console.WriteLine("Available scenarios:");
            System.Console.WriteLine("  xstate           - Run XState-based equipment controller test");
            System.Console.WriteLine("  realistic        - Run realistic equipment simulator with full production scenario");
            System.Console.WriteLine("  real             - Shorthand for realistic");
        }
    }
}