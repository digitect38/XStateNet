using System;
using System.Threading.Tasks;

namespace XStateNet.InterMachine
{
    /// <summary>
    /// Extension methods for state machines to enable inter-machine communication
    /// </summary>
    public static class InterMachineExtensions
    {
        private static readonly InterMachineConnector _globalConnector = new InterMachineConnector();

        /// <summary>
        /// Create a new isolated connector for testing or isolated scenarios
        /// </summary>
        public static InterMachineConnector CreateIsolatedConnector()
        {
            return new InterMachineConnector();
        }

        /// <summary>
        /// Enable inter-machine communication for this state machine
        /// </summary>
        public static IStateMachine EnableInterMachine(this IStateMachine machine, string machineId = null, InterMachineConnector connector = null)
        {
            var id = machineId ?? machine.machineId;
            var conn = connector ?? _globalConnector;
            conn.RegisterMachine(id, machine);
            return machine;
        }

        /// <summary>
        /// Connect this machine to another for bidirectional communication
        /// </summary>
        public static IStateMachine ConnectTo(this IStateMachine machine, string otherMachineId, InterMachineConnector connector = null)
        {
            var conn = connector ?? _globalConnector;
            conn.Connect(machine.machineId, otherMachineId);
            return machine;
        }

        /// <summary>
        /// Send a message directly to another connected machine
        /// </summary>
        public static async Task SendToMachineAsync(this IStateMachine machine, string targetMachineId, string eventName, object data = null, InterMachineConnector connector = null)
        {
            var conn = connector ?? _globalConnector;
            await conn.SendAsync(machine.machineId, targetMachineId, eventName, data);
        }

        /// <summary>
        /// Broadcast a message to all connected machines
        /// </summary>
        public static async Task BroadcastToMachinesAsync(this IStateMachine machine, string eventName, object data = null)
        {
            await _globalConnector.BroadcastAsync(machine.machineId, eventName, data);
        }

        /// <summary>
        /// Set a custom message handler for incoming inter-machine messages
        /// </summary>
        public static IStateMachine OnInterMachineMessage(this IStateMachine machine, Func<InterMachineMessage, Task> handler)
        {
            _globalConnector.SetMessageHandler(machine.machineId, handler);
            return machine;
        }

        /// <summary>
        /// Get the global connector instance (for advanced scenarios)
        /// </summary>
        public static InterMachineConnector GetConnector()
        {
            return _globalConnector;
        }

        /// <summary>
        /// Disconnect from another machine
        /// </summary>
        public static IStateMachine DisconnectFrom(this IStateMachine machine, string otherMachineId)
        {
            _globalConnector.Disconnect(machine.machineId, otherMachineId);
            return machine;
        }

        /// <summary>
        /// Disable inter-machine communication and remove all connections
        /// </summary>
        public static void DisableInterMachine(this IStateMachine machine)
        {
            _globalConnector.UnregisterMachine(machine.machineId);
        }
    }
}