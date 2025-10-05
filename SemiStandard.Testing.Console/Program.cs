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
                    case "test":
                    case "simple":
                        await SimpleOrchestratorTest.RunAsync();
                        break;
                    case "scheduler":
                    case "sched":
                        await CMPSchedulerDemo.RunAsync();
                        break;
                    case "enhanced":
                    case "enh":
                        await EnhancedCMPDemo.RunAsync();
                        break;
                    case "multistation":
                    case "multi":
                    case "workflow":
                        await MultiStationCMPDemo.Run();
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
            System.Console.WriteLine("  test             - Simple orchestrator test with single machine");
            System.Console.WriteLine("  simple           - Alias for test");
            System.Console.WriteLine("  scheduler        - Run CMP scheduler system (master + tool schedulers)");
            System.Console.WriteLine("  sched            - Alias for scheduler");
            System.Console.WriteLine("  enhanced         - Run Enhanced CMP with E40/E90/E134/E39 integration");
            System.Console.WriteLine("  enh              - Alias for enhanced");
            System.Console.WriteLine("  multistation     - Run Multi-Station CMP workflow (LP→WTR1→POL→WTR2→CLN→WTR1→LP)");
            System.Console.WriteLine("  multi/workflow   - Alias for multistation");
        }
    }
}