using SemiFlow.Pipeline.Simulator.Models;
using Spectre.Console;

namespace SemiFlow.Pipeline.Simulator.Pipeline;

public class PipelineVisualizer
{
    private readonly PipelineState _state;

    public PipelineVisualizer(PipelineState state)
    {
        _state = state;
    }

    public Layout CreateDashboard()
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(5),
                new Layout("Pipeline").Size(12),
                new Layout("Body").SplitColumns(
                    new Layout("Resources"),
                    new Layout("Metrics")),
                new Layout("Footer").Size(6));

        return layout;
    }

    public void UpdateDashboard(Layout layout)
    {
        UpdateHeader(layout["Header"]);
        UpdatePipeline(layout["Pipeline"]);
        UpdateResources(layout["Body"]["Resources"]);
        UpdateMetrics(layout["Body"]["Metrics"]);
        UpdateFooter(layout["Footer"]);
    }

    private void UpdateHeader(Layout layout)
    {
        var table = new Table()
            .Border(TableBorder.Double)
            .BorderColor(Color.Cyan1);

        table.AddColumn(new TableColumn("[bold cyan]CMP Pipeline Train Simulator[/]").Centered());

        var statusColor = _state.SystemRunning ? "green" : "red";
        var status = _state.SystemRunning ? "RUNNING" : "STOPPED";

        table.AddRow(
            $"[white]Lot: {_state.LotId}[/] | " +
            $"[yellow]Dispatched: {_state.WafersDispatched}/{_state.TotalWafers}[/] | " +
            $"[green]Completed: {_state.WafersCompleted}[/] | " +
            $"[cyan]Pipeline: {_state.CurrentPipelineDepth}/{_state.MaxPipelineDepth}[/] | " +
            $"[{statusColor}]{status}[/]");

        table.AddRow($"[grey]Elapsed: {_state.ElapsedTime:hh\\:mm\\:ss}[/] | " +
                     $"[grey]Throughput: {_state.ThroughputWafersPerHour:F2} wafers/hr[/] | " +
                     $"[grey]Avg Cycle: {_state.AverageCycleTime:F1}s[/]");

        layout.Update(new Panel(table) { Border = BoxBorder.None });
    }

    private void UpdatePipeline(Layout layout)
    {
        var snapshot = _state.GetPipelineSnapshot();

        var grid = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .AddColumn(new TableColumn("[yellow]Stage[/]").Centered())
            .AddColumn(new TableColumn("[yellow]Count[/]").Centered())
            .AddColumn(new TableColumn("[yellow]Wafers[/]").LeftAligned());

        // Stage 1: Loading to Platen1
        var stage1Wafers = GetWafersAtStage(WaferStage.LoadingToPlaten1);
        var stage1Visual = GetWaferListVisual(stage1Wafers, Color.Blue);
        grid.AddRow("[blue]FOUP → Platen1[/]", $"[white]{stage1Wafers.Count}[/]", stage1Visual);

        // Stage 2: Processing on Platen1
        var stage2Wafers = GetWafersAtStage(WaferStage.ProcessingPlaten1);
        var stage2Visual = GetWaferListVisual(stage2Wafers, Color.Orange1);
        grid.AddRow("[orange1]⚙ Platen1 Processing[/]", $"[white]{stage2Wafers.Count}[/]", stage2Visual);

        // Stage 3: Transfer to Platen2
        var stage3Wafers = GetWafersAtStage(WaferStage.TransferringToPlaten2);
        var stage3Visual = GetWaferListVisual(stage3Wafers, Color.Blue);
        grid.AddRow("[blue]Platen1 → Platen2[/]", $"[white]{stage3Wafers.Count}[/]", stage3Visual);

        // Stage 4: Processing on Platen2
        var stage4Wafers = GetWafersAtStage(WaferStage.ProcessingPlaten2);
        var stage4Visual = GetWaferListVisual(stage4Wafers, Color.Orange1);
        grid.AddRow("[orange1]⚙ Platen2 Processing[/]", $"[white]{stage4Wafers.Count}[/]", stage4Visual);

        // Stage 5: Unloading to FOUP
        var stage5Wafers = GetWafersAtStage(WaferStage.UnloadingToFoup);
        var stage5Visual = GetWaferListVisual(stage5Wafers, Color.Blue);
        grid.AddRow("[blue]Platen2 → FOUP[/]", $"[white]{stage5Wafers.Count}[/]", stage5Visual);

        layout.Update(new Panel(grid)
        {
            Header = new PanelHeader("[yellow]Pipeline Status[/]"),
            Border = BoxBorder.Rounded
        });
    }

    private List<PipelineWafer> GetWafersAtStage(WaferStage stage)
    {
        return _state.ActiveWafers
            .Where(w => w.CurrentStage == stage)
            .OrderBy(w => w.Id)
            .ToList();
    }

    private string GetWaferListVisual(List<PipelineWafer> wafers, Color color)
    {
        if (wafers.Count == 0)
            return "[grey]─────[/]";

        var waferTags = wafers.Select(w => $"[{color}]({w.Id:D2})[/]");
        return string.Join(" ", waferTags);
    }

    private string GetStageVisual(int count, Color color)
    {
        if (count == 0)
            return "[grey]─────[/]";

        var bars = new string('█', Math.Min(count, 10));
        return $"[{color}]{bars}[/] {(count > 10 ? $"[grey]+{count - 10}[/]" : "")}";
    }

    private void UpdateResources(Layout layout)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Resource[/]")
            .AddColumn("[cyan]Status[/]")
            .AddColumn("[cyan]Current[/]")
            .AddColumn("[cyan]Util %[/]");

        foreach (var resource in _state.Resources.Values.OrderBy(r => r.Type))
        {
            var statusColor = resource.Status switch
            {
                ResourceStatus.Idle => "green",
                ResourceStatus.Busy => "yellow",
                ResourceStatus.Processing => "orange1",
                _ => "red"
            };

            var current = resource.CurrentWafer != null
                ? $"W{resource.CurrentWafer.Id}"
                : "─";

            var util = resource.GetUtilization(_state.StartTime);

            table.AddRow(
                resource.Id,
                $"[{statusColor}]{resource.Status}[/]",
                current,
                $"{util:F1}%");
        }

        layout.Update(new Panel(table)
        {
            Header = new PanelHeader("[yellow]Resources[/]"),
            Border = BoxBorder.Rounded
        });
    }

    private void UpdateMetrics(Layout layout)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Metric[/]")
            .AddColumn("[cyan]Value[/]");

        table.AddRow("Total Wafers", _state.TotalWafers.ToString());
        table.AddRow("Waiting", _state.WaitingWafers.Count.ToString());
        table.AddRow("In Pipeline", $"[yellow]{_state.CurrentPipelineDepth}[/]");
        table.AddRow("Completed", $"[green]{_state.WafersCompleted}[/]");
        table.AddEmptyRow();
        table.AddRow("Throughput", $"{_state.ThroughputWafersPerHour:F2} /hr");
        table.AddRow("Avg Cycle", $"{_state.AverageCycleTime:F1}s");
        table.AddRow("Elapsed", $"{_state.ElapsedTime:hh\\:mm\\:ss}");

        layout.Update(new Panel(table)
        {
            Header = new PanelHeader("[yellow]Metrics[/]"),
            Border = BoxBorder.Rounded
        });
    }

    private void UpdateFooter(Layout layout)
    {
        var recent = _state.CompletedWafers.TakeLast(5).ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[cyan]Wafer[/]")
            .AddColumn("[cyan]Cycle Time[/]")
            .AddColumn("[cyan]Stages[/]");

        foreach (var wafer in recent)
        {
            var stageCount = wafer.History.Count;
            table.AddRow(
                $"W{wafer.Id}",
                $"{wafer.CycleTime.TotalSeconds:F1}s",
                $"{stageCount} stages");
        }

        if (recent.Count == 0)
        {
            table.AddRow("[grey]No completed wafers yet[/]", "", "");
        }

        layout.Update(new Panel(table)
        {
            Header = new PanelHeader("[yellow]Recent Completions[/]"),
            Border = BoxBorder.Rounded
        });
    }

    public void ShowFinalSummary()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold green]Pipeline Simulation Complete[/]").Centered());
        AnsiConsole.WriteLine();

        // Summary table
        var summary = new Table()
            .Border(TableBorder.Double)
            .BorderColor(Color.Green)
            .AddColumn(new TableColumn("[bold]Metric[/]").Centered())
            .AddColumn(new TableColumn("[bold]Value[/]").Centered());

        summary.AddRow("Total Wafers", _state.TotalWafers.ToString());
        summary.AddRow("Completed", $"[green]{_state.WafersCompleted}[/]");
        summary.AddRow("Success Rate", $"{(_state.WafersCompleted * 100.0 / _state.TotalWafers):F1}%");
        summary.AddEmptyRow();
        summary.AddRow("Total Duration", $"{_state.ElapsedTime:hh\\:mm\\:ss}");
        summary.AddRow("Throughput", $"{_state.ThroughputWafersPerHour:F2} wafers/hr");
        summary.AddRow("Avg Cycle Time", $"{_state.AverageCycleTime:F1}s");
        summary.AddEmptyRow();

        foreach (var resource in _state.Resources.Values.OrderBy(r => r.Type))
        {
            var util = resource.GetUtilization(_state.StartTime);
            summary.AddRow($"{resource.Id} Utilization", $"{util:F1}%");
        }

        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();

        // Utilization chart
        var chart = new BreakdownChart()
            .Width(60);

        foreach (var resource in _state.Resources.Values.OrderBy(r => r.Type))
        {
            var util = resource.GetUtilization(_state.StartTime);
            var color = resource.Type switch
            {
                ResourceType.Robot => Color.Magenta1,
                ResourceType.Platen1 => Color.Orange1,
                ResourceType.Platen2 => Color.Green,
                _ => Color.Grey
            };
            chart.AddItem(resource.Id, util, color);
        }

        AnsiConsole.Write(new Panel(chart)
        {
            Header = new PanelHeader("[yellow]Resource Utilization[/]"),
            Border = BoxBorder.Rounded
        });

        AnsiConsole.WriteLine();
    }
}
