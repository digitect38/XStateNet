using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SemiFlow.LanguageServer.Services;

namespace SemiFlow.LanguageServer.Handlers;

/// <summary>
/// Provides code completion for SFL documents.
/// </summary>
public class SflCompletionHandler : CompletionHandlerBase
{
    private readonly DocumentManager _documentManager;
    private readonly SymbolIndex _symbolIndex;

    public SflCompletionHandler(DocumentManager documentManager, SymbolIndex symbolIndex)
    {
        _documentManager = documentManager;
        _symbolIndex = symbolIndex;
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
    {
        // Resolve: just return the item as-is (no additional resolution)
        return Task.FromResult(request);
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var items = new List<CompletionItem>();
        var state = _documentManager.Get(request.TextDocument.Uri);

        // Get the line text to determine context
        var lineText = GetLineText(state?.Text, (int)request.Position.Line);
        var trimmed = lineText.TrimStart();

        if (IsTopLevelContext(trimmed, state))
        {
            items.AddRange(TopLevelCompletions());
        }
        else if (IsInsideSchedulerBody(trimmed))
        {
            items.AddRange(SchedulerBodyCompletions());
        }
        else if (IsAfterLayer(trimmed))
        {
            items.AddRange(LayerCompletions());
        }
        else if (IsInsideApplyRule(trimmed))
        {
            items.AddRange(RuleCompletions());
        }
        else if (IsAfterAt(trimmed))
        {
            items.AddRange(QosCompletions());
        }
        else
        {
            // Default: all keywords + known symbols
            items.AddRange(TopLevelCompletions());
            items.AddRange(SchedulerBodyCompletions());
            items.AddRange(SymbolCompletions());
        }

        return Task.FromResult(new CompletionList(items));
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("semiflow"),
            TriggerCharacters = new Container<string>(":", "\"", "@", " "),
            ResolveProvider = false
        };
    }

    private static List<CompletionItem> TopLevelCompletions() => new()
    {
        MakeKeyword("MASTER_SCHEDULER", "Master Scheduler (L1) - Top-level orchestration"),
        MakeKeyword("WAFER_SCHEDULER", "Wafer Scheduler (L2) - Wafer-level scheduling"),
        MakeKeyword("ROBOT_SCHEDULER", "Robot Scheduler (L3) - Robot control"),
        MakeKeyword("STATION", "Station (L4) - Physical equipment"),
        MakeKeyword("SYSTEM_ARCHITECTURE", "System architecture definition"),
        MakeKeyword("MESSAGE_BROKER", "Message broker configuration"),
        MakeKeyword("TRANSACTION_MANAGER", "Transaction manager definition"),
        MakeKeyword("TRANSACTION_FLOW", "Transaction flow definition"),
        MakeKeyword("PIPELINE_SCHEDULING_RULES", "Pipeline scheduling rules"),
        MakeKeyword("RULE", "Rule definition"),
        MakeKeyword("import", "Import module"),
    };

    private static List<CompletionItem> SchedulerBodyCompletions() => new()
    {
        MakeKeyword("LAYER", "Layer assignment (L1-L4)"),
        MakeSnippet("CONFIG", "CONFIG {\n\t$0\n}", "Configuration block"),
        MakeSnippet("SCHEDULE", "SCHEDULE ${1:name} {\n\t$0\n}", "Schedule definition"),
        MakeSnippet("publish", "publish ${1:messageType} to \"${2:topic}\" @${3|0,1,2|};", "Publish statement"),
        MakeSnippet("subscribe", "subscribe to \"${1:topic}\" as ${2:alias} @${3|0,1,2|};", "Subscribe statement"),
        MakeSnippet("transaction", "transaction ${1:NAME} {\n\ttimeout: ${2:30s}\n}", "Transaction definition"),
        MakeSnippet("STATE_MACHINE", "STATE_MACHINE {\n\tinitial: \"${1:IDLE}\"\n\tstates: {\n\t\t${1:IDLE}: { on: { ${2:EVENT}: \"${3:TARGET}\" } }\n\t}\n}", "Embedded state machine"),
        MakeSnippet("ASSIGNED_WAFERS", "ASSIGNED_WAFERS {\n\t$0\n}", "Assigned wafers block"),
        MakeSnippet("WAFER_SCHEDULERS", "WAFER_SCHEDULERS {\n\t$0\n}", "Wafer schedulers block"),
        MakeKeyword("CONTROLLED_ROBOTS", "Controlled robots list"),
        MakeSnippet("VERIFY", "VERIFY {\n\tconstraint: \"${1:expression}\"\n}", "Verification block"),
        MakeSnippet("APPLY_RULE", "APPLY_RULE(\"${1|WAR_001,PSR_001,SSR_001|}\")", "Apply scheduling rule"),
    };

    private static List<CompletionItem> LayerCompletions() => new()
    {
        MakeValue("L1", "Master Scheduler layer"),
        MakeValue("L2", "Wafer Scheduler layer"),
        MakeValue("L3", "Robot Scheduler layer"),
        MakeValue("L4", "Station layer"),
    };

    private static List<CompletionItem> RuleCompletions() => new()
    {
        MakeValue("WAR_001", "Wafer Assignment Rule - Cyclic Zip Distribution"),
        MakeValue("PSR_001", "Pipeline Scheduling Rule - Slot Assignment"),
        MakeValue("SSR_001", "Steady State Rule - Three-Phase Maintenance"),
    };

    private static List<CompletionItem> QosCompletions() => new()
    {
        MakeValue("0", "QoS 0: Fire-and-forget (at most once)"),
        MakeValue("1", "QoS 1: At least once delivery"),
        MakeValue("2", "QoS 2: Exactly once delivery"),
    };

    private List<CompletionItem> SymbolCompletions()
    {
        return _symbolIndex.AllSymbols.Select(s => new CompletionItem
        {
            Label = s.Name,
            Kind = s.Kind switch
            {
                SymbolKindEx.Scheduler => CompletionItemKind.Class,
                SymbolKindEx.Rule => CompletionItemKind.Constant,
                SymbolKindEx.MessageBroker => CompletionItemKind.Interface,
                SymbolKindEx.Transaction => CompletionItemKind.Function,
                _ => CompletionItemKind.Reference
            },
            Detail = s.Detail
        }).ToList();
    }

    // --- Context detection helpers ---

    private static string GetLineText(string? text, int line)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var lines = text.Split('\n');
        return line < lines.Length ? lines[line] : "";
    }

    private static bool IsTopLevelContext(string line, DocumentState? state)
    {
        // At the very beginning of a line outside any block
        return string.IsNullOrWhiteSpace(line) || line.Length == 0;
    }

    private static bool IsInsideSchedulerBody(string line)
    {
        // Indented line inside a block
        return line.StartsWith(" ") || line.StartsWith("\t");
    }

    private static bool IsAfterLayer(string line)
    {
        return line.Contains("LAYER:") || line.TrimEnd().EndsWith("LAYER:");
    }

    private static bool IsInsideApplyRule(string line)
    {
        return line.Contains("APPLY_RULE(");
    }

    private static bool IsAfterAt(string line)
    {
        return line.TrimEnd().EndsWith("@");
    }

    // --- Factory helpers ---

    private static CompletionItem MakeKeyword(string label, string detail) => new()
    {
        Label = label,
        Kind = CompletionItemKind.Keyword,
        Detail = detail
    };

    private static CompletionItem MakeSnippet(string label, string snippet, string detail) => new()
    {
        Label = label,
        Kind = CompletionItemKind.Snippet,
        InsertTextFormat = InsertTextFormat.Snippet,
        InsertText = snippet,
        Detail = detail
    };

    private static CompletionItem MakeValue(string label, string detail) => new()
    {
        Label = label,
        Kind = CompletionItemKind.Value,
        Detail = detail
    };
}
