using Akka.Actor;
using CMPSimXS2.Console.Models;
using XStateNet2.Core.Messages;
using LoggerHelper;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// Array-optimized XState robot scheduler using byte indices for O(1) state transitions.
/// Combines array-based state machine with full transfer management functionality.
/// ACTOR-BASED: No locks, uses Akka.NET mailbox for thread-safe message processing.
/// </summary>
public class RobotSchedulerXStateArray : IRobotScheduler
{
    private readonly IActorRef _actor;
    private readonly ArraySchedulerContext _context;

    public RobotSchedulerXStateArray(ActorSystem actorSystem, string? actorName = null)
    {
        _context = new ArraySchedulerContext();

        var props = Props.Create(() => new ArraySchedulerActor(_context));
        _actor = actorSystem.ActorOf(props, actorName ?? $"array-scheduler-{Guid.NewGuid():N}");
    }

    #region IRobotScheduler Implementation

    public void RegisterRobot(string robotId, IActorRef robotActor)
    {
        _actor.Tell(new RegisterRobotMsg(robotId, robotActor));
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

    #region Message Types (compile-time byte constants)

    private record RegisterRobotMsg(string RobotId, IActorRef RobotActor);
    private record UpdateStateMsg(string RobotId, string State, int? HeldWaferId, string? WaitingFor);
    private record RequestTransferMsg(TransferRequest Request);

    #endregion

    #region Actor Implementation

    /// <summary>
    /// Internal actor that processes events using array-based state machine.
    /// Single-threaded mailbox = no locks needed!
    /// </summary>
    private class ArraySchedulerActor : ReceiveActor
    {
        // State machine constants (compile-time byte indices)
        private const byte STATE_IDLE = 0;
        private const byte STATE_PROCESSING = 1;

        private const byte EVENT_REGISTER_ROBOT = 0;
        private const byte EVENT_UPDATE_STATE = 1;
        private const byte EVENT_REQUEST_TRANSFER = 2;

        // Current state
        private byte _currentState = STATE_IDLE;

        // Context data
        private readonly ArraySchedulerContext _context;

        public ArraySchedulerActor(ArraySchedulerContext context)
        {
            _context = context;

            // Message handlers - map messages to byte event IDs
            Receive<RegisterRobotMsg>(msg => ProcessEvent(EVENT_REGISTER_ROBOT, msg));
            Receive<UpdateStateMsg>(msg => ProcessEvent(EVENT_UPDATE_STATE, msg));
            Receive<RequestTransferMsg>(msg => ProcessEvent(EVENT_REQUEST_TRANSFER, msg));
        }

        // Array-based event processing (O(1) jump table)
        private void ProcessEvent(byte eventId, object? data)
        {
            // Switch compiles to jump table for O(1) dispatch
            switch (_currentState)
            {
                case STATE_IDLE:
                    HandleIdleState(eventId, data);
                    break;
                case STATE_PROCESSING:
                    HandleProcessingState(eventId, data);
                    break;
            }
        }

        private void HandleIdleState(byte eventId, object? data)
        {
            switch (eventId)
            {
                case EVENT_REGISTER_ROBOT:
                    ExecuteRegisterRobot(data);
                    break;

                case EVENT_UPDATE_STATE:
                    // Transition to processing (per state machine JSON)
                    _currentState = STATE_PROCESSING;
                    ExecuteUpdateState(data);
                    ExecuteProcessTransfers();
                    CheckAlwaysTransitions();
                    break;

                case EVENT_REQUEST_TRANSFER:
                    // Transition to processing
                    _currentState = STATE_PROCESSING;
                    ExecuteQueueOrAssignTransfer(data);
                    ExecuteProcessTransfers();
                    CheckAlwaysTransitions();
                    break;
            }
        }

        private void HandleProcessingState(byte eventId, object? data)
        {
            switch (eventId)
            {
                case EVENT_REGISTER_ROBOT:
                    ExecuteRegisterRobot(data);
                    break;

                case EVENT_UPDATE_STATE:
                    ExecuteUpdateState(data);
                    break;

                case EVENT_REQUEST_TRANSFER:
                    ExecuteQueueOrAssignTransfer(data);
                    break;
            }

            // Always try to process after any event
            ExecuteProcessTransfers();
            CheckAlwaysTransitions();
        }

        private void CheckAlwaysTransitions()
        {
            // Guard: hasNoPendingWork
            if (_context.PendingRequests.Count == 0 && _currentState == STATE_PROCESSING)
            {
                // Transition back to idle
                _currentState = STATE_IDLE;
            }
        }

        // Action implementations with full functionality

        private void ExecuteRegisterRobot(object? data)
        {
            if (data is RegisterRobotMsg msg)
            {
                _context.Robots[msg.RobotId] = msg.RobotActor;
                _context.RobotStates[msg.RobotId] = new ArrayRobotState();
                Logger.Instance.Log($"[RobotSchedulerXStateArray] Registered robot: {msg.RobotId}");
            }
        }

        private void ExecuteUpdateState(object? data)
        {
            if (data is not UpdateStateMsg msg)
                return;

            if (!_context.RobotStates.TryGetValue(msg.RobotId, out var robotState))
                return;

            var state = msg.State;
            var heldWaferId = msg.HeldWaferId;
            var waitingFor = msg.WaitingFor;

            // Enforce rule: idle robot cannot hold wafer
            if (state == "idle" && heldWaferId.HasValue)
            {
                Logger.Instance.Log($"[RobotSchedulerXStateArray:WARNING] {msg.RobotId} cannot be idle while holding wafer {heldWaferId}! Clearing wafer.");
                heldWaferId = null;
            }

            var wasIdle = robotState.State == "idle";
            robotState.State = state;
            robotState.HeldWaferId = heldWaferId;
            robotState.WaitingFor = waitingFor;

            Logger.Instance.Log($"[RobotSchedulerXStateArray:DEBUG] Robot {msg.RobotId} state updated: {state} (wafer={heldWaferId ?? 0})");

            // Complete active transfer if robot became idle
            if (state == "idle" && !wasIdle && _context.ActiveTransfers.ContainsKey(msg.RobotId))
            {
                var completedTransfer = _context.ActiveTransfers[msg.RobotId];
                _context.ActiveTransfers.Remove(msg.RobotId);

                completedTransfer.OnCompleted?.Invoke(completedTransfer.WaferId);
                Logger.Instance.Log($"[RobotSchedulerXStateArray] {msg.RobotId} completed transfer of wafer {completedTransfer.WaferId}");
            }

            // Process pending transfers when a robot becomes idle
            if (state == "idle" && !wasIdle)
            {
                ExecuteProcessTransfers();
            }
        }

        private void ExecuteQueueOrAssignTransfer(object? data)
        {
            if (data is not RequestTransferMsg msg)
                return;

            var request = msg.Request;

            // Validate request
            try
            {
                request.Validate();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"[RobotSchedulerXStateArray:ERROR] Invalid transfer request: {ex.Message}");
                return;
            }

            Logger.Instance.Log($"[RobotSchedulerXStateArray] Transfer requested: {request}");

            // Try immediate assignment
            var assignedRobot = TryAssignTransfer(request);
            if (assignedRobot == null)
            {
                _context.PendingRequests.Enqueue(request);
                Logger.Instance.Log($"[RobotSchedulerXStateArray] Queued: {request} (Queue size: {_context.PendingRequests.Count})");
            }
        }

        private void ExecuteProcessTransfers()
        {
            // Process pending requests if any
            while (_context.PendingRequests.Count > 0)
            {
                var request = _context.PendingRequests.Peek();
                var assignedRobot = TryAssignTransfer(request);

                if (assignedRobot != null)
                {
                    _context.PendingRequests.Dequeue();
                    Logger.Instance.Log($"[RobotSchedulerXStateArray] Processed pending request: {request} assigned to {assignedRobot}");
                }
                else
                {
                    break; // No robots available
                }
            }
        }

        // Helper methods for robot selection and transfer execution

        private string? TryAssignTransfer(TransferRequest request)
        {
            // Check preferred robot
            if (!string.IsNullOrEmpty(request.PreferredRobotId))
            {
                if (IsRobotAvailable(request.PreferredRobotId))
                {
                    ExecuteTransfer(request.PreferredRobotId, request);
                    return request.PreferredRobotId;
                }
            }

            // Select nearest robot using inline strategy
            var selectedRobot = SelectNearestRobot(request.From, request.To);
            if (selectedRobot != null && IsRobotAvailable(selectedRobot))
            {
                ExecuteTransfer(selectedRobot, request);
                return selectedRobot;
            }

            // Fallback: first available
            var availableRobot = SelectFirstAvailable();
            if (availableRobot != null)
            {
                ExecuteTransfer(availableRobot, request);
                return availableRobot;
            }

            return null;
        }

        private string? SelectNearestRobot(string from, string to)
        {
            // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
            if ((from == "Carrier" && to == "Polisher") || (from == "Buffer" && to == "Carrier"))
                return IsRobotAvailable("Robot 1") ? "Robot 1" : null;

            // R2: Polisher ↔ Cleaner
            if ((from == "Polisher" && to == "Cleaner") || (from == "Cleaner" && to == "Polisher"))
                return IsRobotAvailable("Robot 2") ? "Robot 2" : null;

            // R3: Cleaner ↔ Buffer
            if ((from == "Cleaner" && to == "Buffer") || (from == "Buffer" && to == "Cleaner"))
                return IsRobotAvailable("Robot 3") ? "Robot 3" : null;

            return null;
        }

        private string? SelectFirstAvailable()
        {
            foreach (var kvp in _context.RobotStates)
            {
                if (IsRobotAvailable(kvp.Key))
                    return kvp.Key;
            }
            return null;
        }

        private bool IsRobotAvailable(string robotId)
        {
            if (!_context.RobotStates.ContainsKey(robotId))
                return false;

            var robotState = _context.RobotStates[robotId];
            if (robotState.State != "idle")
                return false;

            if (robotState.HeldWaferId.HasValue)
            {
                Logger.Instance.Log($"[RobotSchedulerXStateArray:WARNING] {robotId} is idle but holding wafer {robotState.HeldWaferId}! Clearing...");
                robotState.HeldWaferId = null;
            }

            return true;
        }

        private void ExecuteTransfer(string robotId, TransferRequest request)
        {
            if (!_context.Robots.ContainsKey(robotId))
            {
                Logger.Instance.Log($"[RobotSchedulerXStateArray:ERROR] Robot {robotId} not found");
                return;
            }

            Logger.Instance.Log($"[RobotSchedulerXStateArray] Assigning {robotId} for transfer: {request}");

            // Update robot state - CRITICAL FIX!
            _context.RobotStates[robotId].State = "busy";
            _context.RobotStates[robotId].HeldWaferId = request.WaferId;

            // Store active transfer - CRITICAL FIX!
            _context.ActiveTransfers[robotId] = request;

            // Send PICKUP event with all required data
            var pickupData = new Dictionary<string, object>
            {
                ["waferId"] = request.WaferId,
                ["wafer"] = request.WaferId,  // Some robots expect "wafer"
                ["from"] = request.From,
                ["to"] = request.To
            };

            _context.Robots[robotId].Tell(new SendEvent("PICKUP", pickupData));
            Logger.Instance.Log($"[RobotSchedulerXStateArray] Sent PICKUP to {robotId}: wafer {request.WaferId} from {request.From} to {request.To}");
        }
    }

    #endregion
}

/// <summary>
/// Shared context for array scheduler state (used by both public API and actor)
/// </summary>
internal class ArraySchedulerContext
{
    public Dictionary<string, IActorRef> Robots { get; } = new();
    public Dictionary<string, ArrayRobotState> RobotStates { get; } = new();
    public Queue<TransferRequest> PendingRequests { get; } = new();
    public Dictionary<string, TransferRequest> ActiveTransfers { get; } = new();  // CRITICAL FIX!
}

internal class ArrayRobotState
{
    public string State { get; set; } = "idle";
    public int? HeldWaferId { get; set; }
    public string? WaitingFor { get; set; }
}
