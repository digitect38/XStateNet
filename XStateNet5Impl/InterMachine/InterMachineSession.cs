using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XStateNet.InterMachine
{
    /// <summary>
    /// Represents an isolated inter-machine communication session
    /// </summary>
    public class InterMachineSession : IDisposable
    {
        private readonly InterMachineConnector _connector;
        private readonly Dictionary<string, ConnectedMachine> _machines;

        public InterMachineSession()
        {
            _connector = new InterMachineConnector();
            _machines = new Dictionary<string, ConnectedMachine>();
        }

        /// <summary>
        /// Add a machine to this session
        /// </summary>
        public ConnectedMachine AddMachine(IStateMachine machine, string machineId = null)
        {
            var id = machineId ?? machine.machineId;
            _connector.RegisterMachine(id, machine);

            var connectedMachine = new ConnectedMachine(machine, id, _connector);
            _machines[id] = connectedMachine;
            return connectedMachine;
        }

        /// <summary>
        /// Connect two machines in this session
        /// </summary>
        public void Connect(string machine1Id, string machine2Id)
        {
            _connector.Connect(machine1Id, machine2Id);
        }

        /// <summary>
        /// Get a connected machine by ID
        /// </summary>
        public ConnectedMachine GetMachine(string machineId)
        {
            return _machines.TryGetValue(machineId, out var machine) ? machine : null;
        }

        public void Dispose()
        {
            foreach (var machine in _machines.Values)
            {
                machine.Dispose();
            }
            _machines.Clear();
        }
    }

    /// <summary>
    /// Wrapper for a state machine with inter-machine capabilities
    /// </summary>
    public class ConnectedMachine : IDisposable
    {
        private readonly IStateMachine _machine;
        private readonly string _machineId;
        private readonly InterMachineConnector _connector;

        internal ConnectedMachine(IStateMachine machine, string machineId, InterMachineConnector connector)
        {
            _machine = machine;
            _machineId = machineId;
            _connector = connector;
        }

        public IStateMachine Machine => _machine;
        public string MachineId => _machineId;

        /// <summary>
        /// Send a message to another machine in the same session
        /// </summary>
        public async Task SendToAsync(string targetMachineId, string eventName, object data = null)
        {
            await _connector.SendAsync(_machineId, targetMachineId, eventName, data);
        }

        /// <summary>
        /// Broadcast to all connected machines
        /// </summary>
        public async Task BroadcastAsync(string eventName, object data = null)
        {
            await _connector.BroadcastAsync(_machineId, eventName, data);
        }

        /// <summary>
        /// Set a custom message handler
        /// </summary>
        public void OnMessage(Func<InterMachineMessage, Task> handler)
        {
            _connector.SetMessageHandler(_machineId, handler);
        }

        public void Dispose()
        {
            _connector.UnregisterMachine(_machineId);
            _machine.Stop();
        }
    }
}