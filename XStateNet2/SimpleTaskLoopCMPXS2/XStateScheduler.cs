using Akka.Actor;

namespace SimpleTaskLoopCMPXS2
{
    /// <summary>
    /// XStateNet2-based Task Loop Scheduler
    /// Uses actor-based state machines for robots and stations
    /// Each robot follows task-loop logic coordinated through actors
    /// </summary>
    public class XStateScheduler
    {
        private readonly ActorSystem _actorSystem;
        private readonly IActorRef _carrier;
        private readonly IActorRef _polisher;
        private readonly IActorRef _cleaner;
        private readonly IActorRef _buffer;
        private readonly IActorRef _robot1;
        private readonly IActorRef _robot2;
        private readonly IActorRef _robot3;

        private readonly DateTime _startTime;
        private int _totalTransfersCompleted = 0;
        private int _totalPrePositions = 0;
        private CancellationTokenSource? _cts;

        // Track robot states locally for scheduler logic
        private class RobotStateTracker
        {
            public bool HasWafer { get; set; }
            public bool IsProcessed { get; set; }
        }

        private readonly Dictionary<string, RobotStateTracker> _robotStates = new()
        {
            ["R1"] = new RobotStateTracker(),
            ["R2"] = new RobotStateTracker(),
            ["R3"] = new RobotStateTracker()
        };

        public XStateScheduler(ActorSystem actorSystem, int waferCount)
        {
            _actorSystem = actorSystem;
            _startTime = DateTime.UtcNow;

            // Create station actors
            _carrier = actorSystem.ActorOf(CarrierActor.Props(waferCount), "carrier");
            _buffer = actorSystem.ActorOf(BufferStationActor.Props("Buffer"), "buffer");

            // Create process station actors with their processing logic
            _polisher = actorSystem.ActorOf(
                ProcessStationActor.Props("Polisher", async (wafer) => {
                    await Task.Delay(1000); // Simulation time
                    wafer.SetPolished();
                }), "polisher");

            _cleaner = actorSystem.ActorOf(
                ProcessStationActor.Props("Cleaner", async (wafer) => {
                    await Task.Delay(1000); // Simulation time
                    wafer.SetCleaned();
                }), "cleaner");

            // Create robot actors
            _robot1 = actorSystem.ActorOf(RobotActor.Props("R1"), "robot1");
            _robot2 = actorSystem.ActorOf(RobotActor.Props("R2"), "robot2");
            _robot3 = actorSystem.ActorOf(RobotActor.Props("R3"), "robot3");

            Logger.Log("[XStateScheduler] Scheduler initialized");
            Logger.Log("[XStateScheduler]   R1: Carrier ↔ Polisher, Buffer → Carrier");
            Logger.Log("[XStateScheduler]   R2: Polisher → Cleaner");
            Logger.Log("[XStateScheduler]   R3: Cleaner → Buffer");
        }

        public async Task StartAsync()
        {
            Logger.Log("[XStateScheduler] Starting scheduler...");

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Start robot task loops
            var r1Task = R1TaskLoop(token);
            var r2Task = R2TaskLoop(token);
            var r3Task = R3TaskLoop(token);

            Logger.Log("[XStateScheduler] All task loops started");

            // Wait for completion
            while (!await IsAllProcessedAsync() && !token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
            }
        }

        public void Stop()
        {
            Logger.Log($"[XStateScheduler] Stopping scheduler (Transfers: {_totalTransfersCompleted}, Pre-positions: {_totalPrePositions})");
            _cts?.Cancel();
        }

        public void PrintStatistics()
        {
            var runtime = (DateTime.UtcNow - _startTime).TotalSeconds;
            Logger.Log($"");
            Logger.Log($"=== XState Scheduler Statistics ===");
            Logger.Log($"Runtime: {runtime:F1} seconds");
            Logger.Log($"Total transfers completed: {_totalTransfersCompleted}");
            Logger.Log($"Pre-positioning optimizations: {_totalPrePositions} ({100.0 * _totalPrePositions / Math.Max(1, _totalTransfersCompleted):F1}%)");
            Logger.Log($"Average throughput: {_totalTransfersCompleted / runtime:F2} transfers/sec");
            Logger.Log($"======================================");
        }

        private async Task<bool> IsAllProcessedAsync()
        {
            try
            {
                var response = await _carrier.Ask<CheckAllProcessedResponse>(
                    new CheckAllProcessedRequest(ActorRefs.NoSender),
                    TimeSpan.FromSeconds(1));
                return response.AllProcessed;
            }
            catch
            {
                return false;
            }
        }

        private async Task<(string State, bool HasWafer, bool IsProcessed)> GetStationStateAsync(IActorRef station)
        {
            try
            {
                var response = await station.Ask<StateResponse>(
                    new GetStateRequest(ActorRefs.NoSender),
                    TimeSpan.FromMilliseconds(500));
                return (response.State, response.HasWafer, response.IsProcessed);
            }
            catch
            {
                return ("Unknown", false, false);
            }
        }

        private async Task<bool> RobotMoveToAsync(IActorRef robot, IActorRef station, string stationName)
        {
            try
            {
                var response = await robot.Ask<MoveToResponse>(
                    new MoveToRequest(station, stationName, robot),
                    TimeSpan.FromSeconds(2));
                return response.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> RobotMoveToHomeAsync(IActorRef robot)
        {
            try
            {
                var response = await robot.Ask<MoveToHomeResponse>(
                    new MoveToHomeRequest(robot),
                    TimeSpan.FromSeconds(2));
                return response.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task<Wafer?> RobotPickAsync(IActorRef robot, IActorRef station, string robotName)
        {
            try
            {
                // Request station to give wafer
                var pickResponse = await station.Ask<PickResponse>(
                    new PickRequest(station),
                    TimeSpan.FromSeconds(1));

                if (pickResponse.Wafer != null)
                {
                    // Update local tracker
                    _robotStates[robotName].HasWafer = true;
                    _robotStates[robotName].IsProcessed = pickResponse.Wafer.IsProcessed;
                }

                return pickResponse.Wafer;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> RobotPlaceAsync(IActorRef robot, IActorRef station, Wafer wafer, string robotName)
        {
            try
            {
                var placeResponse = await station.Ask<PlaceResponse>(
                    new PlaceRequest(wafer, station),
                    TimeSpan.FromSeconds(1));

                if (placeResponse.Success)
                {
                    // Update local tracker
                    _robotStates[robotName].HasWafer = false;
                    _robotStates[robotName].IsProcessed = false;
                }

                return placeResponse.Success;
            }
            catch
            {
                return false;
            }
        }

        // R1 Task Loop: Carrier ↔ Polisher, Buffer → Carrier
        private async Task R1TaskLoop(CancellationToken token)
        {
            Wafer? currentWafer = null;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var bufferState = await GetStationStateAsync(_buffer);
                    var carrierState = await GetStationStateAsync(_carrier);

                    // Priority 1: If R1 is empty AND Buffer has processed wafer → Pick from Buffer
                    if (currentWafer == null && bufferState.HasWafer && bufferState.IsProcessed)
                    {
                        await RobotMoveToAsync(_robot1, _buffer, "Buffer");
                        currentWafer = await RobotPickAsync(_robot1, _buffer, "R1");
                        if (currentWafer != null)
                        {
                            Interlocked.Increment(ref _totalTransfersCompleted);
                        }
                    }

                    // Priority 2: If R1 has processed wafer → Place on Carrier
                    else if (currentWafer != null && currentWafer.IsProcessed)
                    {
                        await RobotMoveToAsync(_robot1, _carrier, "Carrier");
                        await RobotPlaceAsync(_robot1, _carrier, currentWafer, "R1");
                        currentWafer = null;
                        await RobotMoveToHomeAsync(_robot1);
                    }

                    // Priority 3: If R1 is empty AND Carrier has unprocessed wafer → Pick from Carrier
                    else if (currentWafer == null && carrierState.HasWafer)
                    {
                        await RobotMoveToAsync(_robot1, _carrier, "Carrier");
                        currentWafer = await RobotPickAsync(_robot1, _carrier, "R1");
                        if (currentWafer != null)
                        {
                            Logger.Log($"[R1] Picked wafer {currentWafer.Id}, IsProcessed={currentWafer.IsProcessed}");
                            Interlocked.Increment(ref _totalTransfersCompleted);
                        }
                    }

                    // Priority 4: If R1 has unprocessed wafer → Wait for Polisher and Place
                    else if (currentWafer != null && !currentWafer.IsProcessed)
                    {
                        Logger.Log($"[R1] Has wafer {currentWafer.Id}, waiting for Polisher...");
                        var polisherState = await GetStationStateAsync(_polisher);
                        Logger.Log($"[R1] Polisher state: {polisherState.State}");

                        // Wait while Polisher is not empty
                        while (polisherState.State != "Empty" && !token.IsCancellationRequested)
                        {
                            await Task.Delay(50, token);
                            polisherState = await GetStationStateAsync(_polisher);
                        }

                        // Place wafer on Polisher when empty
                        if (polisherState.State == "Empty")
                        {
                            Logger.Log($"[R1] Placing wafer {currentWafer.Id} on Polisher");
                            await RobotMoveToAsync(_robot1, _polisher, "Polisher");
                            await RobotPlaceAsync(_robot1, _polisher, currentWafer, "R1");
                            currentWafer = null;
                            await RobotMoveToHomeAsync(_robot1);
                        }
                    }

                    await Task.Delay(10, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // R2 Task Loop: Polisher → Cleaner
        private async Task R2TaskLoop(CancellationToken token)
        {
            Wafer? currentWafer = null;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var polisherState = await GetStationStateAsync(_polisher);
                    var cleanerState = await GetStationStateAsync(_cleaner);

                    // If R2 is empty → Pick from Polisher when Done
                    if (currentWafer == null && polisherState.State == "Done")
                    {
                        await RobotMoveToAsync(_robot2, _polisher, "Polisher");
                        currentWafer = await RobotPickAsync(_robot2, _polisher, "R2");
                        if (currentWafer != null)
                        {
                            Interlocked.Increment(ref _totalTransfersCompleted);
                        }
                    }

                    // If R2 has wafer → Wait for Cleaner and Place
                    if (currentWafer != null)
                    {
                        // Wait while Cleaner is not empty
                        while (currentWafer != null && !token.IsCancellationRequested)
                        {
                            cleanerState = await GetStationStateAsync(_cleaner);
                            if (cleanerState.State == "Empty")
                                break;
                            await Task.Delay(50, token);
                        }

                        // Place wafer on Cleaner when empty
                        if (currentWafer != null && cleanerState.State == "Empty")
                        {
                            await RobotMoveToAsync(_robot2, _cleaner, "Cleaner");
                            await RobotPlaceAsync(_robot2, _cleaner, currentWafer, "R2");
                            currentWafer = null;
                            await RobotMoveToHomeAsync(_robot2);
                        }
                    }

                    await Task.Delay(10, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // R3 Task Loop: Cleaner → Buffer
        private async Task R3TaskLoop(CancellationToken token)
        {
            Wafer? currentWafer = null;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var cleanerState = await GetStationStateAsync(_cleaner);
                    var bufferState = await GetStationStateAsync(_buffer);

                    // If R3 is empty → Pick from Cleaner when Done
                    if (currentWafer == null && cleanerState.State == "Done")
                    {
                        await RobotMoveToAsync(_robot3, _cleaner, "Cleaner");
                        currentWafer = await RobotPickAsync(_robot3, _cleaner, "R3");
                        if (currentWafer != null)
                        {
                            Interlocked.Increment(ref _totalTransfersCompleted);
                        }
                    }

                    // If R3 has wafer → Wait for Buffer and Place
                    if (currentWafer != null)
                    {
                        // Wait while Buffer is not empty
                        while (currentWafer != null && !token.IsCancellationRequested)
                        {
                            bufferState = await GetStationStateAsync(_buffer);
                            if (bufferState.State == "Empty")
                                break;
                            await Task.Delay(50, token);
                        }

                        // Place wafer on Buffer when empty
                        if (currentWafer != null && bufferState.State == "Empty")
                        {
                            await RobotMoveToAsync(_robot3, _buffer, "Buffer");
                            await RobotPlaceAsync(_robot3, _buffer, currentWafer, "R3");
                            currentWafer = null;
                            await RobotMoveToHomeAsync(_robot3);
                        }
                    }

                    await Task.Delay(10, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
