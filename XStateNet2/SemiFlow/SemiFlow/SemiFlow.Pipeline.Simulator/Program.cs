using Spectre.Console;
using SemiFlow.Pipeline.Simulator.Pipeline;
using SemiFlow.Pipeline.Simulator.Schedulers;
using System.Text;

namespace SemiFlow.Pipeline.Simulator;

class Program
{
    static async Task Main(string[] args)
    {
        // Enable UTF-8 encoding for proper display
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        DisplayBanner();

        // Get simulation parameters
        var config = GetSimulationConfig(args);

        if (config == null)
            return;

        // Run simulation
        await RunSimulationAsync(config.Value);
    }

    static void DisplayBanner()
    {
        AnsiConsole.Clear();

        var rule = new Rule("[bold cyan]CMP Pipeline Train Simulator[/]")
        {
            Justification = Justify.Center
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var panel = new Panel(
            "[cyan]3-Level Hierarchical Scheduler (Pipelined & Synchronized)[/]\n" +
            "[grey]Coordinator → Wafer Schedulers → Resource Schedulers[/]\n" +
            "[grey]FOUP → Platen1 → Platen2 → FOUP[/]\n" +
            "[yellow]• Pipelined:[/] [grey]Wafers move independently through pipeline[/]\n" +
            "[yellow]• Synchronized:[/] [grey]Wafers move together in synchronized batches[/]")
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1),
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    static (int wafers, int pipelineDepth, bool dashboard, bool synchronized)? GetSimulationConfig(string[] args)
    {
        // Parse command-line args
        if (args.Length >= 2)
        {
            if (int.TryParse(args[0], out int numWafers) && int.TryParse(args[1], out int pipeDepth))
            {
                bool useDashboard = args.Length > 2 && args[2].ToLower() == "dashboard";
                bool useSynchronized = args.Length > 3 && args[3].ToLower() == "sync";
                return (numWafers, pipeDepth, useDashboard, useSynchronized);
            }
        }

        // Show menu
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Select simulation mode:[/]")
                .PageSize(12)
                .AddChoices(new[]
                {
                    "Quick Demo (5 wafers, depth 3, pipelined)",
                    "Quick Demo Synchronized (5 wafers, batch 3, synchronized)",
                    "Standard Run (10 wafers, depth 5, pipelined)",
                    "Standard Run Synchronized (10 wafers, batch 5, synchronized)",
                    "Full Production (25 wafers, depth 5, pipelined)",
                    "Full Production Synchronized (25 wafers, batch 5, synchronized)",
                    "Custom Configuration",
                    "Exit"
                }));

        if (choice.Contains("Exit"))
            return null;

        if (choice.Contains("Quick Demo Synchronized"))
            return (5, 3, true, true);

        if (choice.Contains("Quick Demo"))
            return (5, 3, true, false);

        if (choice.Contains("Standard Run Synchronized"))
            return (10, 5, true, true);

        if (choice.Contains("Standard Run"))
            return (10, 5, true, false);

        if (choice.Contains("Full Production Synchronized"))
            return (25, 5, true, true);

        if (choice.Contains("Full Production"))
            return (25, 5, true, false);

        // Custom
        var wafers = AnsiConsole.Ask<int>("[cyan]Number of wafers:[/]");
        var depth = AnsiConsole.Ask<int>("[cyan]Max pipeline depth/batch size:[/]");
        var dashboard = AnsiConsole.Confirm("[cyan]Show live dashboard?[/]", true);
        var sync = AnsiConsole.Confirm("[cyan]Use synchronized flow?[/]", false);

        return (wafers, depth, dashboard, sync);
    }

    static async Task RunSimulationAsync((int wafers, int pipelineDepth, bool dashboard, bool synchronized) config)
    {
        var mode = config.synchronized ? "synchronized batch" : "pipelined";
        AnsiConsole.MarkupLine($"[yellow]Starting simulation: {config.wafers} wafers, {mode} mode, depth/batch {config.pipelineDepth}[/]");
        AnsiConsole.WriteLine();

        // Initialize state
        var state = new PipelineState
        {
            TotalWafers = config.wafers,
            MaxPipelineDepth = config.pipelineDepth
        };

        // Create schedulers
        var resourceScheduler = new ResourceScheduler(state);

        // Create visualizer
        var visualizer = new PipelineVisualizer(state);

        if (config.synchronized)
        {
            // Use synchronized coordinator
            var syncCoordinator = new SynchronizedCoordinatorScheduler(state, resourceScheduler);

            if (config.dashboard)
            {
                // Run with live dashboard
                await RunWithDashboardSync(syncCoordinator, visualizer, state);
            }
            else
            {
                // Run with log output only
                await syncCoordinator.RunAsync();
            }
        }
        else
        {
            // Use pipelined coordinator
            var coordinator = new CoordinatorScheduler(state, resourceScheduler);

            if (config.dashboard)
            {
                // Run with live dashboard
                await RunWithDashboard(coordinator, visualizer, state);
            }
            else
            {
                // Run with log output only
                await coordinator.RunAsync();
            }
        }

        // Show final summary
        visualizer.ShowFinalSummary();
    }

    static async Task RunWithDashboard(
        CoordinatorScheduler coordinator,
        PipelineVisualizer visualizer,
        PipelineState state)
    {
        var layout = visualizer.CreateDashboard();

        var coordinatorTask = coordinator.RunAsync();

        await AnsiConsole.Live(layout)
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (state.SystemRunning || !state.PipelineEmpty)
                {
                    visualizer.UpdateDashboard(layout);
                    ctx.Refresh();
                    await Task.Delay(500); // Update every 500ms
                }

                // Final update
                visualizer.UpdateDashboard(layout);
                ctx.Refresh();
            });

        await coordinatorTask;
    }

    static async Task RunWithDashboardSync(
        SynchronizedCoordinatorScheduler coordinator,
        PipelineVisualizer visualizer,
        PipelineState state)
    {
        var layout = visualizer.CreateDashboard();

        var coordinatorTask = coordinator.RunAsync();

        await AnsiConsole.Live(layout)
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (state.SystemRunning || !state.PipelineEmpty)
                {
                    visualizer.UpdateDashboard(layout);
                    ctx.Refresh();
                    await Task.Delay(500); // Update every 500ms
                }

                // Final update
                visualizer.UpdateDashboard(layout);
                ctx.Refresh();
            });

        await coordinatorTask;
    }
}
