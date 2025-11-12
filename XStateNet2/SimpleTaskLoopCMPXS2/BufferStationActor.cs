using Akka.Actor;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Factory;

namespace SimpleTaskLoopCMPXS2
{
    /// <summary>
    /// Buffer station actor using XStateNet2 state machine
    /// States: Empty, HasNPW, HasPW
    /// </summary>
    public class BufferStationActor : ReceiveActor
    {
        private readonly IActorRef _machine;
        private Wafer? _wafer;

        // XState machine JSON definition
        private const string MachineJson = @"{
            ""id"": ""bufferStation"",
            ""initial"": ""empty"",
            ""states"": {
                ""empty"": {
                    ""on"": {
                        ""PLACE"": ""hasWafer""
                    }
                },
                ""hasWafer"": {
                    ""on"": {
                        ""PICK"": ""empty""
                    }
                }
            }
        }";

        public string Name { get; }

        public BufferStationActor(string name)
        {
            Name = name;

            // Create XState machine
            var factory = new XStateMachineFactory(Context.System);
            _machine = factory.FromJson(MachineJson)
                .WithAction("onPlace", (ctx, data) => OnPlaceAction(data))
                .WithAction("onPick", (ctx, data) => OnPickAction())
                .BuildAndStart();

            Become(Ready);
        }

        private void Ready()
        {
            Receive<PlaceRequest>(msg =>
            {
                if (_wafer == null)
                {
                    _wafer = msg.Wafer;
                    _machine.Tell(new SendEvent("PLACE"));
                    Logger.Log($"Wafer {msg.Wafer.Id} placed on {Name} (Processed: {msg.Wafer.IsProcessed})");
                    Sender.Tell(new PlaceResponse(true));
                }
                else
                {
                    Logger.Log($"ERROR: Cannot place wafer {msg.Wafer.Id} on {Name} - Station occupied with wafer {_wafer.Id}");
                    Sender.Tell(new PlaceResponse(false));
                }
            });

            Receive<PickRequest>(msg =>
            {
                var wafer = _wafer;
                if (wafer != null)
                {
                    _wafer = null;
                    _machine.Tell(new SendEvent("PICK"));
                    Logger.Log($"Wafer {wafer.Id} picked from {Name}");
                }
                Sender.Tell(new PickResponse(wafer));
            });

            Receive<GetStateRequest>(msg =>
            {
                var state = _wafer == null ? "Empty" :
                           _wafer.IsProcessed ? "HasPW" : "HasNPW";
                Sender.Tell(new StateResponse(state, _wafer != null, _wafer?.IsProcessed ?? false));
            });
        }

        private void OnPlaceAction(object? data)
        {
            // Action callback when wafer is placed
        }

        private void OnPickAction()
        {
            // Action callback when wafer is picked
        }

        public static Props Props(string name) => Akka.Actor.Props.Create(() => new BufferStationActor(name));
    }
}
