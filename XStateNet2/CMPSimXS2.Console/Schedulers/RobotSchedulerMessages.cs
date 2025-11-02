using Akka.Actor;
using CMPSimXS2.Console.Models;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Messages for RobotSchedulerActor (Actor-based version)
/// </summary>
public static class RobotSchedulerMessages
{
    /// <summary>
    /// Register a robot with the scheduler
    /// </summary>
    public record RegisterRobot(string RobotId, IActorRef RobotActor);

    /// <summary>
    /// Update robot state (sent by robot actors)
    /// </summary>
    public record UpdateRobotState(
        string RobotId,
        string State,
        int? HeldWaferId = null,
        string? WaitingFor = null);

    /// <summary>
    /// Request a transfer
    /// </summary>
    public record RequestTransfer(TransferRequest Request);

    /// <summary>
    /// Query queue size
    /// </summary>
    public record GetQueueSize;

    /// <summary>
    /// Response with queue size
    /// </summary>
    public record QueueSize(int Count);

    /// <summary>
    /// Query robot state
    /// </summary>
    public record GetRobotState(string RobotId);

    /// <summary>
    /// Response with robot state
    /// </summary>
    public record RobotStateResponse(string State);
}
