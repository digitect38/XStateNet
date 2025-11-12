using Akka.Actor;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Factory;

namespace SimpleTaskLoopCMPXS2
{
    /// <summary>
    /// Robot actor using XStateNet2 state machine
    /// States: Empty, HasNPW, HasPW, Moving, Picking, Placing
    /// </summary>
    public class RobotActor : ReceiveActor
    {
        private readonly IActorRef _machine;
        private Wafer? _wafer;
        private IActorRef? _currentStation;

        // XState machine JSON definition for Robot
        private const string MachineJson = @"{
            ""id"": ""robot"",
            ""initial"": ""empty"",
            ""states"": {
                ""empty"": {
                    ""on"": {
                        ""MOVE"": ""moving"",
                        ""PICK_START"": ""picking""
                    }
                },
                ""moving"": {
                    ""on"": {
                        ""MOVE_COMPLETE"": [
                            { ""target"": ""empty"", ""cond"": ""isEmpty"" },
                            { ""target"": ""hasNPW"", ""cond"": ""hasNPW"" },
                            { ""target"": ""hasPW"", ""cond"": ""hasPW"" }
                        ]
                    }
                },
                ""picking"": {
                    ""on"": {
                        ""PICK_COMPLETE"": [
                            { ""target"": ""hasNPW"", ""cond"": ""hasNPW"" },
                            { ""target"": ""hasPW"", ""cond"": ""hasPW"" },
                            { ""target"": ""empty"", ""cond"": ""isEmpty"" }
                        ]
                    }
                },
                ""hasNPW"": {
                    ""on"": {
                        ""MOVE"": ""moving"",
                        ""PLACE_START"": ""placing""
                    }
                },
                ""hasPW"": {
                    ""on"": {
                        ""MOVE"": ""moving"",
                        ""PLACE_START"": ""placing""
                    }
                },
                ""placing"": {
                    ""on"": {
                        ""PLACE_COMPLETE"": ""empty""
                    }
                }
            }
        }";

        public string Name { get; }

        public RobotActor(string name)
        {
            Name = name;

            // Create XState machine
            var factory = new XStateMachineFactory(Context.System);
            _machine = factory.FromJson(MachineJson)
                .WithGuard("isEmpty", (ctx, data) => _wafer == null)
                .WithGuard("hasNPW", (ctx, data) => _wafer != null && !_wafer.IsProcessed)
                .WithGuard("hasPW", (ctx, data) => _wafer != null && _wafer.IsProcessed)
                .BuildAndStart();

            Become(Ready);
        }

        private void Ready()
        {
            Receive<MoveToRequest>(msg =>
            {
                _machine.Tell(new SendEvent("MOVE"));
                PerformMove(msg);
            });

            Receive<MoveToHomeRequest>(msg =>
            {
                _machine.Tell(new SendEvent("MOVE"));
                PerformMoveHome(msg);
            });

            Receive<PickFromRequest>(msg =>
            {
                _machine.Tell(new SendEvent("PICK_START"));
                PerformPick(msg);
            });

            Receive<PlaceToRequest>(msg =>
            {
                _machine.Tell(new SendEvent("PLACE_START"));
                PerformPlace(msg);
            });

            Receive<GetRobotStateRequest>(msg =>
            {
                var state = _wafer == null ? "Empty" :
                           _wafer.IsProcessed ? "HasPW" : "HasNPW";
                msg.ReplyTo.Tell(new RobotStateResponse(state, _wafer != null, _wafer?.IsProcessed ?? false, _currentStation));
            });

            Receive<MoveComplete>(msg =>
            {
                _currentStation = msg.TargetStation;
                _machine.Tell(new SendEvent("MOVE_COMPLETE"));
                Logger.Log($"{Name} arrived at {msg.StationName}");
                msg.ReplyTo.Tell(new MoveToResponse(true));
            });

            Receive<MoveHomeComplete>(msg =>
            {
                _machine.Tell(new SendEvent("MOVE_COMPLETE"));
                Logger.Log($"{Name} arrived at Home");
                msg.ReplyTo.Tell(new MoveToHomeResponse(true));
            });

            Receive<PickComplete>(msg =>
            {
                // Ask station for wafer
                msg.FromStation.Tell(new PickRequest(Self));
            });

            Receive<PickResponse>(msg =>
            {
                _wafer = msg.Wafer;
                _machine.Tell(new SendEvent("PICK_COMPLETE"));

                if (msg.Wafer != null)
                    Logger.Log($"{Name} picked wafer {msg.Wafer.Id}");
            });

            Receive<PlaceComplete>(msg =>
            {
                if (_wafer != null)
                {
                    var wafer = _wafer;
                    _wafer = null;
                    msg.ToStation.Tell(new PlaceRequest(wafer, Self));
                }
                else
                {
                    _machine.Tell(new SendEvent("PLACE_COMPLETE"));
                }
            });

            Receive<PlaceResponse>(msg =>
            {
                _machine.Tell(new SendEvent("PLACE_COMPLETE"));
            });
        }

        private void PerformMove(MoveToRequest msg)
        {
            var targetStation = msg.TargetStation;

            // Skip if already at target
            if (_currentStation == targetStation)
            {
                _machine.Tell(new SendEvent("MOVE_COMPLETE"));
                msg.ReplyTo.Tell(new MoveToResponse(true));
                return;
            }

            Logger.Log($"{Name} moving to {msg.StationName}...");

            // Schedule move completion (100ms = 1 second in simulation)
            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromMilliseconds(100),
                Self,
                new MoveComplete(targetStation, msg.StationName, msg.ReplyTo),
                Self);
        }

        private void PerformMoveHome(MoveToHomeRequest msg)
        {
            _currentStation = null;
            Logger.Log($"{Name} moving to Home...");

            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromMilliseconds(100),
                Self,
                new MoveHomeComplete(msg.ReplyTo),
                Self);
        }

        private void PerformPick(PickFromRequest msg)
        {
            // Schedule pick operation (50ms = 0.5 second)
            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromMilliseconds(50),
                Self,
                new PickComplete(msg.FromStation, msg.ReplyTo),
                Self);
        }

        private void PerformPlace(PlaceToRequest msg)
        {
            // Schedule place operation (50ms = 0.5 second)
            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromMilliseconds(50),
                Self,
                new PlaceComplete(msg.ToStation, msg.ReplyTo),
                Self);
        }

        // Note: These are called within the Ready() method with Receive<T> calls

        public static Props Props(string name) => Akka.Actor.Props.Create(() => new RobotActor(name));
    }

    // Robot-specific messages
    public record MoveToRequest(IActorRef TargetStation, string StationName, IActorRef ReplyTo);
    public record MoveToResponse(bool Success);
    public record MoveToHomeRequest(IActorRef ReplyTo);
    public record MoveToHomeResponse(bool Success);
    public record PickFromRequest(IActorRef FromStation, IActorRef ReplyTo);
    public record PickFromResponse(bool Success, Wafer? Wafer);
    public record PlaceToRequest(IActorRef ToStation, IActorRef ReplyTo);
    public record PlaceToResponse(bool Success);
    public record GetRobotStateRequest(IActorRef ReplyTo);
    public record RobotStateResponse(string State, bool HasWafer, bool IsProcessed, IActorRef? CurrentStation);

    // Internal messages
    internal record MoveComplete(IActorRef TargetStation, string StationName, IActorRef ReplyTo);
    internal record MoveHomeComplete(IActorRef ReplyTo);
    internal record PickComplete(IActorRef FromStation, IActorRef ReplyTo);
    internal record PlaceComplete(IActorRef ToStation, IActorRef ReplyTo);
}
