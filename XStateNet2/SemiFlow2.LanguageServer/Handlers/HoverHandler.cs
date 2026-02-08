using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SemiFlow.Compiler.Ast;
using SemiFlow.LanguageServer.Services;

namespace SemiFlow.LanguageServer.Handlers;

/// <summary>
/// Provides hover information for SFL symbols.
/// </summary>
public class SflHoverHandler : HoverHandlerBase
{
    private readonly DocumentManager _documentManager;
    private readonly SymbolIndex _symbolIndex;

    public SflHoverHandler(DocumentManager documentManager, SymbolIndex symbolIndex)
    {
        _documentManager = documentManager;
        _symbolIndex = symbolIndex;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var state = _documentManager.Get(request.TextDocument.Uri);
        if (state == null) return Task.FromResult<Hover?>(null);

        var word = GetWordAtPosition(state.Text, request.Position);
        if (string.IsNullOrEmpty(word)) return Task.FromResult<Hover?>(null);

        var content = BuildHoverContent(word, state.Compilation.Ast);
        if (content == null) return Task.FromResult<Hover?>(null);

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = content
            })
        });
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("semiflow")
        };
    }

    private string? BuildHoverContent(string word, SflProgram? ast)
    {
        if (ast == null) return null;

        // Check if it's a scheduler name
        var scheduler = ast.Schedulers.FirstOrDefault(s => s.Name == word);
        if (scheduler != null)
            return BuildSchedulerHover(scheduler);

        // Check if it's a layer keyword
        if (word is "L1" or "L2" or "L3" or "L4")
            return BuildLayerHover(word);

        // Check in symbol index
        var symbol = _symbolIndex.Lookup(word);
        if (symbol != null)
            return BuildSymbolHover(symbol);

        // Check if it's a known keyword
        return BuildKeywordHover(word);
    }

    private static string BuildSchedulerHover(SchedulerDef scheduler)
    {
        var lines = new List<string>
        {
            $"**{scheduler.Type.ToString().ToUpper()}_SCHEDULER** `{scheduler.Name}`"
        };

        if (scheduler.Layer != null)
            lines.Add($"- **Layer**: {scheduler.Layer}");

        if (scheduler.Config != null)
        {
            lines.Add("- **Config**:");
            foreach (var item in scheduler.Config.Items.Take(5))
            {
                var val = FormatValue(item.Value);
                lines.Add($"  - `{item.Key}`: {val}");
            }
            if (scheduler.Config.Items.Count > 5)
                lines.Add($"  - *...and {scheduler.Config.Items.Count - 5} more*");
        }

        if (scheduler.Schedules.Count > 0)
            lines.Add($"- **Schedules**: {string.Join(", ", scheduler.Schedules.Select(s => s.Name))}");

        if (scheduler.Publishes.Count > 0)
            lines.Add($"- **Publishes**: {scheduler.Publishes.Count} topic(s)");

        if (scheduler.Subscribes.Count > 0)
            lines.Add($"- **Subscribes**: {scheduler.Subscribes.Count} topic(s)");

        if (scheduler.Transactions.Count > 0)
            lines.Add($"- **Transactions**: {string.Join(", ", scheduler.Transactions.Select(t => t.Name))}");

        if (scheduler.StateMachine != null)
            lines.Add($"- **State Machine**: initial = `{scheduler.StateMachine.Initial}`, {scheduler.StateMachine.States.Count} states");

        return string.Join("\n", lines);
    }

    private static string BuildLayerHover(string layer) => layer switch
    {
        "L1" => "**Layer 1** — Master Scheduler\n\nTop-level orchestration layer. Expected scheduler type: `MASTER_SCHEDULER`",
        "L2" => "**Layer 2** — Wafer Scheduler\n\nWafer-level scheduling layer. Expected scheduler type: `WAFER_SCHEDULER`",
        "L3" => "**Layer 3** — Robot Scheduler\n\nRobot control layer. Expected scheduler type: `ROBOT_SCHEDULER`",
        "L4" => "**Layer 4** — Station\n\nPhysical equipment layer. Expected scheduler type: `STATION`",
        _ => null!
    };

    private static string BuildSymbolHover(SymbolInfo symbol)
    {
        return $"**{symbol.Kind}** `{symbol.Name}`\n\nType: {symbol.Detail}";
    }

    private static string? BuildKeywordHover(string word) => word switch
    {
        "MASTER_SCHEDULER" => "**MASTER_SCHEDULER** — L1 top-level orchestration\n\nDistributes wafers across WAFER_SCHEDULERs using rules like Cyclic Zip.",
        "WAFER_SCHEDULER" => "**WAFER_SCHEDULER** — L2 wafer-level scheduling\n\nManages a subset of wafers assigned by the master scheduler.",
        "ROBOT_SCHEDULER" => "**ROBOT_SCHEDULER** — L3 robot control\n\nControls physical robot movements and wafer transfers.",
        "STATION" => "**STATION** — L4 physical equipment\n\nRepresents a physical processing station (polisher, cleaner, buffer, etc.)",
        "CONFIG" => "**CONFIG** block\n\nConfiguration key-value pairs for the parent scheduler.",
        "SCHEDULE" => "**SCHEDULE** block\n\nDefines a production schedule with rules and verification.",
        "APPLY_RULE" => "**APPLY_RULE**(\"rule_id\")\n\nApplies a scheduling rule. Built-in rules: WAR_001, PSR_001, SSR_001.",
        "FORMULA" => "**FORMULA**(name, args...)\n\nComputes a value using a named formula (e.g., CYCLIC_ZIP).",
        "VERIFY" => "**VERIFY** block\n\nDefines constraints that must hold after scheduling.",
        "STATE_MACHINE" => "**STATE_MACHINE** block\n\nEmbedded XState state machine definition with initial state and transitions.",
        "publish" => "**publish** messageType **to** \"topic\" @qos\n\nPublishes messages to an MQTT-style topic.",
        "subscribe" => "**subscribe to** \"topic\" **as** alias @qos\n\nSubscribes to messages from an MQTT-style topic.",
        "transaction" => "**transaction** NAME { ... }\n\nDefines a transaction with timeout and retry semantics.",
        _ => null
    };

    private static string FormatValue(ValueExpr expr) => expr switch
    {
        StringValue s => $"`\"{s.Value}\"`",
        IntValue i => $"`{i.Value}`",
        FloatValue f => $"`{f.Value}`",
        DurationValue d => $"`{d.Amount}{d.Unit}`",
        FrequencyValue f => $"`{f.Hz}Hz`",
        BoolValue b => $"`{b.Value}`",
        IdentifierValue id => $"`{id.Name}`",
        FormulaExpr fm => $"`FORMULA({fm.Name}, ...)`",
        ArrayValue a => $"[{a.Elements.Count} items]",
        _ => expr.ToString() ?? ""
    };

    private static string GetWordAtPosition(string text, Position position)
    {
        var lines = text.Split('\n');
        var lineIdx = (int)position.Line;
        if (lineIdx >= lines.Length) return "";

        var line = lines[lineIdx];
        var col = (int)position.Character;
        if (col >= line.Length) return "";

        // Find word boundaries
        var start = col;
        while (start > 0 && IsWordChar(line[start - 1]))
            start--;

        var end = col;
        while (end < line.Length && IsWordChar(line[end]))
            end++;

        return line[start..end];
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
