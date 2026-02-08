using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.CodeGen;
using SemiFlow.Compiler.Lexer;
using SemiFlow.Compiler.Parser;
using XStateNet2.Core.Engine;

namespace SemiFlow.Compiler.Tests;

public class CodeGenTests
{
    private XStateMachineScript Generate(string source)
    {
        var lexer = new SflLexer(source, "test.sfl");
        var tokens = lexer.Tokenize();
        var parser = new SflParser(tokens);
        var program = parser.ParseProgram();
        parser.Errors.Should().BeEmpty();

        var gen = new XStateGenerator();
        return gen.Generate(program);
    }

    [Fact]
    public void Generate_ParallelMachine()
    {
        var machine = Generate(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}
STATION STN_001 {
    LAYER: L4
}");

        machine.Id.Should().Be("sfl_system");
        machine.Type.Should().Be("parallel");
        machine.States.Should().ContainKey("MSC_001");
        machine.States.Should().ContainKey("STN_001");
    }

    [Fact]
    public void Generate_MasterScheduler_HasStandardStates()
    {
        var machine = Generate(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE RUN_001 {
        APPLY_RULE(""WAR_001"")
    }
}");

        var mscNode = machine.States["MSC_001"];
        mscNode.Initial.Should().Be("idle");
        mscNode.States.Should().ContainKey("idle");
        mscNode.States.Should().ContainKey("scheduling");
        mscNode.States.Should().ContainKey("monitoring");
        mscNode.States.Should().ContainKey("completed");

        // Completed should be final
        mscNode.States!["completed"].Type.Should().Be("final");
    }

    [Fact]
    public void Generate_MasterScheduler_ApplyRulesAsEntryActions()
    {
        var machine = Generate(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE RUN_001 {
        APPLY_RULE(""WAR_001"")
        APPLY_RULE(""PSR_001"")
    }
}");

        var scheduling = machine.States["MSC_001"].States!["scheduling"];
        scheduling.Entry.Should().NotBeNull();
        scheduling.Entry.Should().Contain("applyRule_WAR_001");
        scheduling.Entry.Should().Contain("applyRule_PSR_001");
    }

    [Fact]
    public void Generate_Station_WithStateMachine()
    {
        var machine = Generate(@"
STATION WTR_001 {
    LAYER: L4
    STATE_MACHINE {
        initial: ""IDLE""
        states: {
            IDLE: { on: { RECEIVE_TASK: ""EXECUTING"" } }
            EXECUTING: { on: { COMPLETE: ""IDLE"" } }
        }
    }
}");

        var station = machine.States["WTR_001"];
        station.Initial.Should().Be("IDLE");
        station.States.Should().ContainKey("IDLE");
        station.States.Should().ContainKey("EXECUTING");

        var idleOn = station.States!["IDLE"].On!;
        idleOn.Should().ContainKey("RECEIVE_TASK");
        idleOn["RECEIVE_TASK"][0].Target.Should().Be("EXECUTING");
    }

    [Fact]
    public void Generate_Station_DefaultStates()
    {
        var machine = Generate(@"
STATION STN_CMP01 {
    LAYER: L4
    CONFIG {
        process_time: 180s
    }
}");

        var station = machine.States["STN_CMP01"];
        station.Initial.Should().Be("idle");
        station.States.Should().ContainKey("idle");
        station.States.Should().ContainKey("processing");
        station.States.Should().ContainKey("alarm");

        // Processing should have after delay
        var processing = station.States!["processing"];
        processing.After.Should().NotBeNull();
        processing.After.Should().ContainKey(180000);
    }

    [Fact]
    public void Generate_RobotScheduler_WithTransaction()
    {
        var machine = Generate(@"
ROBOT_SCHEDULER RSC_001 {
    LAYER: L3
    transaction MOVE_WAFER {
        timeout: 30s
    }
}");

        var robot = machine.States["RSC_001"];
        var moving = robot.States!["moving"];
        moving.Initial.Should().Be("begin");
        moving.States.Should().ContainKey("begin");
        moving.States.Should().ContainKey("execute");
        moving.States.Should().ContainKey("commit");
        moving.States.Should().ContainKey("rollback");

        // Execute should have timeout
        var execute = moving.States!["execute"];
        execute.After.Should().ContainKey(30000);
    }

    [Fact]
    public void Generate_MetadataIncludesLayer()
    {
        var machine = Generate(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}");

        var meta = machine.States["MSC_001"].Meta!;
        meta.Should().ContainKey("layer");
        meta["layer"].Should().Be("L1");
        meta.Should().ContainKey("schedulerType");
        meta["schedulerType"].Should().Be("Master");
    }

    [Fact]
    public void Generate_SubscribeBecomesEventHandler()
    {
        var machine = Generate(@"
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    subscribe to ""msc/+/command"" as msc_commands @2;
}");

        var wsc = machine.States["WSC_001"];
        wsc.On.Should().NotBeNull();
        wsc.On.Should().ContainKey("msc/+/command");
    }

    [Fact]
    public void Generate_WaferScheduler_HasStandardStates()
    {
        var machine = Generate(@"
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
}");

        var wsc = machine.States["WSC_001"];
        wsc.Initial.Should().Be("idle");
        wsc.States.Should().ContainKey("idle");
        wsc.States.Should().ContainKey("processing");
        wsc.States.Should().ContainKey("error");
        wsc.States.Should().ContainKey("completed");
    }

    [Fact]
    public void Generate_FlowBlock_EmitsMeta()
    {
        var machine = Generate(@"
FLOW PRODUCTION_LINE {
    SRC -> R1 -> POL -> R2 -> DST
}");

        machine.Meta.Should().NotBeNull();
        machine.Meta.Should().ContainKey("flowName");
        machine.Meta!["flowName"].Should().Be("PRODUCTION_LINE");
        machine.Meta.Should().ContainKey("flow");
        var flow = machine.Meta["flow"] as List<string>;
        flow.Should().NotBeNull();
        flow.Should().HaveCount(5);
        flow![0].Should().Be("SRC");
    }

    [Fact]
    public void Generate_CrossoverBlock_EmitsMeta()
    {
        var machine = Generate(@"
CROSSOVER {
    POL: disabled
    CLN: enabled
}");

        machine.Meta.Should().NotBeNull();
        machine.Meta.Should().ContainKey("crossover");
        var crossover = machine.Meta!["crossover"] as Dictionary<string, object>;
        crossover.Should().NotBeNull();
        crossover!["POL"].Should().Be(false);
        crossover["CLN"].Should().Be(true);
    }

    [Fact]
    public void Generate_MutexBlock_EmitsMeta()
    {
        var machine = Generate(@"
MUTEX {
    group: L*.R1
    group: L1.R2, L1.R3
}");

        machine.Meta.Should().NotBeNull();
        machine.Meta.Should().ContainKey("mutex");
        var mutex = machine.Meta!["mutex"] as List<object>;
        mutex.Should().NotBeNull();
        mutex.Should().HaveCount(2);

        var group1 = mutex![0] as Dictionary<string, object>;
        group1.Should().NotBeNull();
        group1!["isGlobal"].Should().Be(true); // L*.R1 has wildcard
    }

    [Fact]
    public void Generate_ConstraintsBlock_EmitsMeta()
    {
        var machine = Generate(@"
CONSTRAINTS {
    max_wip: 10
    priority: ""FIFO""
}");

        machine.Meta.Should().NotBeNull();
        machine.Meta.Should().ContainKey("constraints");
        var constraints = machine.Meta!["constraints"] as Dictionary<string, object>;
        constraints.Should().NotBeNull();
        constraints!["max_wip"].Should().Be(10);
        constraints["priority"].Should().Be("FIFO");
    }

    [Fact]
    public void Generate_NoTopologyBlocks_NoMeta()
    {
        var machine = Generate(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}");

        machine.Meta.Should().BeNull();
    }

    [Fact]
    public void Generate_ConstraintsBlock_EmitsSchedulingMode()
    {
        var machine = Generate(@"
CONSTRAINTS {
    scheduling_mode: ""EVENT_DRIVEN""
    max_wip: 5
}");

        machine.Meta.Should().NotBeNull();
        machine.Meta.Should().ContainKey("constraints");
        var constraints = machine.Meta!["constraints"] as Dictionary<string, object>;
        constraints.Should().NotBeNull();
        constraints!["scheduling_mode"].Should().Be("EVENT_DRIVEN");
        constraints["max_wip"].Should().Be(5);
    }
}
