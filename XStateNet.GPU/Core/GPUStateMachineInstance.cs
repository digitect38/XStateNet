using System;
using System.Runtime.InteropServices;
using ILGPU;

namespace XStateNet.GPU.Core
{
    /// <summary>
    /// GPU-friendly state machine instance structure
    /// Must be blittable for GPU transfer
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUStateMachineInstance
    {
        public int InstanceId;
        public int CurrentState;
        public int PreviousState;
        public int LastEvent;
        public long LastTransitionTime;
        public int ErrorCount;
        public int Flags; // Bit flags for various states

        // Fixed-size context data (adjust size as needed)
        // Using fixed buffer for GPU compatibility
        public unsafe fixed byte ContextData[256];
    }

    /// <summary>
    /// GPU event structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUEvent
    {
        public int InstanceId;
        public int EventType;
        public int EventData1;
        public int EventData2;
        public long Timestamp;
    }

    /// <summary>
    /// Transition table entry
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TransitionEntry
    {
        public int FromState;
        public int EventType;
        public int ToState;
        public int ActionId;
        public int GuardId;
    }

    /// <summary>
    /// State machine definition for GPU
    /// </summary>
    public class GPUStateMachineDefinition
    {
        public string Name { get; set; }
        public int StateCount { get; set; }
        public int EventTypeCount { get; set; }
        public string[] StateNames { get; set; }
        public string[] EventNames { get; set; }
        public TransitionEntry[] TransitionTable { get; set; }

        public GPUStateMachineDefinition(string name, int stateCount, int eventTypeCount)
        {
            Name = name;
            StateCount = stateCount;
            EventTypeCount = eventTypeCount;
            StateNames = new string[stateCount];
            EventNames = new string[eventTypeCount];
            TransitionTable = Array.Empty<TransitionEntry>();
        }

        /// <summary>
        /// Convert string state/event names to integer IDs for GPU
        /// </summary>
        public int GetStateId(string stateName)
        {
            return Array.IndexOf(StateNames, stateName);
        }

        public int GetEventId(string eventName)
        {
            return Array.IndexOf(EventNames, eventName);
        }
    }

    /// <summary>
    /// GPU kernels for state machine processing
    /// </summary>
    public static class GPUStateMachineKernels
    {
        /// <summary>
        /// Process events for state machines on GPU
        /// Each thread processes one state machine instance
        /// </summary>
        public static void ProcessEventsKernel(
            Index1D index,
            ArrayView<GPUStateMachineInstance> instances,
            ArrayView<GPUEvent> events,
            ArrayView<TransitionEntry> transitions,
            int transitionCount,
            int maxState,
            int maxEvent)
        {
            if (index >= instances.Length) return;

            // Get the event for this instance
            var evt = events[index];
            if (evt.InstanceId != index) return; // Event not for this instance

            ref var instance = ref instances[index];

            // Linear search through transition table
            // (could be optimized with sorted table + binary search)
            for (int i = 0; i < transitionCount; i++)
            {
                var transition = transitions[i];

                if (transition.FromState == instance.CurrentState &&
                    transition.EventType == evt.EventType)
                {
                    // Found matching transition
                    instance.PreviousState = instance.CurrentState;
                    instance.CurrentState = transition.ToState;
                    instance.LastEvent = evt.EventType;
                    instance.LastTransitionTime = evt.Timestamp;
                    break;
                }
            }
        }

        /// <summary>
        /// Batch update context data
        /// </summary>
        public static unsafe void UpdateContextKernel(
            Index1D index,
            ArrayView<GPUStateMachineInstance> instances,
            ArrayView<byte> contextUpdates,
            int contextSize)
        {
            if (index >= instances.Length) return;

            ref var instance = ref instances[index];
            int offset = index * contextSize;

            // Copy context update to instance
            fixed (byte* contextPtr = instance.ContextData)
            {
                for (int i = 0; i < contextSize && i < 256; i++)
                {
                    contextPtr[i] = contextUpdates[offset + i];
                }
            }
        }

        /// <summary>
        /// Check guards in parallel
        /// </summary>
        public static void EvaluateGuardsKernel(
            Index1D index,
            ArrayView<GPUStateMachineInstance> instances,
            ArrayView<int> guardResults)
        {
            if (index >= instances.Length) return;

            var instance = instances[index];

            // Simple guard evaluation example
            // In practice, this would be more complex
            if (instance.ErrorCount > 3)
            {
                guardResults[index] = 0; // Guard fails
            }
            else
            {
                guardResults[index] = 1; // Guard passes
            }
        }

        /// <summary>
        /// Collect statistics across all instances
        /// </summary>
        public static void CollectStatsKernel(
            Index1D index,
            ArrayView<GPUStateMachineInstance> instances,
            ArrayView<int> stateHistogram)
        {
            if (index >= instances.Length) return;

            var state = instances[index].CurrentState;
            if (state >= 0 && state < stateHistogram.Length)
            {
                // Note: Atomic operations needed for correct histogram
                Atomic.Add(ref stateHistogram[state], 1);
            }
        }
    }
}