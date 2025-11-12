using Akka.Actor;

namespace SimpleTaskLoopCMPXS2
{
    // Messages for Station communication
    public record PickRequest(IActorRef ReplyTo);
    public record PickResponse(Wafer? Wafer);
    public record PlaceRequest(Wafer Wafer, IActorRef ReplyTo);
    public record PlaceResponse(bool Success);

    // Process station specific messages
    public record StartProcessing();
    public record ProcessingComplete();

    // Query messages
    public record GetStateRequest(IActorRef ReplyTo);
    public record StateResponse(string State, bool HasWafer, bool IsProcessed);
}
