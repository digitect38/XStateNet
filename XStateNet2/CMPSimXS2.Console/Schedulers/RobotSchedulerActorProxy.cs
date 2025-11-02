using Akka.Actor;
using CMPSimXS2.Console.Models;
using static CMPSimXS2.Console.Schedulers.RobotSchedulerMessages;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Proxy wrapper for RobotSchedulerActor to provide same API as RobotScheduler
/// Allows easy switching between lock-based and actor-based implementations
/// ACTOR-BASED VERSION (NO LOCKS)
/// </summary>
public class RobotSchedulerActorProxy : IRobotScheduler
{
    private readonly IActorRef _schedulerActor;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);

    public RobotSchedulerActorProxy(ActorSystem actorSystem, string? actorName = null)
    {
        // Use unique name if provided, otherwise generate one
        var name = actorName ?? $"robot-scheduler-{Guid.NewGuid():N}";
        _schedulerActor = actorSystem.ActorOf(Props.Create<RobotSchedulerActor>(), name);
    }

    /// <summary>
    /// Register a robot state machine
    /// </summary>
    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        _schedulerActor.Tell(new RegisterRobot(robotId, robotActor));
    }

    /// <summary>
    /// Update robot state
    /// </summary>
    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        _schedulerActor.Tell(new UpdateRobotState(robotId, state, heldWaferId, waitingFor));
    }

    /// <summary>
    /// Request a transfer
    /// </summary>
    public void RequestTransfer(TransferRequest request)
    {
        _schedulerActor.Tell(new RequestTransfer(request));
    }

    /// <summary>
    /// Get current queue size
    /// </summary>
    public int GetQueueSize()
    {
        var result = _schedulerActor.Ask<QueueSize>(new GetQueueSize(), _defaultTimeout).Result;
        return result.Count;
    }

    /// <summary>
    /// Get robot state for debugging
    /// </summary>
    public string GetRobotState(string robotId)
    {
        var result = _schedulerActor.Ask<RobotStateResponse>(new GetRobotState(robotId), _defaultTimeout).Result;
        return result.State;
    }
}
