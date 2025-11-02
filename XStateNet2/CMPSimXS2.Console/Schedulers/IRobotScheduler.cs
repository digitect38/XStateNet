using Akka.Actor;
using CMPSimXS2.Console.Models;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Common interface for robot schedulers (both lock-based and actor-based)
/// </summary>
public interface IRobotScheduler
{
    void RegisterRobot(string robotId, IActorRef robotActor);
    void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null);
    void RequestTransfer(TransferRequest request);
    int GetQueueSize();
    string GetRobotState(string robotId);
}
