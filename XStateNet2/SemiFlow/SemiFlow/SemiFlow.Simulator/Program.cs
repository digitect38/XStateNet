using Spectre.Console;
using SemiFlow.Converter;
using SemiFlow.Simulator.Simulation;
using SemiFlow.Simulator.Actions;
using SemiFlow.Simulator.Guards;
using System.Text.Json;
using System.Text;

namespace SemiFlow.Simulator;

class Program
{
    static void Main(string[] args)
    {
        // Enable UTF-8 encoding for emoji support
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        // Display banner
        DisplayBanner();

        // Get workflow file
        string workflowPath = args.Length > 0
            ? args[0]
            : "../../../../../cmp_1f1r2p_semiflow.json";

        if (!File.Exists(workflowPath))
        {
            AnsiConsole.MarkupLine("[red]Error: Workflow file not found![/]");
            AnsiConsole.MarkupLine($"[grey]Looked for: {Path.GetFullPath(workflowPath)}[/]");
            return;
        }

        // Get simulation type from args or menu
        SimulationType simulationType;
        if (args.Length > 1)
        {
            // Command-line argument: demo, full, or dashboard
            simulationType = args[1].ToLower() switch
            {
                "demo" => SimulationType.Demo,
                "dashboard" => SimulationType.Dashboard,
                "full" => SimulationType.Full,
                _ => SimulationType.Demo
            };
        }
        else
        {
            // Show menu
            var menuResult = ShowMenu();
            if (menuResult == null) return;
            simulationType = menuResult.Value;
        }

        // Run simulation
        RunSimulation(workflowPath, simulationType);
    }

    static void DisplayBanner()
    {
        AnsiConsole.Clear();

        var rule = new Rule("[bold cyan]🏭 CMP Scheduler Simulator[/]")
        {
            Justification = Justify.Center
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var panel = new Panel(
            "[cyan]SemiFlow-based CMP System Simulator[/]\n" +
            "[grey]1 FOUP • 1 Robot • 2 Platens[/]\n" +
            "[grey]Supports 1-Step and 2-Step Processing[/]")
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1),
            Padding = new Padding(2, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    static SimulationType? ShowMenu()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Select simulation type:[/]")
                .PageSize(10)
                .AddChoices(new[]
                {
                    "🚀 Run Full Simulation (25 wafers)",
                    "🧪 Run Demo (5 wafers - Fast)",
                    "📊 Run with Live Dashboard",
                    "❌ Exit"
                }));

        if (choice.Contains("Exit"))
            return null;

        if (choice.Contains("Demo"))
            return SimulationType.Demo;

        if (choice.Contains("Dashboard"))
            return SimulationType.Dashboard;

        return SimulationType.Full;
    }

    static void RunSimulation(string workflowPath, SimulationType type)
    {
        // Initialize simulation state
        var state = new SimulationState();

        if (type == SimulationType.Demo)
        {
            state.TotalWafers = 5;
        }

        // Create actions and guards
        var actions = new WorkflowActions(state);
        var guards = new WorkflowGuards(state);

        // Load and convert workflow
        AnsiConsole.Status()
            .Start("[yellow]Loading workflow...[/]", ctx =>
            {
                var json = File.ReadAllText(workflowPath);

                ctx.Status("[yellow]Converting SemiFlow to XState...[/]");
                var converter = new SemiFlowToXStateConverter();
                var xstateMachine = converter.Convert(json);

                ctx.Status("[yellow]Workflow loaded successfully[/]");
                Thread.Sleep(500);
            });

        AnsiConsole.MarkupLine("[green]✓ Workflow loaded successfully[/]");
        AnsiConsole.WriteLine();

        // Run simulation based on type
        if (type == SimulationType.Dashboard)
        {
            RunWithDashboard(state, actions, guards);
        }
        else
        {
            RunStandardSimulation(state, actions, guards);
        }

        // Show final summary
        ShowFinalSummary(state);
    }

    static void RunStandardSimulation(SimulationState state, WorkflowActions actions, WorkflowGuards guards)
    {
        AnsiConsole.MarkupLine("[bold cyan]Starting Simulation...[/]");
        AnsiConsole.WriteLine();

        // Initialize
        actions.InitializeSystem();
        actions.StartLotProcessing();
        AnsiConsole.WriteLine();

        // Process wafers
        int wafersToProcess = state.TotalWafers;

        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .Start(ctx =>
            {
                var task = ctx.AddTask($"[cyan]Processing {wafersToProcess} wafers[/]", maxValue: wafersToProcess);

                while (guards.HasMoreWafers())
                {
                    // Get next wafer
                    actions.GetNextWaferFromFoup();
                    actions.DetermineProcessType();

                    if (state.CurrentWafer != null)
                    {
                        task.Description = $"[cyan]Wafer {state.CurrentWafer.Id}/{wafersToProcess} ({state.CurrentWafer.ProcessType})[/]";

                        if (guards.IsTwoStepProcess())
                        {
                            // 2-Step Process
                            ProcessTwoStep(state, actions);
                        }
                        else
                        {
                            // 1-Step Process
                            ProcessOneStep(state, actions);
                        }

                        actions.MarkWaferComplete();
                        actions.IncrementProcessedCount();

                        task.Increment(1);
                    }
                }

                task.StopTask();
            });

        AnsiConsole.WriteLine();

        // Finalize
        actions.FinalizeLotProcessing();
        actions.CleanupSystem();
    }

    static void RunWithDashboard(SimulationState state, WorkflowActions actions, WorkflowGuards guards)
    {
        // Initialize
        actions.InitializeSystem();
        actions.StartLotProcessing();

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Body").SplitColumns(
                    new Layout("Left"),
                    new Layout("Right")),
                new Layout("Footer").Size(8));

        AnsiConsole.Live(layout)
            .Start(ctx =>
            {
                while (guards.HasMoreWafers())
                {
                    // Get next wafer
                    actions.GetNextWaferFromFoup();
                    actions.DetermineProcessType();

                    if (state.CurrentWafer != null)
                    {
                        if (guards.IsTwoStepProcess())
                        {
                            ProcessTwoStep(state, actions);
                        }
                        else
                        {
                            ProcessOneStep(state, actions);
                        }

                        actions.MarkWaferComplete();
                        actions.IncrementProcessedCount();
                    }

                    // Update dashboard
                    UpdateDashboard(layout, state);
                    ctx.Refresh();
                    Thread.Sleep(100);
                }
            });

        // Finalize
        actions.FinalizeLotProcessing();
        actions.CleanupSystem();
    }

    static void ProcessOneStep(SimulationState state, WorkflowActions actions)
    {
        // Load from FOUP
        actions.PickWaferFromFoup();

        // Process on platen
        actions.SelectAvailablePlaten();
        actions.MoveRobotToPlaten();
        actions.PlaceWaferOnPlaten();
        actions.RobotReturnHome();

        // Process
        actions.ProcessWaferOnPlaten();

        // Return to FOUP
        actions.PickWaferFromPlaten();
        actions.MoveRobotToFoup();
        actions.PlaceWaferInFoup();
        actions.RobotReturnHome();
    }

    static void ProcessTwoStep(SimulationState state, WorkflowActions actions)
    {
        // Load from FOUP
        actions.PickWaferFromFoup();

        // Step 1: First platen
        actions.SelectAvailablePlaten();
        actions.MoveRobotToPlaten();
        actions.PlaceWaferOnPlaten();
        actions.RobotReturnHome();
        actions.ProcessWaferOnPlaten();

        // Transfer to second platen
        actions.PickWaferFromPlaten();
        actions.SelectOtherPlaten();
        actions.MoveRobotToPlaten();
        actions.PlaceWaferOnPlaten();
        actions.RobotReturnHome();

        // Step 2: Second platen
        actions.ProcessWaferOnPlaten();

        // Return to FOUP
        actions.PickWaferFromPlaten();
        actions.MoveRobotToFoup();
        actions.PlaceWaferInFoup();
        actions.RobotReturnHome();
    }

    static void UpdateDashboard(Layout layout, SimulationState state)
    {
        // Header
        layout["Header"].Update(
            new Panel(
                Align.Center(
                    new Markup($"[bold cyan]CMP Scheduler - Lot: {state.CurrentLot}[/] | " +
                              $"[green]Processed: {state.ProcessedWafers}/{state.TotalWafers}[/] | " +
                              $"[yellow]Elapsed: {state.ElapsedTime:hh\\:mm\\:ss}[/]")))
            {
                Border = BoxBorder.Double
            });

        // Left: Station Status
        var stationTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Station[/]")
            .AddColumn("[cyan]State[/]")
            .AddColumn("[cyan]Wafer[/]")
            .AddColumn("[cyan]Util %[/]");

        foreach (var station in state.Stations.Values)
        {
            var stateColor = station.State switch
            {
                Models.StationState.Idle => "grey",
                Models.StationState.Busy => "yellow",
                Models.StationState.Processing => "green",
                _ => "white"
            };

            var waferInfo = station.CurrentWafer?.Id.ToString() ?? "-";
            var util = state.Metrics.ContainsKey($"{station.Id.ToLower()}_utilization")
                ? $"{state.Metrics[$"{station.Id.ToLower()}_utilization"]:F1}%"
                : "-";

            stationTable.AddRow(
                station.Id,
                $"[{stateColor}]{station.State}[/]",
                waferInfo,
                util);
        }

        layout["Left"].Update(
            new Panel(stationTable)
            {
                Header = new PanelHeader("[yellow]Station Status[/]")
            });

        // Right: Metrics
        var metricsTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Metric[/]")
            .AddColumn("[cyan]Value[/]");

        metricsTable.AddRow("Throughput", $"{state.ThroughputWafersPerHour:F2} wafers/hr");
        metricsTable.AddRow("Avg Cycle Time", $"{state.Metrics["avg_cycle_time"]:F1}s");
        metricsTable.AddRow("1-Step Wafers", state.OneStepWafers.ToString());
        metricsTable.AddRow("2-Step Wafers", state.TwoStepWafers.ToString());
        metricsTable.AddRow("Errors", state.ErrorCount.ToString());

        layout["Right"].Update(
            new Panel(metricsTable)
            {
                Header = new PanelHeader("[yellow]Metrics[/]")
            });

        // Footer: Recent wafers
        var recentTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]ID[/]")
            .AddColumn("[cyan]Type[/]")
            .AddColumn("[cyan]Status[/]")
            .AddColumn("[cyan]Cycle Time[/]");

        foreach (var wafer in state.CompletedWafers.TakeLast(5))
        {
            var cycleTime = wafer.EndProcessTime.HasValue && wafer.StartProcessTime.HasValue
                ? (wafer.EndProcessTime.Value - wafer.StartProcessTime.Value).TotalSeconds
                : 0;

            recentTable.AddRow(
                wafer.Id.ToString(),
                wafer.ProcessType.ToString(),
                "[green]Complete[/]",
                $"{cycleTime:F1}s");
        }

        layout["Footer"].Update(
            new Panel(recentTable)
            {
                Header = new PanelHeader("[yellow]Recent Completions[/]")
            });
    }

    static void ShowFinalSummary(SimulationState state)
    {
        AnsiConsole.WriteLine();
        var rule = new Rule("[bold green]📊 Simulation Complete[/]");
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green)
            .AddColumn(new TableColumn("[bold]Metric[/]").Centered())
            .AddColumn(new TableColumn("[bold]Value[/]").Centered());

        summaryTable.AddRow("Total Wafers", state.TotalWafers.ToString());
        summaryTable.AddRow("Processed", $"[green]{state.ProcessedWafers}[/]");
        summaryTable.AddRow("1-Step Wafers", state.OneStepWafers.ToString());
        summaryTable.AddRow("2-Step Wafers", state.TwoStepWafers.ToString());
        summaryTable.AddRow("Errors", state.ErrorCount > 0 ? $"[red]{state.ErrorCount}[/]" : "0");
        summaryTable.AddEmptyRow();
        summaryTable.AddRow("Total Duration", $"{state.ElapsedTime:hh\\:mm\\:ss}");
        summaryTable.AddRow("Avg Cycle Time", $"{state.Metrics["avg_cycle_time"]:F2}s");
        summaryTable.AddRow("Throughput", $"{state.ThroughputWafersPerHour:F2} wafers/hr");
        summaryTable.AddEmptyRow();
        summaryTable.AddRow("Robot Utilization", $"{state.Metrics["robot_utilization"]:F1}%");
        summaryTable.AddRow("Platen 1 Utilization", $"{state.Metrics["platen1_utilization"]:F1}%");
        summaryTable.AddRow("Platen 2 Utilization", $"{state.Metrics["platen2_utilization"]:F1}%");

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Show chart
        ShowUtilizationChart(state);
    }

    static void ShowUtilizationChart(SimulationState state)
    {
        var chart = new BreakdownChart()
            .Width(60)
            .AddItem("Robot", state.Metrics["robot_utilization"], Color.Magenta1)
            .AddItem("Platen 1", state.Metrics["platen1_utilization"], Color.Orange1)
            .AddItem("Platen 2", state.Metrics["platen2_utilization"], Color.Green);

        AnsiConsole.Write(
            new Panel(chart)
            {
                Header = new PanelHeader("[yellow]Equipment Utilization[/]"),
                Border = BoxBorder.Rounded
            });

        AnsiConsole.WriteLine();
    }
}

enum SimulationType
{
    Full,
    Demo,
    Dashboard
}
