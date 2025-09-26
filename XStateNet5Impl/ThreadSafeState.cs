using System;
using System.Threading;

namespace XStateNet
{
    /// <summary>
    /// Thread-safe state management for compound states
    /// </summary>
    public class ThreadSafeStateInfo
    {
        private volatile StateSnapshot _snapshot = new StateSnapshot();

        /// <summary>
        /// Immutable snapshot of state information
        /// </summary>
        private class StateSnapshot
        {
            public bool IsActive { get; init; }
            public string? ActiveStateName { get; init; }
            public string? LastActiveStateName { get; init; }

            public StateSnapshot() { }

            public StateSnapshot(bool isActive, string? activeStateName, string? lastActiveStateName)
            {
                IsActive = isActive;
                ActiveStateName = activeStateName;
                LastActiveStateName = lastActiveStateName;
            }
        }

        /// <summary>
        /// Get current active state
        /// </summary>
        public bool IsActive => _snapshot.IsActive;

        /// <summary>
        /// Get current active state name
        /// </summary>
        public string? ActiveStateName => _snapshot.ActiveStateName;

        /// <summary>
        /// Get last active state name (for history)
        /// </summary>
        public string? LastActiveStateName => _snapshot.LastActiveStateName;

        /// <summary>
        /// Atomically update state information
        /// </summary>
        public void UpdateState(bool isActive, string? activeStateName)
        {
            var current = _snapshot;
            var newSnapshot = new StateSnapshot(
                isActive,
                activeStateName,
                activeStateName ?? current.LastActiveStateName // Preserve last active if setting to null
            );

            // Atomic assignment ensures all fields are updated together
            _snapshot = newSnapshot;
        }

        /// <summary>
        /// Atomically transition to active state
        /// </summary>
        public void Activate(string? stateName)
        {
            UpdateState(true, stateName);
        }

        /// <summary>
        /// Atomically transition to inactive state
        /// </summary>
        public void Deactivate()
        {
            var current = _snapshot;
            _snapshot = new StateSnapshot(
                false,
                null,
                current.ActiveStateName ?? current.LastActiveStateName // Preserve for history
            );
        }

        /// <summary>
        /// Get a consistent snapshot of the state
        /// </summary>
        public (bool isActive, string? activeStateName, string? lastActiveStateName) GetSnapshot()
        {
            var snapshot = _snapshot;
            return (snapshot.IsActive, snapshot.ActiveStateName, snapshot.LastActiveStateName);
        }
    }

    /// <summary>
    /// Thread-safe counter for statistics
    /// </summary>
    public class ThreadSafeCounter
    {
        private long _value;

        public long Value => Interlocked.Read(ref _value);

        public long Increment() => Interlocked.Increment(ref _value);

        public long Decrement() => Interlocked.Decrement(ref _value);

        public long Add(long value) => Interlocked.Add(ref _value, value);

        public void Reset() => Interlocked.Exchange(ref _value, 0);

        public long Exchange(long newValue) => Interlocked.Exchange(ref _value, newValue);
    }
}