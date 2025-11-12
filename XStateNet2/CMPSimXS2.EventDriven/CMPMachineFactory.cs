using Akka.Actor;
using CMPSimXS2.EventDriven.Actions;
using CMPSimXS2.EventDriven.Guards;
using XStateNet2.Core.Builder;
using XStateNet2.Core.Factory;

namespace CMPSimXS2.EventDriven;

/// <summary>
/// Factory class to build and configure the CMP state machine
/// </summary>
public static class CMPMachineFactory
{
    /// <summary>
    /// Create and configure a CMP state machine actor
    /// </summary>
    public static IActorRef Create(ActorSystem actorSystem, string machineJson)
    {
        // Create the XState machine factory
        var factory = new XStateMachineFactory(actorSystem);

        // Build the machine with all guards and actions
        var machineBuilder = factory.FromJson(machineJson);

        // Register all guards first (these don't need self reference)
        machineBuilder
            .WithGuard("canStartCycle", CMPGuards.CanStartCycle)
            .WithGuard("allWafersProcessed", CMPGuards.AllWafersProcessed)
            .WithGuard("canRetry", CMPGuards.CanRetry)
            .WithGuard("robotNotBusy", CMPGuards.RobotNotBusy)
            .WithGuard("platenNotLocked", CMPGuards.PlatenNotLocked)
            .WithGuard("pickSuccessful", CMPGuards.PickSuccessful)
            .WithGuard("placeSuccessful", CMPGuards.PlaceSuccessful)
            .WithGuard("polishSuccessful", CMPGuards.PolishSuccessful);

        // Use a late-bound reference that will be set after the actor is created
        IActorRef? actorRef = null;

        // Register all actions with a closure that captures the actor reference
        CMPActions.RegisterAll(machineBuilder, () => actorRef!);

        // Build and start the actor
        var actor = machineBuilder.BuildAndStart("cmp-machine");

        // Set the actor reference for the actions
        actorRef = actor;

        return actor;
    }
}
