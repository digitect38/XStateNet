using Akka.Actor;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Factory;

namespace SimpleTaskLoopCMPXS2
{
    /// <summary>
    /// Process station actor (Polisher/Cleaner) using XStateNet2 state machine
    /// States: Empty, Idle, Processing, AlmostDone, Done
    /// </summary>
    public class ProcessStationActor : ReceiveActor
    {
        private readonly IActorRef _machine;
        private Wafer? _wafer;
        private ICancelable? _processingTask;
        private ICancelable? _almostDoneTask;
        private DateTime _processStartTime;
        private readonly int _expectedProcessTimeMs = 1000;

        // XState machine JSON definition
        private const string MachineJson = @"{
            ""id"": ""processStation"",
            ""initial"": ""empty"",
            ""states"": {
                ""empty"": {
                    ""on"": {
                        ""PLACE"": {
                            ""target"": ""idle"",
                            ""actions"": [""onPlace""]
                        }
                    }
                },
                ""idle"": {
                    ""on"": {
                        ""START_PROCESS"": {
                            ""target"": ""processing"",
                            ""actions"": [""onStartProcess""]
                        }
                    }
                },
                ""processing"": {
                    ""on"": {
                        ""ALMOST_DONE"": {
                            ""target"": ""almostDone"",
                            ""actions"": [""onAlmostDone""]
                        }
                    }
                },
                ""almostDone"": {
                    ""on"": {
                        ""COMPLETE"": {
                            ""target"": ""done"",
                            ""actions"": [""onComplete""]
                        }
                    }
                },
                ""done"": {
                    ""on"": {
                        ""PICK"": {
                            ""target"": ""empty"",
                            ""actions"": [""onPick""]
                        }
                    }
                }
            }
        }";

        public string Name { get; }

        public ProcessStationActor(string name, Func<Wafer, Task> processAction)
        {
            Name = name;
            _processAction = processAction;

            // Create XState machine
            var factory = new XStateMachineFactory(Context.System);
            _machine = factory.FromJson(MachineJson)
                .WithAction("onPlace", (ctx, data) => { })
                .WithAction("onStartProcess", (ctx, data) => OnStartProcessAction())
                .WithAction("onAlmostDone", (ctx, data) => { })
                .WithAction("onComplete", (ctx, data) => { })
                .WithAction("onPick", (ctx, data) => { })
                .BuildAndStart();

            Become(Ready);
        }

        private Func<Wafer, Task> _processAction;

        private void Ready()
        {
            Receive<PlaceRequest>(msg =>
            {
                if (_wafer == null)
                {
                    _wafer = msg.Wafer;
                    _machine.Tell(new SendEvent("PLACE"));
                    Logger.Log($"Wafer {msg.Wafer.Id} placed on {Name} for processing");
                    Sender.Tell(new PlaceResponse(true));

                    // Automatically trigger processing
                    Self.Tell(new StartProcessing());
                }
                else
                {
                    Logger.Log($"ERROR: Cannot place wafer {msg.Wafer.Id} on {Name} - Station is busy with wafer {_wafer.Id}");
                    Sender.Tell(new PlaceResponse(false));
                }
            });

            Receive<StartProcessing>(msg =>
            {
                if (_wafer != null)
                {
                    _machine.Tell(new SendEvent("START_PROCESS"));
                    _processStartTime = DateTime.Now;
                    Logger.Log($"{Name} starting to process wafer {_wafer.Id}");

                    // Schedule AlmostDone after 800ms (80% of 1000ms)
                    _almostDoneTask = Context.System.Scheduler.ScheduleTellOnceCancelable(
                        TimeSpan.FromMilliseconds(800),
                        Self,
                        new ProcessingAlmostDone(),
                        Self);

                    // Start actual processing task
                    StartProcessingTask();
                }
            });

            Receive<ProcessingAlmostDone>(msg =>
            {
                _machine.Tell(new SendEvent("ALMOST_DONE"));
                Logger.Log($"{Name} processing wafer {_wafer?.Id}: 80% done (AlmostDone)");
            });

            Receive<ProcessingComplete>(msg =>
            {
                if (_wafer != null)
                {
                    _machine.Tell(new SendEvent("COMPLETE"));
                    Logger.Log($"{Name} processing wafer {_wafer.Id}: 100% done");

                    // Check for timeout
                    var elapsed = (DateTime.Now - _processStartTime).TotalMilliseconds;
                    var allowedTime = _expectedProcessTimeMs * 1.5;
                    if (elapsed > allowedTime)
                    {
                        AlarmManager.RaiseAlarm(
                            AlarmLevel.Error,
                            "TIMEOUT",
                            $"{Name} processing timeout: {elapsed:F0}ms (expected: {_expectedProcessTimeMs}ms, wafer: {_wafer.Id})");
                    }
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
                    Sender.Tell(new PickResponse(wafer));
                }
                else
                {
                    Sender.Tell(new PickResponse(null));
                }
            });

            Receive<GetStateRequest>(msg =>
            {
                // Query current XState machine state
                var currentState = GetCurrentStateFromMachine();
                Sender.Tell(new StateResponse(currentState, _wafer != null, _wafer?.IsProcessed ?? false));
            });
        }

        private void OnStartProcessAction()
        {
            // Action triggered when processing starts
        }

        private void StartProcessingTask()
        {
            var wafer = _wafer;
            if (wafer == null) return;

            // Schedule processing complete after 1000ms
            _processingTask = Context.System.Scheduler.ScheduleTellOnceCancelable(
                TimeSpan.FromMilliseconds(1000),
                Self,
                new ProcessingComplete(),
                Self);

            // Run the processing action asynchronously
            Task.Run(async () =>
            {
                try
                {
                    await _processAction(wafer);
                }
                catch (Exception ex)
                {
                    Logger.Log($"ERROR in {Name} processing: {ex.Message}");
                }
            });
        }

        private string GetCurrentStateFromMachine()
        {
            // Since we can't directly query the machine state synchronously,
            // we'll track it ourselves
            if (_wafer == null) return "Empty";
            if (_processingTask != null && !_processingTask.IsCancellationRequested) return "Processing";
            return "Done";
        }

        protected override void PostStop()
        {
            _processingTask?.Cancel();
            _almostDoneTask?.Cancel();
            base.PostStop();
        }

        public static Props Props(string name, Func<Wafer, Task> processAction) =>
            Akka.Actor.Props.Create(() => new ProcessStationActor(name, processAction));
    }

    // Internal message for processing state
    internal record ProcessingAlmostDone();
}
