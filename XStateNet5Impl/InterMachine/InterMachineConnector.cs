using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XStateNet.InterMachine
{
    /// <summary>
    /// XStateNet-IM: Inter-Machine direct communication framework
    /// Allows state machines to communicate directly without external mediators
    /// </summary>
    public class InterMachineConnector
    {
        private readonly ConcurrentDictionary<string, IStateMachine> _machines = new();
        private readonly ConcurrentDictionary<string, List<string>> _connections = new();
        private readonly ConcurrentDictionary<string, Func<InterMachineMessage, Task>> _messageHandlers = new();

        /// <summary>
        /// Register a state machine with the connector
        /// </summary>
        public void RegisterMachine(string machineId, IStateMachine machine)
        {
            if (!_machines.TryAdd(machineId, machine))
            {
                throw new InvalidOperationException($"Machine {machineId} is already registered");
            }
            _connections[machineId] = new List<string>();
        }

        /// <summary>
        /// Connect two machines for bidirectional communication
        /// </summary>
        public void Connect(string machine1Id, string machine2Id)
        {
            if (!_machines.ContainsKey(machine1Id) || !_machines.ContainsKey(machine2Id))
            {
                throw new InvalidOperationException("Both machines must be registered before connecting");
            }

            // Add bidirectional connection
            _connections[machine1Id].Add(machine2Id);
            _connections[machine2Id].Add(machine1Id);
        }

        /// <summary>
        /// Set a message handler for a machine
        /// </summary>
        public void SetMessageHandler(string machineId, Func<InterMachineMessage, Task> handler)
        {
            _messageHandlers[machineId] = handler;
        }

        /// <summary>
        /// Send a message from one machine to another
        /// </summary>
        public async Task SendAsync(string fromMachineId, string toMachineId, string eventName, object data = null)
        {
            if (!_machines.ContainsKey(fromMachineId))
            {
                throw new InvalidOperationException($"Sender machine {fromMachineId} is not registered");
            }

            if (!_machines.ContainsKey(toMachineId))
            {
                throw new InvalidOperationException($"Target machine {toMachineId} is not registered");
            }

            if (!_connections[fromMachineId].Contains(toMachineId))
            {
                throw new InvalidOperationException($"Machines {fromMachineId} and {toMachineId} are not connected");
            }

            var message = new InterMachineMessage
            {
                FromMachineId = fromMachineId,
                ToMachineId = toMachineId,
                EventName = eventName,
                Data = data,
                Timestamp = DateTime.UtcNow
            };

            // If target has a handler, use it
            if (_messageHandlers.TryGetValue(toMachineId, out var handler))
            {
                await handler(message);
            }
            else
            {
                // Direct send to target machine
                var targetMachine = _machines[toMachineId];
                await targetMachine.SendAsync(eventName, data);
            }
        }

        /// <summary>
        /// Broadcast a message from one machine to all connected machines
        /// </summary>
        public async Task BroadcastAsync(string fromMachineId, string eventName, object data = null)
        {
            if (!_machines.ContainsKey(fromMachineId))
            {
                throw new InvalidOperationException($"Sender machine {fromMachineId} is not registered");
            }

            var connectedMachines = _connections[fromMachineId];
            var tasks = new List<Task>();

            foreach (var toMachineId in connectedMachines)
            {
                tasks.Add(SendAsync(fromMachineId, toMachineId, eventName, data));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Get all connected machines for a given machine
        /// </summary>
        public IReadOnlyList<string> GetConnections(string machineId)
        {
            if (!_connections.TryGetValue(machineId, out var connections))
            {
                return new List<string>();
            }
            return connections.AsReadOnly();
        }

        /// <summary>
        /// Disconnect two machines
        /// </summary>
        public void Disconnect(string machine1Id, string machine2Id)
        {
            if (_connections.TryGetValue(machine1Id, out var connections1))
            {
                connections1.Remove(machine2Id);
            }

            if (_connections.TryGetValue(machine2Id, out var connections2))
            {
                connections2.Remove(machine1Id);
            }
        }

        /// <summary>
        /// Unregister a machine and remove all its connections
        /// </summary>
        public void UnregisterMachine(string machineId)
        {
            if (_machines.TryRemove(machineId, out _))
            {
                // Remove all connections to this machine
                if (_connections.TryRemove(machineId, out var connections))
                {
                    foreach (var connectedId in connections)
                    {
                        if (_connections.TryGetValue(connectedId, out var otherConnections))
                        {
                            otherConnections.Remove(machineId);
                        }
                    }
                }

                _messageHandlers.TryRemove(machineId, out _);
            }
        }
    }

    /// <summary>
    /// Message structure for inter-machine communication
    /// </summary>
    public class InterMachineMessage
    {
        public string FromMachineId { get; set; }
        public string ToMachineId { get; set; }
        public string EventName { get; set; }
        public object Data { get; set; }
        public DateTime Timestamp { get; set; }
    }
}