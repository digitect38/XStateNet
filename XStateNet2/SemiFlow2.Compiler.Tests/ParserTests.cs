using SemiFlow.Compiler.Ast;
using SemiFlow.Compiler.Lexer;
using SemiFlow.Compiler.Parser;

namespace SemiFlow.Compiler.Tests;

public class ParserTests
{
    private SflProgram Parse(string source)
    {
        var lexer = new SflLexer(source, "test.sfl");
        var tokens = lexer.Tokenize();
        var parser = new SflParser(tokens);
        var program = parser.ParseProgram();
        parser.Errors.Should().BeEmpty($"unexpected parse errors: {string.Join(", ", parser.Errors)}");
        return program;
    }

    [Fact]
    public void Parse_Import()
    {
        var program = Parse("import semiflow.algorithms.cyclic_zip");

        program.Imports.Should().HaveCount(1);
        program.Imports[0].ModulePath.Should().Be("semiflow.algorithms.cyclic_zip");
    }

    [Fact]
    public void Parse_MasterScheduler_Minimal()
    {
        var program = Parse(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}");

        program.Schedulers.Should().HaveCount(1);
        var sched = program.Schedulers[0];
        sched.Type.Should().Be(SchedulerType.Master);
        sched.Name.Should().Be("MSC_001");
        sched.Layer.Should().Be("L1");
    }

    [Fact]
    public void Parse_MasterScheduler_WithConfig()
    {
        var program = Parse(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    CONFIG {
        wafer_distribution: ""CYCLIC_ZIP""
        total_wafers: 25
        optimization_interval: 30s
    }
}");

        var config = program.Schedulers[0].Config;
        config.Should().NotBeNull();
        config!.Items.Should().HaveCount(3);
        config.Items[0].Key.Should().Be("wafer_distribution");
        config.Items[0].Value.Should().BeOfType<StringValue>()
            .Which.Value.Should().Be("CYCLIC_ZIP");
        config.Items[1].Value.Should().BeOfType<IntValue>()
            .Which.Value.Should().Be(25);
        config.Items[2].Value.Should().BeOfType<DurationValue>();
    }

    [Fact]
    public void Parse_ScheduleBlock_WithApplyRule()
    {
        var program = Parse(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    SCHEDULE PRODUCTION_RUN_001 {
        wafer_count: 25
        APPLY_RULE(""WAR_001"")
        APPLY_RULE(""PSR_001"")
        VERIFY {
            constraint: ""all_wafers_assigned""
            constraint: ""no_conflicts""
        }
    }
}");

        var schedule = program.Schedulers[0].Schedules[0];
        schedule.Name.Should().Be("PRODUCTION_RUN_001");
        schedule.ApplyRules.Should().HaveCount(2);
        schedule.ApplyRules[0].Should().Be("WAR_001");
        schedule.ApplyRules[1].Should().Be("PSR_001");
        schedule.Verify.Should().NotBeNull();
        schedule.Verify!.Constraints.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_WaferScheduler_WithSubscribePublish()
    {
        var program = Parse(@"
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    subscribe to ""msc/+/command"" as msc_commands @2;
    publish status to ""wsc/001/status"" @1;
}");

        var sched = program.Schedulers[0];
        sched.Type.Should().Be(SchedulerType.Wafer);
        sched.Subscribes.Should().HaveCount(1);
        sched.Subscribes[0].Topic.Should().Be("msc/+/command");
        sched.Subscribes[0].Alias.Should().Be("msc_commands");
        sched.Subscribes[0].Qos.Should().Be(2);
        sched.Publishes.Should().HaveCount(1);
        sched.Publishes[0].Topic.Should().Be("wsc/001/status");
        sched.Publishes[0].Qos.Should().Be(1);
    }

    [Fact]
    public void Parse_RobotScheduler_WithTransaction()
    {
        var program = Parse(@"
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
}");

        var sched = program.Schedulers[0];
        sched.Type.Should().Be(SchedulerType.Robot);
        sched.Config!.Items.Should().HaveCount(3);

        var freqItem = sched.Config.Items.First(i => i.Key == "position_update_rate");
        freqItem.Value.Should().BeOfType<FrequencyValue>()
            .Which.Hz.Should().Be(10);

        sched.Publishes[0].Modifier.Should().Be("volatile");
        sched.Transactions.Should().HaveCount(1);
        sched.Transactions[0].Name.Should().Be("MOVE_WAFER");
        sched.Transactions[0].Timeout.Should().BeOfType<DurationValue>();
    }

    [Fact]
    public void Parse_Station_WithStateMachine()
    {
        var program = Parse(@"
STATION WTR_001 {
    ID: ""WTR_001""
    LAYER: L4
    STATE_MACHINE {
        initial: ""IDLE""
        states: {
            IDLE: { on: { RECEIVE_TASK: ""EXECUTING"" } }
            EXECUTING: { on: { COMPLETE: ""IDLE"" } }
        }
    }
}");

        var sched = program.Schedulers[0];
        sched.Type.Should().Be(SchedulerType.Station);
        sched.StateMachine.Should().NotBeNull();
        sched.StateMachine!.Initial.Should().Be("IDLE");
        sched.StateMachine.States.Should().HaveCount(2);
        sched.StateMachine.States["IDLE"].On!["RECEIVE_TASK"].Should().Be("EXECUTING");
    }

    [Fact]
    public void Parse_Station_WithoutStateMachine()
    {
        var program = Parse(@"
STATION STN_CMP01 {
    LAYER: L4
    CONFIG {
        type: ""CMP_POLISHER""
        process_time: 180s
        capacity: 1
    }
}");

        var sched = program.Schedulers[0];
        sched.StateMachine.Should().BeNull();
        sched.Config!.Items.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_MessageBroker()
    {
        var program = Parse(@"
MESSAGE_BROKER STATUS_BROKER_01 {
    TYPE: ""PUB_SUB""
    TOPICS {
        WTR_STATUS {
            topic: ""wtr/+/status""
            publishers: [""WTR_001"", ""WTR_002""]
        }
    }
}");

        program.MessageBrokers.Should().HaveCount(1);
        var broker = program.MessageBrokers[0];
        broker.Name.Should().Be("STATUS_BROKER_01");
        broker.Topics.Should().NotBeNull();
        broker.Topics!.Topics.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_SystemArchitecture()
    {
        var program = Parse(@"
SYSTEM_ARCHITECTURE {
    NAME: ""FAB_CMP_LINE_01""
    VERSION: ""1.0.0""
}");

        program.SystemArchitecture.Should().NotBeNull();
        program.SystemArchitecture!.Properties.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_WaferSchedulers()
    {
        var program = Parse(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    WAFER_SCHEDULERS {
        WSC_001: { priority: 1, wafers: [1, 4, 7] }
        WSC_002: { priority: 1, wafers: [2, 5, 8] }
    }
}");

        var ws = program.Schedulers[0].WaferSchedulers;
        ws.Should().NotBeNull();
        ws!.Entries.Should().HaveCount(2);
        ws.Entries[0].Name.Should().Be("WSC_001");
    }

    [Fact]
    public void Parse_AssignedWafers()
    {
        var program = Parse(@"
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    ASSIGNED_WAFERS {
        pattern: ""CYCLIC_ZIP""
        offset: 0
        stride: 3
        wafer_list: [W001, W004, W007]
    }
}");

        var aw = program.Schedulers[0].AssignedWafers;
        aw.Should().NotBeNull();
        aw!.Properties.Should().HaveCount(4);
    }

    [Fact]
    public void Parse_ControlledRobots()
    {
        var program = Parse(@"
ROBOT_SCHEDULER RSC_001 {
    LAYER: L3
    CONTROLLED_ROBOTS: [""WTR_001"", ""WTR_002""]
}");

        var robots = program.Schedulers[0].ControlledRobots;
        robots.Should().NotBeNull();
        robots.Should().HaveCount(2);
        robots![0].Should().Be("WTR_001");
    }

    [Fact]
    public void Parse_FormulaExpression()
    {
        var program = Parse(@"
WAFER_SCHEDULER WSC_001 {
    LAYER: L2
    CONFIG {
        assigned_wafers: FORMULA(CYCLIC_ZIP, 0, 3, 25)
    }
}");

        var config = program.Schedulers[0].Config!;
        config.Items[0].Value.Should().BeOfType<FormulaExpr>();
        var formula = (FormulaExpr)config.Items[0].Value;
        formula.Name.Should().Be("CYCLIC_ZIP");
        formula.Args.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_TransactionManager()
    {
        var program = Parse(@"
TRANSACTION_MANAGER TXN_MGR_001 {
    TRANSACTION_SCHEMA {
        unique: true
        ttl: 3600s
    }
}");

        program.TransactionManagers.Should().HaveCount(1);
        program.TransactionManagers[0].Name.Should().Be("TXN_MGR_001");
    }

    [Fact]
    public void Parse_ErrorRecovery()
    {
        var lexer = new SflLexer(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
    INVALID_TOKEN ???
}
STATION STN_001 {
    LAYER: L4
}", "test.sfl");
        var tokens = lexer.Tokenize();
        var parser = new SflParser(tokens);
        var program = parser.ParseProgram();

        // Should still parse the station even with errors in master scheduler
        program.Schedulers.Should().HaveCountGreaterOrEqualTo(1);
        parser.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_FlowBlock()
    {
        var program = Parse(@"
FLOW PRODUCTION_LINE {
    SRC -> R1 -> POL -> R2 -> CLN -> R3 -> DST
}");

        program.FlowBlock.Should().NotBeNull();
        program.FlowBlock!.Name.Should().Be("PRODUCTION_LINE");
        program.FlowBlock.Sequence.Should().HaveCount(7);
        program.FlowBlock.Sequence[0].Should().Be("SRC");
        program.FlowBlock.Sequence[^1].Should().Be("DST");
    }

    [Fact]
    public void Parse_CrossoverBlock()
    {
        var program = Parse(@"
CROSSOVER {
    POL: disabled
    CLN: enabled
    RNS: enabled
}");

        program.Crossover.Should().NotBeNull();
        program.Crossover!.Entries.Should().HaveCount(3);
        program.Crossover.Entries[0].StationName.Should().Be("POL");
        program.Crossover.Entries[0].Enabled.Should().BeFalse();
        program.Crossover.Entries[1].StationName.Should().Be("CLN");
        program.Crossover.Entries[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Parse_MutexBlock()
    {
        var program = Parse(@"
MUTEX {
    group: L1.R1, L2.R1
    group: L1.R2
}");

        program.Mutex.Should().NotBeNull();
        program.Mutex!.Groups.Should().HaveCount(2);
        program.Mutex.Groups[0].Patterns.Should().HaveCount(2);
        program.Mutex.Groups[0].Patterns[0].Should().Be("L1.R1");
        program.Mutex.Groups[0].Patterns[1].Should().Be("L2.R1");
        program.Mutex.Groups[1].Patterns.Should().HaveCount(1);
        program.Mutex.Groups[1].Patterns[0].Should().Be("L1.R2");
    }

    [Fact]
    public void Parse_MutexBlock_WithWildcards()
    {
        var program = Parse(@"
MUTEX {
    group: L*.R1
    group: *.R2
}");

        program.Mutex.Should().NotBeNull();
        program.Mutex!.Groups[0].Patterns[0].Should().Be("L*.R1");
        program.Mutex.Groups[1].Patterns[0].Should().Be("*.R2");
    }

    [Fact]
    public void Parse_ConstraintsBlock()
    {
        var program = Parse(@"
CONSTRAINTS {
    no_wait: [R1, R2]
    max_wip: 10
    priority: ""FIFO""
}");

        program.Constraints.Should().NotBeNull();
        program.Constraints!.Properties.Should().HaveCount(3);
        program.Constraints.Properties[0].Key.Should().Be("no_wait");
        program.Constraints.Properties[1].Key.Should().Be("max_wip");
        program.Constraints.Properties[2].Key.Should().Be("priority");
    }

    [Fact]
    public void Parse_BackwardCompatibility_NoTopologyBlocks()
    {
        var program = Parse(@"
MASTER_SCHEDULER MSC_001 {
    LAYER: L1
}");

        program.FlowBlock.Should().BeNull();
        program.Crossover.Should().BeNull();
        program.Mutex.Should().BeNull();
        program.Constraints.Should().BeNull();
    }
}
