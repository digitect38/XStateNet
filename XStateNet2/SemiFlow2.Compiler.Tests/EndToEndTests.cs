using System.Text.Json;
using SemiFlow.Compiler.Diagnostics;
using XStateNet2.Core.Engine;
using XStateNet2.Core.Parser;

namespace SemiFlow.Compiler.Tests;

public class EndToEndTests
{
    [Fact]
    public void Compile_CmpLineSystem()
    {
        var source = @"
import semiflow.algorithms.cyclic_zip
import semiflow.semi.e90

MASTER_SCHEDULER MSC_001 {
  LAYER: L1
  CONFIG {
    wafer_distribution: ""CYCLIC_ZIP""
    total_wafers: 25
    active_wsc_count: 3
    optimization_interval: 30s
  }
  SCHEDULE PRODUCTION_RUN_001 {
    wafer_count: 25
    scheduler_count: 3
    APPLY_RULE(""WAR_001"")
    APPLY_RULE(""PSR_001"")
    APPLY_RULE(""SSR_001"")
    VERIFY {
      constraint: ""all_wafers_assigned""
      constraint: ""no_conflicts""
      constraint: ""pipeline_depth <= 3""
    }
  }
}

WAFER_SCHEDULER WSC_001 {
  LAYER: L2
  CONFIG {
    assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25)
    max_concurrent: 3
  }
  subscribe to ""msc/+/command"" as msc_commands @2;
  publish status to ""wsc/001/status"" @1;
}

ROBOT_SCHEDULER RSC_EFEM_001 {
  LAYER: L3
  CONFIG {
    robot_type: ""EFEM""
    max_velocity: 2.0
    position_update_rate: 10Hz
  }
  publish position to ""rsc/efem/position"" @0, volatile;
  transaction MOVE_WAFER {
    parent: TXN_MSC_001
    timeout: 30s
  }
}

STATION STN_CMP01 {
  LAYER: L4
  CONFIG {
    type: ""CMP_POLISHER""
    process_time: 180s
    capacity: 1
  }
  publish state to ""station/cmp01/state"" @2, persistent;
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source, "cmp_line_system.sfl");

        result.Success.Should().BeTrue(
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
        result.Machine.Should().NotBeNull();
        result.Machine!.Id.Should().Be("sfl_system");
        result.Machine.Type.Should().Be("parallel");
        result.Machine.States.Should().HaveCount(4);
        result.Machine.States.Should().ContainKey("MSC_001");
        result.Machine.States.Should().ContainKey("WSC_001");
        result.Machine.States.Should().ContainKey("RSC_EFEM_001");
        result.Machine.States.Should().ContainKey("STN_CMP01");
    }

    [Fact]
    public void Compile_ProducesValidJson()
    {
        var source = @"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}
STATION STN_001 {
    LAYER: L4
    CONFIG {
        process_time: 60s
    }
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();

        var json = result.ToJson();
        json.Should().NotBeNullOrWhiteSpace();

        // Verify it's valid JSON
        var doc = JsonDocument.Parse(json);
        doc.Should().NotBeNull();

        // Verify it has expected structure
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("sfl_system");
        root.GetProperty("type").GetString().Should().Be("parallel");
        root.GetProperty("states").EnumerateObject().Should().HaveCount(2);
    }

    [Fact]
    public void Compile_JsonRoundTrip_ThroughXStateParser()
    {
        var source = @"
STATION WTR_001 {
    LAYER: L4
    STATE_MACHINE {
        initial: ""IDLE""
        states: {
            IDLE: { on: { RECEIVE_TASK: ""EXECUTING"" } }
            EXECUTING: { on: { COMPLETE: ""IDLE"" } }
        }
    }
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();

        var json = result.ToJson();

        // Parse with XStateParser
        var xparser = new XStateParser();
        var parsed = xparser.Parse(json);
        parsed.Should().NotBeNull();
        parsed.Id.Should().Be("sfl_system");
        parsed.States.Should().ContainKey("WTR_001");

        var wtr = parsed.States["WTR_001"];
        wtr.Initial.Should().Be("IDLE");
        wtr.States.Should().ContainKey("IDLE");
        wtr.States.Should().ContainKey("EXECUTING");
    }

    [Fact]
    public void Compile_WithStationStateMachineAndPubSub()
    {
        var source = @"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE RUN_001 {
        APPLY_RULE(""WAR_001"")
    }
}

STATION WTR_001 {
    LAYER: L4
    STATE_MACHINE {
        initial: ""IDLE""
        states: {
            IDLE: { on: { RECEIVE_TASK: ""EXECUTING"" } }
            EXECUTING: { on: { COMPLETE: ""IDLE"" } }
        }
    }
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();

        var json = result.ToJson();
        var xparser = new XStateParser();
        var parsed = xparser.Parse(json);
        parsed.States.Should().HaveCount(2);
    }

    [Fact]
    public void Compile_DiagnosticsReport_LayerMismatch()
    {
        var source = @"
MASTER_SCHEDULER MSC_001 {
    LAYER: L3
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);

        // Layer mismatch is an error (SFL002)
        result.Diagnostics.Should().Contain(d => d.Code == "SFL002");
    }

    [Fact]
    public void Compile_EmptySource()
    {
        var compiler = new SflCompiler();
        var result = compiler.Compile("");

        result.Success.Should().BeTrue();
        result.Machine.Should().NotBeNull();
        result.Machine!.States.Should().BeEmpty();
    }

    [Fact]
    public void CompileToJson_ReturnsJsonDirectly()
    {
        var source = @"
STATION STN_001 {
    LAYER: L4
}";
        var compiler = new SflCompiler();
        var json = compiler.CompileToJson(source);
        json.Should().Contain("sfl_system");
    }

    [Fact]
    public void Compile_MultipleSchedulerTypes()
    {
        var source = @"
MASTER_SCHEDULER MSC_001 { LAYER: L1 }
WAFER_SCHEDULER WSC_001 { LAYER: L2 }
WAFER_SCHEDULER WSC_002 { LAYER: L2 }
ROBOT_SCHEDULER RSC_001 { LAYER: L3 }
STATION STN_001 { LAYER: L4 }
STATION STN_002 { LAYER: L4 }";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();
        result.Machine!.States.Should().HaveCount(6);
    }

    [Fact]
    public void Compile_TransactionFlow()
    {
        var source = @"
TRANSACTION_FLOW {
    example: ""TXN_20240101120000_00001_A3F2""
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_TransactionManager()
    {
        var source = @"
TRANSACTION_MANAGER TXN_MGR_001 {
    TRANSACTION_SCHEMA {
        unique: true
        ttl: 3600s
    }
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Compile_AllTopologyBlocks()
    {
        var source = @"
FLOW PRODUCTION_LINE {
    SRC -> R1 -> POL -> R2 -> CLN -> R3 -> DST
}

CROSSOVER {
    POL: disabled
    CLN: enabled
}

MUTEX {
    group: L*.R1
    group: L1.R2, L1.R3
}

CONSTRAINTS {
    no_wait: [R1, R2]
    max_wip: 10
    priority: ""FIFO""
}

MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();

        var json = result.ToJson();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify meta exists with topology data
        root.TryGetProperty("meta", out var meta).Should().BeTrue();
        meta.TryGetProperty("flowName", out var flowName).Should().BeTrue();
        flowName.GetString().Should().Be("PRODUCTION_LINE");
        meta.TryGetProperty("flow", out _).Should().BeTrue();
        meta.TryGetProperty("crossover", out _).Should().BeTrue();
        meta.TryGetProperty("mutex", out _).Should().BeTrue();
        meta.TryGetProperty("constraints", out _).Should().BeTrue();
    }

    [Fact]
    public void Compile_CmpMultilaneScheduler_FullSfl()
    {
        var baseDir = Path.GetDirectoryName(typeof(EndToEndTests).Assembly.Location)!;
        var sflPath = Path.Combine(baseDir, "..", "..", "..", "..",
            "SemiFlow2", "semiflow-project", "examples", "advanced",
            "cmp_multilane_scheduler.sfl");
        var source = File.ReadAllText(sflPath);

        var compiler = new SflCompiler();
        var result = compiler.Compile(source, "cmp_multilane_scheduler.sfl");

        // Should compile with no errors (may have warnings)
        result.Errors.Should().BeEmpty(
            $"compilation errors: {string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
        result.Success.Should().BeTrue();
        result.Machine.Should().NotBeNull();

        var json = result.ToJson();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify parallel structure
        root.GetProperty("type").GetString().Should().Be("parallel");

        // Verify meta with topology data
        root.TryGetProperty("meta", out var meta).Should().BeTrue();
        meta.GetProperty("flowName").GetString().Should().Be("PRODUCTION_LINE");

        var flow = meta.GetProperty("flow");
        flow.GetArrayLength().Should().Be(11); // SRC,R1,POL,R2,CLN,R3,RNS,R4,BUF,R5,DST
        flow[0].GetString().Should().Be("SRC");
        flow[10].GetString().Should().Be("DST");

        // Verify crossover
        var crossover = meta.GetProperty("crossover");
        crossover.GetProperty("POL").GetBoolean().Should().BeFalse();
        crossover.GetProperty("CLN").GetBoolean().Should().BeTrue();

        // Verify mutex
        var mutex = meta.GetProperty("mutex");
        mutex.GetArrayLength().Should().Be(2);

        // Verify constraints
        var constraints = meta.GetProperty("constraints");
        constraints.GetProperty("max_wip").GetInt32().Should().Be(10);

        // Verify scheduler regions
        var states = root.GetProperty("states");
        states.TryGetProperty("MSC_CMP_LINE", out _).Should().BeTrue();
        states.TryGetProperty("STN_POL", out _).Should().BeTrue();
        states.TryGetProperty("STN_CLN", out _).Should().BeTrue();
        states.TryGetProperty("RSC_R1", out _).Should().BeTrue();
    }

    [Fact]
    public void Compile_BackwardCompatibility_NoTopologyBlocks()
    {
        // Existing SFL without topology blocks should compile identically
        var source = @"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}
STATION STN_001 {
    LAYER: L4
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();

        var json = result.ToJson();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // No meta property when no topology blocks
        root.TryGetProperty("meta", out _).Should().BeFalse();
    }

    [Fact]
    public void Compile_ConstraintsWithSchedulingMode()
    {
        var source = @"
FLOW LINE {
    SRC -> R1 -> POL -> DST
}

CONSTRAINTS {
    scheduling_mode: ""DEADLINE_DRIVEN""
    deadline_policy: ""EDF""
    max_wip: 5
}

MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}";

        var compiler = new SflCompiler();
        var result = compiler.Compile(source);
        result.Success.Should().BeTrue();

        var json = result.ToJson();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var meta = root.GetProperty("meta");
        var constraints = meta.GetProperty("constraints");
        constraints.GetProperty("scheduling_mode").GetString().Should().Be("DEADLINE_DRIVEN");
        constraints.GetProperty("deadline_policy").GetString().Should().Be("EDF");
        constraints.GetProperty("max_wip").GetInt32().Should().Be(5);
    }
}
