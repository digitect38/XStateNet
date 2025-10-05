using System.Collections.Concurrent;

namespace TimelineWPF.PubSub
{
    /// <summary>
    /// Message published when a state transition occurs
    /// </summary>
    public class StateTransitionMessage : ITimelineMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public double Timestamp { get; }
        public string MachineName { get; }
        public TimelineMessageType MessageType => TimelineMessageType.StateTransition;

        public string FromState { get; }
        public string ToState { get; }
        public string? TriggerEvent { get; }
        public ConcurrentDictionary<string, object>? Context { get; }

        public StateTransitionMessage(string machineName, string fromState, string toState,
            double timestamp, string? triggerEvent = null, ConcurrentDictionary<string, object>? context = null)
        {
            MachineName = machineName;
            FromState = fromState;
            ToState = toState;
            Timestamp = timestamp;
            TriggerEvent = triggerEvent;
            Context = context;
        }
    }

    /// <summary>
    /// Message published when an event is sent to a state machine
    /// </summary>
    public class EventMessage : ITimelineMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public double Timestamp { get; }
        public string MachineName { get; }
        public TimelineMessageType MessageType => TimelineMessageType.Event;

        public string EventName { get; }
        public object? EventData { get; }
        public bool WasHandled { get; }

        public EventMessage(string machineName, string eventName, double timestamp,
            object? eventData = null, bool wasHandled = true)
        {
            MachineName = machineName;
            EventName = eventName;
            Timestamp = timestamp;
            EventData = eventData;
            WasHandled = wasHandled;
        }
    }

    /// <summary>
    /// Message published when an action is executed
    /// </summary>
    public class ActionMessage : ITimelineMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public double Timestamp { get; }
        public string MachineName { get; }
        public TimelineMessageType MessageType => TimelineMessageType.Action;

        public string ActionName { get; }
        public string? StateName { get; }
        public TimeSpan? Duration { get; }

        public ActionMessage(string machineName, string actionName, double timestamp,
            string? stateName = null, TimeSpan? duration = null)
        {
            MachineName = machineName;
            ActionName = actionName;
            Timestamp = timestamp;
            StateName = stateName;
            Duration = duration;
        }
    }

    /// <summary>
    /// Message published when a state machine is registered
    /// </summary>
    public class MachineRegisteredMessage : ITimelineMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public double Timestamp { get; }
        public string MachineName { get; }
        public TimelineMessageType MessageType => TimelineMessageType.MachineRegistered;

        public List<string> States { get; }
        public string InitialState { get; }
        public ConcurrentDictionary<string, object>? Metadata { get; }

        public MachineRegisteredMessage(string machineName, List<string> states,
            string initialState, double timestamp, ConcurrentDictionary<string, object>? metadata = null)
        {
            MachineName = machineName;
            States = states;
            InitialState = initialState;
            Timestamp = timestamp;
            Metadata = metadata;
        }
    }

    /// <summary>
    /// Message published when a state machine is unregistered
    /// </summary>
    public class MachineUnregisteredMessage : ITimelineMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public double Timestamp { get; }
        public string MachineName { get; }
        public TimelineMessageType MessageType => TimelineMessageType.MachineUnregistered;

        public string? Reason { get; }

        public MachineUnregisteredMessage(string machineName, double timestamp, string? reason = null)
        {
            MachineName = machineName;
            Timestamp = timestamp;
            Reason = reason;
        }
    }

    /// <summary>
    /// Message published when an error occurs
    /// </summary>
    public class ErrorMessage : ITimelineMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public double Timestamp { get; }
        public string MachineName { get; }
        public TimelineMessageType MessageType => TimelineMessageType.Error;

        public string ErrorType { get; }
        public string ErrorDescription { get; }
        public Exception? Exception { get; }

        public ErrorMessage(string machineName, string errorType, string errorDescription,
            double timestamp, Exception? exception = null)
        {
            MachineName = machineName;
            ErrorType = errorType;
            ErrorDescription = errorDescription;
            Timestamp = timestamp;
            Exception = exception;
        }
    }
}