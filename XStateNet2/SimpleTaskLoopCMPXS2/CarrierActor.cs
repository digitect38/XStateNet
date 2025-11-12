using Akka.Actor;

namespace SimpleTaskLoopCMPXS2
{
    /// <summary>
    /// Carrier actor - manages wafer slots
    /// </summary>
    public class CarrierActor : ReceiveActor
    {
        private readonly Wafer?[] _wafers;
        private readonly int _waferCount;

        public bool IsArrived { get; set; }
        public string Name { get; }

        public CarrierActor(int waferCount = Config.W_COUNT)
        {
            Name = "Carrier";
            _waferCount = waferCount;
            _wafers = new Wafer[_waferCount];

            // Initialize with wafers
            for (int i = 0; i < _waferCount; i++)
                _wafers[i] = new Wafer();

            Become(Ready);
        }

        private void Ready()
        {
            Receive<PickRequest>(msg =>
            {
                // Pick first unprocessed wafer
                for (int i = 0; i < _waferCount; i++)
                {
                    var wafer = _wafers[i];
                    if (wafer != null && !wafer.IsProcessed)
                    {
                        _wafers[i] = null;
                        Logger.Log($"Wafer {wafer.Id} picked from Carrier (slot {i})");
                        msg.ReplyTo.Tell(new PickResponse(wafer));
                        return;
                    }
                }
                msg.ReplyTo.Tell(new PickResponse(null));
            });

            Receive<PlaceRequest>(msg =>
            {
                // Place wafer in first empty slot
                for (int i = 0; i < _waferCount; i++)
                {
                    if (_wafers[i] == null)
                    {
                        _wafers[i] = msg.Wafer;
                        Logger.Log($"Wafer {msg.Wafer.Id} placed in Carrier (slot {i})");
                        msg.ReplyTo.Tell(new PlaceResponse(true));
                        return;
                    }
                }
                Logger.Log($"ERROR: Carrier is full, cannot place wafer {msg.Wafer.Id}");
                msg.ReplyTo.Tell(new PlaceResponse(false));
            });

            Receive<GetStateRequest>(msg =>
            {
                bool hasNPW = false;
                foreach (var w in _wafers)
                {
                    if (w != null && !w.IsProcessed)
                    {
                        hasNPW = true;
                        break;
                    }
                }

                Sender.Tell(new StateResponse("Carrier", hasNPW, false));
            });

            Receive<CheckAllProcessedRequest>(msg =>
            {
                bool allProcessed = true;
                foreach (var w in _wafers)
                {
                    if (w == null || !w.IsProcessed)
                    {
                        allProcessed = false;
                        break;
                    }
                }
                Sender.Tell(new CheckAllProcessedResponse(allProcessed));
            });
        }

        public static Props Props(int waferCount = Config.W_COUNT) =>
            Akka.Actor.Props.Create(() => new CarrierActor(waferCount));
    }

    // Additional messages for Carrier
    public record CheckAllProcessedRequest(IActorRef ReplyTo);
    public record CheckAllProcessedResponse(bool AllProcessed);
}
