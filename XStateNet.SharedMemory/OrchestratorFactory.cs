using System;
using XStateNet.Orchestration;

namespace XStateNet.SharedMemory
{
    /// <summary>
    /// Orchestrator type selection
    /// </summary>
    public enum OrchestratorType
    {
        /// <summary>
        /// In-process orchestration using EventBusOrchestrator
        /// Performance: Excellent (in-memory event bus)
        /// Use case: Single process, multiple state machines
        /// </summary>
        InProcess,

        /// <summary>
        /// Shared memory orchestration for ultra-low latency IPC
        /// Performance: 50,000+ msg/sec, 0.02-0.05ms latency
        /// Use case: Multiple processes on same machine
        /// </summary>
        SharedMemory,

        /// <summary>
        /// Named pipe orchestration for cross-platform IPC
        /// Performance: 1,800 msg/sec, 0.5ms latency
        /// Use case: Multiple processes, cross-platform
        /// </summary>
        InterProcess,

        /// <summary>
        /// Distributed orchestration for multi-machine deployment
        /// Performance: Network-dependent
        /// Use case: Multiple machines, distributed systems
        /// </summary>
        Distributed
    }

    /// <summary>
    /// Configuration for orchestrator creation
    /// </summary>
    public class OrchestratorConfig
    {
        public OrchestratorType Type { get; set; } = OrchestratorType.InProcess;
        public string? SegmentName { get; set; }
        public string? ProcessName { get; set; }
        public long BufferSize { get; set; } = 1024 * 1024; // 1MB default
        public string? PipeName { get; set; }
        public string? DistributedEndpoint { get; set; }
    }

    /// <summary>
    /// Factory for creating orchestrators with unified API
    /// Provides location transparency for state machines
    /// </summary>
    public static class OrchestratorFactory
    {
        /// <summary>
        /// Creates an orchestrator based on configuration
        /// </summary>
        public static object Create(OrchestratorConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            return config.Type switch
            {
                OrchestratorType.InProcess => CreateInProcess(),

                OrchestratorType.SharedMemory => CreateSharedMemory(
                    config.SegmentName ?? "XStateNet_Default",
                    config.ProcessName,
                    config.BufferSize),

                OrchestratorType.InterProcess => CreateInterProcess(
                    config.PipeName ?? "XStateNet_Pipe"),

                OrchestratorType.Distributed => CreateDistributed(
                    config.DistributedEndpoint ?? "localhost:6379"),

                _ => throw new ArgumentException($"Unknown orchestrator type: {config.Type}")
            };
        }

        /// <summary>
        /// Creates an in-process orchestrator (default)
        /// </summary>
        public static EventBusOrchestrator CreateInProcess()
        {
            return new EventBusOrchestrator();
        }

        /// <summary>
        /// Creates a shared memory orchestrator for ultra-low latency IPC
        /// </summary>
        public static SharedMemoryOrchestrator CreateSharedMemory(
            string segmentName = "XStateNet_Default",
            string? processName = null,
            long bufferSize = 1024 * 1024)
        {
            return new SharedMemoryOrchestrator(segmentName, processName, bufferSize);
        }

        /// <summary>
        /// Creates an inter-process orchestrator using Named Pipes
        /// NOTE: Not yet implemented - placeholder for future work
        /// </summary>
        public static EventBusOrchestrator CreateInterProcess(string pipeName)
        {
            // TODO: Implement InterProcessOrchestrator
            // return new InterProcessOrchestrator(pipeName);
            throw new NotImplementedException("InterProcess orchestrator not yet implemented. Use SharedMemory for IPC.");
        }

        /// <summary>
        /// Creates a distributed orchestrator for multi-machine deployment
        /// NOTE: Not yet implemented - placeholder for future work
        /// </summary>
        public static EventBusOrchestrator CreateDistributed(string endpoint)
        {
            // TODO: Implement DistributedOrchestrator
            // return new DistributedOrchestrator(endpoint);
            throw new NotImplementedException("Distributed orchestrator not yet implemented. Use SharedMemory for local IPC.");
        }

        /// <summary>
        /// Creates an orchestrator with automatic type selection based on environment
        /// </summary>
        public static object CreateAuto(string? segmentName = null)
        {
            // Check environment variables or configuration
            var orchestratorType = Environment.GetEnvironmentVariable("XSTATENET_ORCHESTRATOR");

            if (orchestratorType != null)
            {
                switch (orchestratorType.ToUpperInvariant())
                {
                    case "INPROCESS":
                        return CreateInProcess();

                    case "SHAREDMEMORY":
                        return CreateSharedMemory(segmentName ?? "XStateNet_Default");

                    case "INTERPROCESS":
                        return CreateInterProcess(segmentName ?? "XStateNet_Pipe");

                    case "DISTRIBUTED":
                        return CreateDistributed(segmentName ?? "localhost:6379");
                }
            }

            // Default to in-process
            return CreateInProcess();
        }

        /// <summary>
        /// Gets performance characteristics for a given orchestrator type
        /// </summary>
        public static OrchestratorPerformance GetPerformanceCharacteristics(OrchestratorType type)
        {
            return type switch
            {
                OrchestratorType.InProcess => new OrchestratorPerformance
                {
                    Type = OrchestratorType.InProcess,
                    TypicalLatencyMs = 0.01,
                    TypicalThroughputMsgPerSec = 100000,
                    Description = "In-memory event bus, single process only",
                    Scope = "Single Process"
                },

                OrchestratorType.SharedMemory => new OrchestratorPerformance
                {
                    Type = OrchestratorType.SharedMemory,
                    TypicalLatencyMs = 0.05,
                    TypicalThroughputMsgPerSec = 50000,
                    Description = "Memory-mapped files, ultra-low latency IPC",
                    Scope = "Same Machine"
                },

                OrchestratorType.InterProcess => new OrchestratorPerformance
                {
                    Type = OrchestratorType.InterProcess,
                    TypicalLatencyMs = 0.54,
                    TypicalThroughputMsgPerSec = 1800,
                    Description = "Named Pipes, cross-platform IPC",
                    Scope = "Same Machine"
                },

                OrchestratorType.Distributed => new OrchestratorPerformance
                {
                    Type = OrchestratorType.Distributed,
                    TypicalLatencyMs = 10.0,
                    TypicalThroughputMsgPerSec = 1000,
                    Description = "Network-based, multi-machine deployment",
                    Scope = "Multiple Machines"
                },

                _ => throw new ArgumentException($"Unknown orchestrator type: {type}")
            };
        }
    }

    /// <summary>
    /// Performance characteristics for an orchestrator type
    /// </summary>
    public class OrchestratorPerformance
    {
        public OrchestratorType Type { get; set; }
        public double TypicalLatencyMs { get; set; }
        public int TypicalThroughputMsgPerSec { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Type}: {TypicalLatencyMs}ms latency, {TypicalThroughputMsgPerSec:N0} msg/sec - {Description} ({Scope})";
        }
    }
}
