using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Messages;
using LoggerHelper;
using System.Collections.Concurrent;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// ANT Colony Optimization (ACO) robot scheduler.
/// Uses pheromone trails to learn optimal robot selection patterns over time.
///
/// ACO STRATEGY:
/// - Each transfer route (from→to station) has pheromone trails for each robot
/// - Pheromones increase when transfers succeed quickly
/// - Pheromones evaporate over time
/// - Robot selection is probabilistic based on pheromone strength + heuristic
///
/// HEURISTIC FACTORS:
/// - Robot availability (idle vs busy)
/// - Distance to source station
/// - Current workload
///
/// LEARNING: System adapts to find optimal robot-route assignments automatically.
/// </summary>
public class RobotSchedulerACO : IRobotScheduler
{
    private readonly IActorRef _actor;
    private readonly ACOSchedulerContext _context;

    public RobotSchedulerACO(ActorSystem actorSystem, string? actorName = null)
    {
        Logger.Instance.Log("[ACO:CONSTRUCTOR] RobotSchedulerACO constructor called!");
        _context = new ACOSchedulerContext();

        var props = Props.Create(() => new ACOSchedulerActor(_context));
        _actor = actorSystem.ActorOf(props, actorName ?? $"aco-scheduler-{Guid.NewGuid():N}");
        Logger.Instance.Log($"[ACO:CONSTRUCTOR] Actor created at path: {_actor.Path}");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        Logger.Instance.Log($"[ACO:PUBLIC] RegisterRobot called for {robotId}, sending to actor {_actor.Path}");
        _actor.Tell(new RegisterRobotMsg(robotId, robotActor));
        Logger.Instance.Log($"[ACO:PUBLIC] Message sent to actor");
    }

    public void UpdateRobotState(string robotId, string state, int? heldWaferId = null, string? waitingFor = null)
    {
        _actor.Tell(new UpdateStateMsg(robotId, state, heldWaferId, waitingFor));
    }

    public void RequestTransfer(TransferRequest request)
    {
        _actor.Tell(new RequestTransferMsg(request));
    }

    public int GetQueueSize()
    {
        return _context.PendingRequests.Count;
    }

    public string GetRobotState(string robotId)
    {
        return _context.RobotStates.TryGetValue(robotId, out var state) ? state.State : "unknown";
    }

    #endregion

    #region Message Types

    private record RegisterRobotMsg(string RobotId, IActorRef RobotActor);
    private record UpdateStateMsg(string RobotId, string State, int? HeldWaferId, string? WaitingFor);
    private record RequestTransferMsg(TransferRequest Request);
    private record EvaporatePheromones(); // Periodic pheromone decay

    #endregion

    #region ACO Data Structures

    /// <summary>
    /// Robot state information
    /// </summary>
    private class RobotState
    {
        public string State { get; set; } = "idle"; // idle, busy, carrying
        public int? HeldWaferId { get; set; }
        public string? WaitingFor { get; set; }
    }

    /// <summary>
    /// Pheromone trail for a specific robot on a specific route
    /// </summary>
    private class PheromoneTrail
    {
        public double Strength { get; set; } = 1.0; // Initial pheromone level
        public int SuccessCount { get; set; } = 0;
        public int FailureCount { get; set; } = 0;
        public double AverageCompletionTime { get; set; } = 0.0;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Route identifier (from station → to station)
    /// </summary>
    private record RouteKey(string From, string To);

    /// <summary>
    /// Context data for ACO scheduler
    /// </summary>
    private class ACOSchedulerContext
    {
        // Robot state tracking
        public Dictionary<string, RobotState> RobotStates { get; } = new();
        public Dictionary<string, IActorRef> RobotActors { get; } = new();

        // Transfer queue
        public Queue<TransferRequest> PendingRequests { get; } = new();

        // ACO pheromone trails: Route → Robot → Trail
        public Dictionary<RouteKey, Dictionary<string, PheromoneTrail>> PheromoneMap { get; } = new();

        // Active transfers (for completion tracking)
        public Dictionary<int, ActiveTransfer> ActiveTransfers { get; } = new();

        // Statistics
        public int TotalTransfers { get; set; } = 0;
        public int TotalEvaporations { get; set; } = 0;
    }

    /// <summary>
    /// Tracks an active transfer for completion time measurement
    /// </summary>
    private record ActiveTransfer(
        string RobotId,
        RouteKey Route,
        DateTime StartTime,
        TransferRequest Request
    );

    #endregion

    #region Actor Implementation

    private class ACOSchedulerActor : ReceiveActor
    {
        // ACO Algorithm Parameters
        private const double ALPHA = 1.0;           // Pheromone importance
        private const double BETA = 2.0;            // Heuristic importance
        private const double EVAPORATION_RATE = 0.1; // 10% decay per cycle
        private const double PHEROMONE_DEPOSIT = 1.0;
        private const double MIN_PHEROMONE = 0.1;
        private const double MAX_PHEROMONE = 10.0;

        private readonly ACOSchedulerContext _context;
        private readonly Random _random = new Random();
        private ICancelable? _evaporationTimer;

        public ACOSchedulerActor(ACOSchedulerContext context)
        {
            _context = context;

            Receive<RegisterRobotMsg>(HandleRegisterRobot);
            Receive<UpdateStateMsg>(HandleUpdateState);
            Receive<RequestTransferMsg>(HandleRequestTransfer);
            Receive<EvaporatePheromones>(HandleEvaporation);

            // Start periodic pheromone evaporation (every 100ms)
            _evaporationTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                TimeSpan.FromMilliseconds(1000),
                TimeSpan.FromMilliseconds(1000),
                Self,
                new EvaporatePheromones(),
                ActorRefs.NoSender
            );
        }

        protected override void PostStop()
        {
            _evaporationTimer?.Cancel();
            base.PostStop();
        }

        #region Message Handlers

        private void HandleRegisterRobot(RegisterRobotMsg msg)
        {
            _context.RobotActors[msg.RobotId] = msg.RobotActor;
            _context.RobotStates[msg.RobotId] = new RobotState
            {
                State = "idle",
                HeldWaferId = null,
                WaitingFor = null
            };

            Logger.Instance.Log($"[ACO] Robot registered: {msg.RobotId}");
        }

        private void HandleUpdateState(UpdateStateMsg msg)
        {
            if (!_context.RobotStates.ContainsKey(msg.RobotId))
            {
                _context.RobotStates[msg.RobotId] = new RobotState();
            }

            var oldState = _context.RobotStates[msg.RobotId].State;
            _context.RobotStates[msg.RobotId].State = msg.State;
            _context.RobotStates[msg.RobotId].HeldWaferId = msg.HeldWaferId;
            _context.RobotStates[msg.RobotId].WaitingFor = msg.WaitingFor;

            Logger.Instance.Log($"[ACO] Robot {msg.RobotId}: {oldState} → {msg.State}");

            // Check if transfer completed (robot returns to idle from either busy or carrying)
            if (msg.State == "idle" && (oldState == "busy" || oldState == "carrying"))
            {
                HandleTransferCompletion(msg.RobotId);
            }

            // Try to assign pending requests
            TryAssignPendingRequests();
        }

        private void HandleRequestTransfer(RequestTransferMsg msg)
        {
            _context.PendingRequests.Enqueue(msg.Request);
            Logger.Instance.Log($"[ACO] Transfer requested: W{msg.Request.WaferId} from {msg.Request.From} → {msg.Request.To} (Queue: {_context.PendingRequests.Count})");

            TryAssignPendingRequests();
        }

        private void HandleEvaporation(EvaporatePheromones msg)
        {
            _context.TotalEvaporations++;

            // Evaporate all pheromone trails
            foreach (var route in _context.PheromoneMap.Values)
            {
                foreach (var trail in route.Values)
                {
                    trail.Strength *= (1.0 - EVAPORATION_RATE);
                    if (trail.Strength < MIN_PHEROMONE)
                    {
                        trail.Strength = MIN_PHEROMONE;
                    }
                }
            }
        }

        #endregion

        #region Transfer Assignment Logic

        private void TryAssignPendingRequests()
        {
            while (_context.PendingRequests.Count > 0)
            {
                var request = _context.PendingRequests.Peek();

                // Select robot using ACO probability distribution
                var selectedRobot = SelectRobotUsingACO(request);

                if (selectedRobot == null)
                {
                    Logger.Instance.Log($"[ACO] No available robot for W{request.WaferId}");
                    break;
                }

                // Dequeue and assign
                _context.PendingRequests.Dequeue();

                // Update robot state
                _context.RobotStates[selectedRobot].State = "busy";
                _context.RobotStates[selectedRobot].HeldWaferId = request.WaferId;

                // Track active transfer
                var route = new RouteKey(request.From, request.To);
                _context.ActiveTransfers[request.WaferId] = new ActiveTransfer(
                    selectedRobot,
                    route,
                    DateTime.UtcNow,
                    request
                );

                // Send PICKUP event to robot with all required data
                var robotActor = _context.RobotActors[selectedRobot];
                var pickupData = new Dictionary<string, object>
                {
                    ["waferId"] = request.WaferId,
                    ["wafer"] = request.WaferId,
                    ["from"] = request.From,
                    ["to"] = request.To
                };
                robotActor.Tell(new SendEvent("PICKUP", pickupData));

                _context.TotalTransfers++;

                Logger.Instance.Log($"[ACO] Assigned W{request.WaferId} to {selectedRobot} (Total transfers: {_context.TotalTransfers})");
            }
        }

        /// <summary>
        /// Select robot using ACO probability distribution
        /// P(robot) ∝ (pheromone^α) * (heuristic^β)
        /// </summary>
        private string? SelectRobotUsingACO(TransferRequest request)
        {
            var route = new RouteKey(request.From, request.To);

            // Get available robots (idle state)
            var availableRobots = _context.RobotStates
                .Where(kvp => kvp.Value.State == "idle")
                .Select(kvp => kvp.Key)
                .ToList();

            if (availableRobots.Count == 0)
                return null;

            // Ensure pheromone trails exist for this route
            if (!_context.PheromoneMap.ContainsKey(route))
            {
                _context.PheromoneMap[route] = new Dictionary<string, PheromoneTrail>();
            }

            var routePheromones = _context.PheromoneMap[route];

            // Initialize pheromones for new robots on this route
            foreach (var robot in availableRobots)
            {
                if (!routePheromones.ContainsKey(robot))
                {
                    routePheromones[robot] = new PheromoneTrail();
                }
            }

            // Calculate probabilities for each robot
            var probabilities = new Dictionary<string, double>();
            double totalProbability = 0.0;

            foreach (var robot in availableRobots)
            {
                var trail = routePheromones[robot];

                // Pheromone component
                double pheromone = Math.Pow(trail.Strength, ALPHA);

                // Heuristic component (based on historical performance)
                double heuristic = CalculateHeuristic(robot, request, trail);
                double heuristicPower = Math.Pow(heuristic, BETA);

                // Combined probability
                double probability = pheromone * heuristicPower;

                probabilities[robot] = probability;
                totalProbability += probability;
            }

            if (totalProbability == 0.0)
            {
                // Fallback: random selection
                return availableRobots[_random.Next(availableRobots.Count)];
            }

            // Normalize probabilities
            foreach (var robot in availableRobots)
            {
                probabilities[robot] /= totalProbability;
            }

            // Select robot using roulette wheel selection
            double randomValue = _random.NextDouble();
            double cumulativeProbability = 0.0;

            foreach (var robot in availableRobots)
            {
                cumulativeProbability += probabilities[robot];
                if (randomValue <= cumulativeProbability)
                {
                    return robot;
                }
            }

            // Fallback (should not reach here)
            return availableRobots.Last();
        }

        /// <summary>
        /// Calculate heuristic value for robot selection
        /// Higher values = better choice
        /// </summary>
        private double CalculateHeuristic(string robotId, TransferRequest request, PheromoneTrail trail)
        {
            double heuristic = 1.0;

            // Factor 1: Success rate
            int totalAttempts = trail.SuccessCount + trail.FailureCount;
            if (totalAttempts > 0)
            {
                double successRate = (double)trail.SuccessCount / totalAttempts;
                heuristic *= (1.0 + successRate); // Boost by success rate
            }

            // Factor 2: Average completion time (inverse)
            if (trail.AverageCompletionTime > 0)
            {
                heuristic *= (1.0 / trail.AverageCompletionTime); // Faster = better
            }

            // Factor 3: Recency (prefer recently successful robots)
            double hoursSinceUpdate = (DateTime.UtcNow - trail.LastUpdated).TotalHours;
            if (hoursSinceUpdate < 1.0)
            {
                heuristic *= 1.5; // Boost for recent activity
            }

            return Math.Max(heuristic, 0.1); // Minimum heuristic value
        }

        #endregion

        #region Pheromone Update

        private void HandleTransferCompletion(string robotId)
        {
            // Find the active transfer for this robot
            var activeTransfer = _context.ActiveTransfers.Values
                .FirstOrDefault(t => t.RobotId == robotId);

            if (activeTransfer == null)
                return;

            var route = activeTransfer.Route;
            var completionTime = (DateTime.UtcNow - activeTransfer.StartTime).TotalSeconds;

            // Update pheromone trail
            if (_context.PheromoneMap.TryGetValue(route, out var routePheromones))
            {
                if (routePheromones.TryGetValue(robotId, out var trail))
                {
                    // Deposit pheromone (inversely proportional to completion time)
                    double deposit = PHEROMONE_DEPOSIT / (1.0 + completionTime);
                    trail.Strength += deposit;

                    // Clamp pheromone to max value
                    if (trail.Strength > MAX_PHEROMONE)
                    {
                        trail.Strength = MAX_PHEROMONE;
                    }

                    // Update statistics
                    trail.SuccessCount++;
                    trail.AverageCompletionTime = (trail.AverageCompletionTime * (trail.SuccessCount - 1) + completionTime) / trail.SuccessCount;
                    trail.LastUpdated = DateTime.UtcNow;

                    Logger.Instance.Log($"[ACO] Pheromone updated: {robotId} on {route.From}→{route.To} " +
                                $"(Strength: {trail.Strength:F2}, AvgTime: {trail.AverageCompletionTime:F2}s, " +
                                $"Success: {trail.SuccessCount})");
                }
            }

            // Invoke completion callback to notify wafer journey scheduler
            var waferId = _context.ActiveTransfers.FirstOrDefault(kvp => kvp.Value == activeTransfer).Key;
            if (waferId > 0)
            {
                // Call the OnCompleted callback
                if (activeTransfer.Request.OnCompleted != null)
                {
                    try
                    {
                        activeTransfer.Request.OnCompleted(waferId);
                        Logger.Instance.Log($"[ACO] Transfer completed callback invoked for W{waferId}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Log($"[ACO] Error in completion callback for W{waferId}: {ex.Message}");
                    }
                }

                _context.ActiveTransfers.Remove(waferId);
            }
        }

        #endregion
    }

    #endregion
}
