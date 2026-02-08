namespace SemiFlow.Compiler.Ast;

public interface IAstVisitor<T>
{
    T VisitProgram(SflProgram node);
    T VisitImport(ImportDecl node);
    T VisitSystemArchitecture(SystemArchitectureDef node);
    T VisitScheduler(SchedulerDef node);
    T VisitConfigBlock(ConfigBlock node);
    T VisitScheduleBlock(ScheduleBlock node);
    T VisitVerifyBlock(VerifyBlock node);
    T VisitPublish(PublishStmt node);
    T VisitSubscribe(SubscribeStmt node);
    T VisitTransaction(TransactionDef node);
    T VisitStateMachine(StateMachineDef node);
    T VisitMessageBroker(MessageBrokerDef node);
    T VisitTransactionManager(TransactionManagerDef node);
    T VisitTransactionFlow(TransactionFlowDef node);
    T VisitPipelineRules(PipelineRulesDef node);
    T VisitRule(RuleDef node);
    T VisitFlowBlock(FlowBlockDef node);
    T VisitCrossover(CrossoverDef node);
    T VisitMutex(MutexDef node);
    T VisitConstraints(ConstraintsDef node);
}
