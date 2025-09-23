using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using XStateNet;

namespace TimelineWPF.PubSub
{
    /// <summary>
    /// Publisher that integrates XStateNet state machines with Timeline event bus
    /// </summary>
    public class StateMachinePublisher
    {
        private readonly ITimelineEventBus _eventBus;
        private readonly ConcurrentDictionary<string, StateMachine> _registeredMachines;
        private readonly Stopwatch _stopwatch;
        private readonly object _lock = new object();

        public StateMachinePublisher(ITimelineEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _registeredMachines = new ConcurrentDictionary<string, StateMachine>();
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Register a state machine for timeline monitoring
        /// </summary>
        public void RegisterStateMachine(string machineName, StateMachine machine, List<string> states = null)
        {
            if (string.IsNullOrEmpty(machineName))
                throw new ArgumentNullException(nameof(machineName));
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            lock (_lock)
            {
                if (_registeredMachines.ContainsKey(machineName))
                {
                    UnregisterStateMachine(machineName);
                }

                _registeredMachines[machineName] = machine;

                // Get initial state
                var initialState = machine.GetActiveStateString() ?? "initial";
                
                // If states not provided, try to extract from machine
                if (states == null)
                {
                    states = new List<string> { initialState };
                }

                // Publish machine registration message
                var registrationMessage = new MachineRegisteredMessage(
                    machineName,
                    states,
                    initialState,
                    GetCurrentTimestamp()
                );
                _eventBus.Publish(registrationMessage);

                // Hook up event handlers - XStateNet uses a simpler API
                // For now, we'll just track the initial state since XStateNet doesn't expose these events directly
            }
        }

        /// <summary>
        /// Unregister a state machine from timeline monitoring
        /// </summary>
        public void UnregisterStateMachine(string machineName)
        {
            lock (_lock)
            {
                if (_registeredMachines.TryRemove(machineName, out var machine))
                {
                    // Publish unregistration message
                    var unregistrationMessage = new MachineUnregisteredMessage(
                        machineName,
                        GetCurrentTimestamp(),
                        "Machine unregistered"
                    );
                    _eventBus.Publish(unregistrationMessage);
                }
            }
        }

        /// <summary>
        /// Manually publish a state transition
        /// </summary>
        public void PublishStateTransition(string machineName, string fromState, string toState, string triggerEvent = null)
        {
            var message = new StateTransitionMessage(
                machineName,
                fromState,
                toState,
                GetCurrentTimestamp(),
                triggerEvent
            );
            _eventBus.Publish(message);
        }

        /// <summary>
        /// Manually publish an event
        /// </summary>
        public void PublishEvent(string machineName, string eventName, object eventData = null, bool wasHandled = true)
        {
            var message = new EventMessage(
                machineName,
                eventName,
                GetCurrentTimestamp(),
                eventData,
                wasHandled
            );
            _eventBus.Publish(message);
        }

        /// <summary>
        /// Manually publish an action
        /// </summary>
        public void PublishAction(string machineName, string actionName, string stateName = null)
        {
            var message = new ActionMessage(
                machineName,
                actionName,
                GetCurrentTimestamp(),
                stateName
            );
            _eventBus.Publish(message);
        }

        // These methods would be called if XStateNet provided event hooks
        // For now they're kept for future implementation when XStateNet adds these features

        private double GetCurrentTimestamp()
        {
            return _stopwatch.Elapsed.TotalMilliseconds * 1000; // Convert to microseconds
        }

        /// <summary>
        /// Clear all registered machines
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                var machineNames = new List<string>(_registeredMachines.Keys);
                foreach (var name in machineNames)
                {
                    UnregisterStateMachine(name);
                }
            }
        }
    }

    // These event argument classes are placeholders for future XStateNet integration
    // When XStateNet adds event hooks, these can be used to capture state machine events
}