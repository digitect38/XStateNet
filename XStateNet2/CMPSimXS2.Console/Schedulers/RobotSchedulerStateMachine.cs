using Akka.Actor;
using CMPSimXS2.Console.Models;

namespace CMPSimXS2.Console.Schedulers;

/// <summary>
/// State Machine definition for Robot Scheduler in JSON format
/// XSTATE VERSION - Declarative state machine approach
/// </summary>
public static class RobotSchedulerStateMachine
{
    /// <summary>
    /// XState JSON definition for Robot Scheduler
    /// States: idle -> processing -> idle
    /// </summary>
    public const string MachineJson = """
    {
      "id": "robotScheduler",
      "initial": "idle",
      "context": {},
      "states": {
        "idle": {
          "description": "Scheduler is idle, waiting for requests or state updates",
          "meta": {
            "color": "green",
            "description": "Ready to process requests"
          },
          "on": {
            "REGISTER_ROBOT": {
              "actions": ["registerRobot"]
            },
            "UPDATE_ROBOT_STATE": {
              "target": "processing",
              "actions": ["updateRobotState"]
            },
            "REQUEST_TRANSFER": {
              "target": "processing",
              "actions": ["queueOrAssignTransfer"]
            }
          }
        },
        "processing": {
          "description": "Processing transfer requests and robot state changes",
          "meta": {
            "color": "yellow",
            "description": "Assigning transfers to robots"
          },
          "entry": ["processTransfers"],
          "on": {
            "REGISTER_ROBOT": {
              "actions": ["registerRobot"]
            },
            "UPDATE_ROBOT_STATE": {
              "actions": ["updateRobotState", "processTransfers"]
            },
            "REQUEST_TRANSFER": {
              "actions": ["queueOrAssignTransfer", "processTransfers"]
            }
          },
          "always": {
            "target": "idle",
            "cond": "hasNoPendingWork"
          }
        }
      }
    }
    """;

    /// <summary>
    /// Context data for the scheduler (maintained in InterpreterContext)
    /// </summary>
    public class SchedulerContext
    {
        public Dictionary<string, IActorRef> Robots { get; set; } = new();
        public Dictionary<string, RobotState> RobotStates { get; set; } = new();
        public Queue<TransferRequest> PendingRequests { get; set; } = new();
        public Dictionary<string, TransferRequest> ActiveTransfers { get; set; } = new();
    }

    public class RobotState
    {
        public string State { get; set; } = "idle";
        public int? HeldWaferId { get; set; }
        public string? WaitingFor { get; set; }
    }

    /// <summary>
    /// Selection strategies for robot allocation
    /// </summary>
    public static class RobotSelectionStrategy
    {
        public static string? SelectNearestRobot(string from, string to, Dictionary<string, RobotState> robotStates)
        {
            // R1: Carrier ↔ Polisher, Buffer ↔ Carrier
            if ((from == "Carrier" && to == "Polisher") || (from == "Buffer" && to == "Carrier"))
                return IsAvailable("Robot 1", robotStates) ? "Robot 1" : null;

            // R2: Polisher ↔ Cleaner
            if ((from == "Polisher" && to == "Cleaner") || (from == "Cleaner" && to == "Polisher"))
                return IsAvailable("Robot 2", robotStates) ? "Robot 2" : null;

            // R3: Cleaner ↔ Buffer
            if ((from == "Cleaner" && to == "Buffer") || (from == "Buffer" && to == "Cleaner"))
                return IsAvailable("Robot 3", robotStates) ? "Robot 3" : null;

            return null;
        }

        public static string? SelectFirstAvailable(Dictionary<string, RobotState> robotStates)
        {
            foreach (var kvp in robotStates)
            {
                if (IsAvailable(kvp.Key, robotStates))
                    return kvp.Key;
            }
            return null;
        }

        private static bool IsAvailable(string robotId, Dictionary<string, RobotState> robotStates)
        {
            if (!robotStates.ContainsKey(robotId))
                return false;

            var state = robotStates[robotId];
            return state.State == "idle" && !state.HeldWaferId.HasValue;
        }
    }
}
