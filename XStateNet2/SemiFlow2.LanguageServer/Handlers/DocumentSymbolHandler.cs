using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.Lexer;
using SemiFlow.LanguageServer.Services;

namespace SemiFlow.LanguageServer.Handlers;

/// <summary>
/// Provides document symbols for the outline view and breadcrumbs.
/// </summary>
public class SflDocumentSymbolHandler : DocumentSymbolHandlerBase
{
    private readonly DocumentManager _documentManager;

    public SflDocumentSymbolHandler(DocumentManager documentManager)
    {
        _documentManager = documentManager;
    }

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken ct)
    {
        var state = _documentManager.Get(request.TextDocument.Uri);
        if (state?.Compilation.Ast == null)
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);

        var symbols = new List<DocumentSymbol>();
        var ast = state.Compilation.Ast;

        // System architecture
        if (ast.SystemArchitecture != null)
        {
            symbols.Add(MakeSymbol(
                "SYSTEM_ARCHITECTURE",
                SymbolKind.Module,
                ast.SystemArchitecture.Location,
                null));
        }

        // Schedulers
        foreach (var sched in ast.Schedulers)
        {
            var children = new List<DocumentSymbol>();

            // Layer
            if (sched.Layer != null)
            {
                children.Add(MakeSymbol(
                    $"LAYER: {sched.Layer}",
                    SymbolKind.Property,
                    sched.Location,
                    null));
            }

            // Config
            if (sched.Config != null)
            {
                var configChildren = sched.Config.Items.Select(item =>
                    MakeSymbol(
                        $"{item.Key}: {FormatValueShort(item.Value)}",
                        SymbolKind.Property,
                        item.Location,
                        null)).ToList();

                children.Add(MakeSymbol(
                    "CONFIG",
                    SymbolKind.Property,
                    sched.Config.Location,
                    configChildren));
            }

            // Schedules
            foreach (var schedule in sched.Schedules)
            {
                var schedChildren = new List<DocumentSymbol>();
                foreach (var rule in schedule.ApplyRules)
                {
                    schedChildren.Add(MakeSymbol(
                        $"APPLY_RULE(\"{rule}\")",
                        SymbolKind.Constant,
                        schedule.Location,
                        null));
                }
                if (schedule.Verify != null)
                {
                    schedChildren.Add(MakeSymbol(
                        $"VERIFY ({schedule.Verify.Constraints.Count} constraints)",
                        SymbolKind.Boolean,
                        schedule.Verify.Location,
                        null));
                }

                children.Add(MakeSymbol(
                    $"SCHEDULE {schedule.Name}",
                    SymbolKind.Method,
                    schedule.Location,
                    schedChildren));
            }

            // Pub/Sub
            foreach (var pub in sched.Publishes)
            {
                children.Add(MakeSymbol(
                    $"publish {pub.MessageType} → \"{pub.Topic}\"",
                    SymbolKind.Event,
                    pub.Location,
                    null));
            }
            foreach (var sub in sched.Subscribes)
            {
                children.Add(MakeSymbol(
                    $"subscribe \"{sub.Topic}\" as {sub.Alias}",
                    SymbolKind.Event,
                    sub.Location,
                    null));
            }

            // Transactions
            foreach (var txn in sched.Transactions)
            {
                children.Add(MakeSymbol(
                    $"transaction {txn.Name}",
                    SymbolKind.Function,
                    txn.Location,
                    null));
            }

            // State machine
            if (sched.StateMachine != null)
            {
                var smChildren = sched.StateMachine.States.Select(kvp =>
                    MakeSymbol(
                        kvp.Key,
                        SymbolKind.Field,
                        kvp.Value.Location,
                        null)).ToList();

                children.Add(MakeSymbol(
                    $"STATE_MACHINE (initial: {sched.StateMachine.Initial})",
                    SymbolKind.Struct,
                    sched.StateMachine.Location,
                    smChildren));
            }

            var typeLabel = sched.Type switch
            {
                SchedulerType.Master => "MASTER_SCHEDULER",
                SchedulerType.Wafer => "WAFER_SCHEDULER",
                SchedulerType.Robot => "ROBOT_SCHEDULER",
                SchedulerType.Station => "STATION",
                _ => "SCHEDULER"
            };

            symbols.Add(MakeSymbol(
                $"{typeLabel} {sched.Name}",
                SymbolKind.Class,
                sched.Location,
                children));
        }

        // Message brokers
        foreach (var broker in ast.MessageBrokers)
        {
            symbols.Add(MakeSymbol(
                $"MESSAGE_BROKER {broker.Name}",
                SymbolKind.Interface,
                broker.Location,
                null));
        }

        // Transaction managers
        foreach (var tm in ast.TransactionManagers)
        {
            symbols.Add(MakeSymbol(
                $"TRANSACTION_MANAGER {tm.Name}",
                SymbolKind.Interface,
                tm.Location,
                null));
        }

        // Pipeline rules
        if (ast.PipelineRules != null)
        {
            var ruleSymbols = ast.PipelineRules.Rules.Select(r =>
                MakeSymbol(r.Name, SymbolKind.Constant, r.Location, null)).ToList();

            symbols.Add(MakeSymbol(
                "PIPELINE_SCHEDULING_RULES",
                SymbolKind.Namespace,
                ast.PipelineRules.Location,
                ruleSymbols));
        }

        // Top-level rules
        foreach (var rule in ast.Rules)
        {
            symbols.Add(MakeSymbol(
                $"RULE {rule.Name}",
                SymbolKind.Constant,
                rule.Location,
                null));
        }

        var result = symbols.Select(s =>
            new SymbolInformationOrDocumentSymbol(s)).ToArray();
        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(result));
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentSymbolRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("semiflow")
        };
    }

    private static DocumentSymbol MakeSymbol(
        string name,
        SymbolKind kind,
        SourceLocation loc,
        List<DocumentSymbol>? children)
    {
        var line = Math.Max(0, loc.Line - 1);
        var col = Math.Max(0, loc.Column - 1);
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            new Position(line, col),
            new Position(line, col + name.Length));

        return new DocumentSymbol
        {
            Name = name,
            Kind = kind,
            Range = range,
            SelectionRange = range,
            Children = children != null
                ? new Container<DocumentSymbol>(children)
                : null
        };
    }

    private static string FormatValueShort(ValueExpr expr) => expr switch
    {
        StringValue s => $"\"{s.Value}\"",
        IntValue i => i.Value.ToString(),
        FloatValue f => f.Value.ToString("G"),
        DurationValue d => $"{d.Amount}{d.Unit}",
        FrequencyValue f => $"{f.Hz}Hz",
        BoolValue b => b.Value.ToString().ToLower(),
        IdentifierValue id => id.Name,
        FormulaExpr fm => $"FORMULA({fm.Name}, ...)",
        ArrayValue a => $"[{a.Elements.Count} items]",
        _ => "..."
    };
}
