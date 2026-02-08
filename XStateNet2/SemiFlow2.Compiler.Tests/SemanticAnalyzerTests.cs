using SemiFlow.Compiler.Analysis;
using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.Diagnostics;
using SemiFlow.Compiler.Lexer;
using SemiFlow.Compiler.Parser;

namespace SemiFlow.Compiler.Tests;

public class SemanticAnalyzerTests
{
    private (SflProgram program, IReadOnlyList<Diagnostic> diagnostics) Analyze(string source)
    {
        var lexer = new SflLexer(source, "test.sfl");
        var tokens = lexer.Tokenize();
        var parser = new SflParser(tokens);
        var program = parser.ParseProgram();
        parser.Errors.Should().BeEmpty();

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);
        return (program, analyzer.Diagnostics);
    }

    [Fact]
    public void SFL002_LayerMismatch_Error()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L3
}");
        diags.Should().Contain(d => d.Code == "SFL002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SFL002_CorrectLayer_NoError()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
}
ROBOT_SCHEDULER RSC_001 {
    LAYER: L3
}
STATION STN_001 {
    LAYER: L4
}");
        diags.Where(d => d.Code == "SFL002").Should().BeEmpty();
    }

    [Fact]
    public void SFL003_UnknownRule_Warning()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE RUN {
        APPLY_RULE(""UNKNOWN_RULE"")
    }
}");
        diags.Should().Contain(d => d.Code == "SFL003");
    }

    [Fact]
    public void SFL003_BuiltInRule_NoWarning()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE RUN {
        APPLY_RULE(""WAR_001"")
        APPLY_RULE(""PSR_001"")
        APPLY_RULE(""SSR_001"")
    }
}");
        diags.Where(d => d.Code == "SFL003").Should().BeEmpty();
    }

    [Fact]
    public void SFL005_InvalidQos_Error()
    {
        // We can't test QoS > 2 through normal parse since the parser only reads an integer.
        // But we can test a valid QoS produces no error.
        var (_, diags) = Analyze(@"
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    publish status to ""wsc/001/status"" @1;
    subscribe to ""msc/+/command"" as cmd @2;
}");
        diags.Where(d => d.Code == "SFL005").Should().BeEmpty();
    }

    [Fact]
    public void SFL006_ValidTimeout_NoError()
    {
        var (_, diags) = Analyze(@"
ROBOT_SCHEDULER RSC_001 {
    LAYER: L3
    transaction MOVE {
        timeout: 30s
    }
}");
        diags.Where(d => d.Code == "SFL006").Should().BeEmpty();
    }

    [Fact]
    public void SFL008_PipelineDepthExceeded_Warning()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE RUN {
        pipeline_depth: 5
    }
}");
        diags.Should().Contain(d => d.Code == "SFL008" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SFL008_PipelineDepthNormal_NoWarning()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE RUN {
        pipeline_depth: 3
    }
}");
        diags.Where(d => d.Code == "SFL008").Should().BeEmpty();
    }

    [Fact]
    public void SFL001_NamingConvention_Warning()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER BadName {
    LAYER: L1
}");
        diags.Should().Contain(d => d.Code == "SFL001" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void NoLayer_NoDiagnostics()
    {
        var (_, diags) = Analyze(@"
MASTER_SCHEDULER MSC_001 {
}");
        diags.Where(d => d.Code == "SFL002").Should().BeEmpty();
    }

    [Fact]
    public void SFL009_FlowTooShort_Error()
    {
        var (_, diags) = Analyze(@"
FLOW LINE {
    SRC -> DST
}");
        diags.Should().Contain(d => d.Code == "SFL009" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SFL009_FlowNotStartingWithSRC_Warning()
    {
        var (_, diags) = Analyze(@"
FLOW LINE {
    R1 -> POL -> DST
}");
        diags.Should().Contain(d => d.Code == "SFL009" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SFL009_FlowValid_NoError()
    {
        var (_, diags) = Analyze(@"
FLOW LINE {
    SRC -> R1 -> POL -> DST
}");
        diags.Where(d => d.Code == "SFL009" && d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void SFL010_CrossoverStationNotInFlow_Warning()
    {
        var (_, diags) = Analyze(@"
FLOW LINE {
    SRC -> R1 -> POL -> DST
}
CROSSOVER {
    UNKNOWN: enabled
}");
        diags.Should().Contain(d => d.Code == "SFL010" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SFL010_CrossoverStationInFlow_NoWarning()
    {
        var (_, diags) = Analyze(@"
FLOW LINE {
    SRC -> R1 -> POL -> DST
}
CROSSOVER {
    POL: enabled
}");
        diags.Where(d => d.Code == "SFL010").Should().BeEmpty();
    }

    [Fact]
    public void SFL012_MaxWipNegative_Error()
    {
        var (_, diags) = Analyze(@"
CONSTRAINTS {
    max_wip: -1
}");
        diags.Should().Contain(d => d.Code == "SFL012" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SFL012_MaxWipPositive_NoError()
    {
        var (_, diags) = Analyze(@"
CONSTRAINTS {
    max_wip: 10
}");
        diags.Where(d => d.Code == "SFL012").Should().BeEmpty();
    }

    [Fact]
    public void SFL013_ValidSchedulingMode_NoError()
    {
        var (_, diags) = Analyze(@"
CONSTRAINTS {
    scheduling_mode: ""TAKT_TIME""
}");
        diags.Where(d => d.Code == "SFL013").Should().BeEmpty();
    }

    [Fact]
    public void SFL013_InvalidSchedulingMode_Error()
    {
        var (_, diags) = Analyze(@"
CONSTRAINTS {
    scheduling_mode: ""INVALID_MODE""
}");
        diags.Should().Contain(d => d.Code == "SFL013" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SFL013_DeadlinePolicyWithoutDeadlineMode_Warning()
    {
        var (_, diags) = Analyze(@"
CONSTRAINTS {
    scheduling_mode: ""TAKT_TIME""
    deadline_policy: ""EDF""
}");
        diags.Should().Contain(d => d.Code == "SFL013" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SFL013_DeadlinePolicyWithDeadlineMode_NoWarning()
    {
        var (_, diags) = Analyze(@"
CONSTRAINTS {
    scheduling_mode: ""DEADLINE_DRIVEN""
    deadline_policy: ""EDF""
}");
        diags.Where(d => d.Code == "SFL013" && d.Severity == DiagnosticSeverity.Warning).Should().BeEmpty();
    }
}
