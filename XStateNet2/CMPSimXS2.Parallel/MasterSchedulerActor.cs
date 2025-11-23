using Akka.Actor;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Layer 1: Master scheduler
/// Manages overall system state and coordinates wafer spawning
/// </summary>
public class MasterSchedulerActor : ReceiveActor
{
    private readonly string _masterJson;
    private readonly IActorRef _coordinator;

    public MasterSchedulerActor(string masterJson, IActorRef coordinator)
    {
        _masterJson = masterJson;
        _coordinator = coordinator;

        TableLogger.Log("[MASTER] Master scheduler initialized");
    }
}
