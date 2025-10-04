using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using XStateNet.SharedMemory.Core;
using XStateNet.Orchestration;

namespace XStateNet.SharedMemory
{
    /// <summary>
    /// Ultra-high performance orchestrator using shared memory for inter-process communication
    /// Wraps EventBusOrchestrator and adds shared memory capability
    /// Target: 50,000+ msg/sec, 0.02-0.05ms latency
    /// </summary>
    public class SharedMemoryOrchestrator : IDisposable
    {
        private readonly EventBusOrchestrator _localOrchestrator;
        private readonly InterceptingOrchestrator _interceptingOrchestrator;
        private readonly SharedMemorySegment _segment;
        private readonly ProcessRegistry _processRegistry;
        private readonly SharedMemoryRingBuffer _ringBuffer;
        private readonly ProcessRegistration _thisProcess;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _readerTask;
        private readonly ConcurrentDictionary<string, int> _machineToProcessMap;
        private bool _disposed;

        public int ProcessId => _thisProcess.ProcessId;
        public string ProcessName => _thisProcess.GetProcessName();
        public EventBusOrchestrator LocalOrchestrator => _interceptingOrchestrator;

        /// <summary>
        /// Creates a new SharedMemoryOrchestrator
        /// </summary>
        /// <param name="segmentName">Name of the shared memory segment</param>
        /// <param name="processName">Name of this process (optional)</param>
        /// <param name="bufferSize">Size of the ring buffer (default 1MB)</param>
        public SharedMemoryOrchestrator(
            string segmentName = "XStateNet_Default",
            string? processName = null,
            long bufferSize = 1024 * 1024)
        {
            // Create local orchestrator for in-process machines
            _localOrchestrator = new EventBusOrchestrator();

            // Create intercepting wrapper that hooks RegisterMachine calls
            _interceptingOrchestrator = new InterceptingOrchestrator(_localOrchestrator, OnMachineRegistered, OnMachineUnregistered);

            // Determine if we're the first process (owner) or joining existing segment
            bool isOwner = false;
            try
            {
                _segment = new SharedMemorySegment(segmentName, bufferSize, createNew: true);
                isOwner = true;
            }
            catch
            {
                _segment = new SharedMemorySegment(segmentName, bufferSize, createNew: false);
                isOwner = false;
            }

            // Initialize components
            _processRegistry = new ProcessRegistry(_segment);
            _ringBuffer = new SharedMemoryRingBuffer(_segment);
            _cancellationTokenSource = new CancellationTokenSource();
            _machineToProcessMap = new ConcurrentDictionary<string, int>();

            // Register this process
            _thisProcess = _processRegistry.RegisterProcess(
                processName ?? $"Process_{System.Diagnostics.Process.GetCurrentProcess().Id}"
            );

            // Start background reader
            _readerTask = Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token));

            Log($"SharedMemoryOrchestrator initialized: ProcessId={_thisProcess.ProcessId}, IsOwner={isOwner}, Segment={segmentName}");
        }

        /// <summary>
        /// Sends an event to a target machine
        /// Optimizes for same-process delivery, uses shared memory for cross-process
        /// </summary>
        public async Task<EventResult> SendEventAsync(
            string fromMachineId,
            string toMachineId,
            string eventName,
            object? eventData = null,
            int timeoutMs = 5000)
        {
            // Lookup target machine's process
            int targetProcessId = LookupMachineProcess(toMachineId);

            if (targetProcessId == _thisProcess.ProcessId)
            {
                // Same process - use local delivery (fast path)
                Log($"Local delivery: {fromMachineId} -> {toMachineId} ({eventName})");
                return await _localOrchestrator.SendEventAsync(fromMachineId, toMachineId, eventName, eventData, timeoutMs);
            }
            else
            {
                // Different process - use shared memory
                Log($"Shared memory delivery: {fromMachineId} -> {toMachineId} ({eventName}) [Process {_thisProcess.ProcessId} -> {targetProcessId}]");

                // Build message envelope
                var message = MessageEnvelopeHelper.BuildMessage(toMachineId, eventName, SerializeEventData(eventData));

                // Write to ring buffer
                bool written = await _ringBuffer.WriteAsync(message, timeoutMs: 1000, _cancellationTokenSource.Token);

                if (!written)
                {
                    return new EventResult
                    {
                        Success = false,
                        EventId = Guid.NewGuid(),
                        ErrorMessage = "Failed to write message to shared memory within timeout. Buffer may be full."
                    };
                }

                return new EventResult
                {
                    Success = true,
                    EventId = Guid.NewGuid()
                };
            }
        }

        /// <summary>
        /// Callback when a machine is registered via LocalOrchestrator
        /// Updates shared memory registry
        /// </summary>
        private void OnMachineRegistered(string machineId, IStateMachine machine)
        {
            // Record machine location
            _machineToProcessMap[machineId] = _thisProcess.ProcessId;
            _processRegistry.RegisterMachine(_thisProcess.ProcessId, machineId);

            Log($"Machine registered: {machineId} in Process {_thisProcess.ProcessId}");
        }

        /// <summary>
        /// Callback when a machine is unregistered via LocalOrchestrator
        /// Updates shared memory registry
        /// </summary>
        private void OnMachineUnregistered(string machineId)
        {
            _machineToProcessMap.TryRemove(machineId, out _);
            _processRegistry.UnregisterMachine(_thisProcess.ProcessId, machineId);

            Log($"Machine unregistered: {machineId}");
        }

        /// <summary>
        /// Registers a machine in this orchestrator
        /// Records machine-to-process mapping for routing
        /// </summary>
        public void RegisterMachine(string machineId, IStateMachine machine)
        {
            _localOrchestrator.RegisterMachine(machineId, machine);
            OnMachineRegistered(machineId, machine);
        }

        /// <summary>
        /// Unregisters a machine from this orchestrator (deprecated - use LocalOrchestrator)
        /// </summary>
        [Obsolete("Use LocalOrchestrator.UnregisterMachine instead")]
        public void UnregisterMachine(string machineId)
        {
            _localOrchestrator.UnregisterMachine(machineId);
        }

        /// <summary>
        /// Background task that reads messages from shared memory
        /// </summary>
        private async Task ReadMessagesAsync(CancellationToken cancellationToken)
        {
            Log("Message reader started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read message from ring buffer
                    var message = await _ringBuffer.ReadAsync(timeoutMs: 100, cancellationToken);

                    if (message != null)
                    {
                        // Parse message
                        var (machineId, eventName, payload, timestamp) = MessageEnvelopeHelper.ParseMessage(message);

                        // Check if target machine is in this process
                        if (_machineToProcessMap.TryGetValue(machineId, out int processId) && processId == _thisProcess.ProcessId)
                        {
                            // Deliver locally via EventBusOrchestrator
                            var eventData = DeserializeEventData(payload);
                            await _localOrchestrator.SendEventAsync("SharedMemory", machineId, eventName, eventData);

                            Log($"Message delivered: {machineId} ({eventName}) [age={(DateTime.UtcNow.Ticks - timestamp) / 10000.0:F2}ms]");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Error reading message: {ex.Message}");
                }
            }

            Log("Message reader stopped");
        }

        /// <summary>
        /// Looks up which process owns the target machine
        /// </summary>
        private int LookupMachineProcess(string machineId)
        {
            // Check local cache first
            if (_machineToProcessMap.TryGetValue(machineId, out int processId))
            {
                Log($"Lookup: {machineId} -> Process {processId} (from cache)");
                return processId;
            }

            // Query process registry
            processId = _processRegistry.FindMachineProcess(machineId);

            if (processId >= 0)
            {
                _machineToProcessMap[machineId] = processId;
                Log($"Lookup: {machineId} -> Process {processId} (from registry)");
                return processId;
            }

            // Default to local process (will be registered when machine is created)
            Log($"Lookup: {machineId} -> Process {_thisProcess.ProcessId} (default - not found in registry)");
            return _thisProcess.ProcessId;
        }

        /// <summary>
        /// Serializes event data to byte array
        /// </summary>
        private byte[]? SerializeEventData(object? eventData)
        {
            if (eventData == null)
                return null;

            // Simple JSON serialization (can be optimized with binary serialization)
            var json = System.Text.Json.JsonSerializer.Serialize(eventData);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Deserializes event data from byte array
        /// </summary>
        private object? DeserializeEventData(byte[]? payload)
        {
            if (payload == null || payload.Length == 0)
                return null;

            // Simple JSON deserialization
            var json = System.Text.Encoding.UTF8.GetString(payload);
            return System.Text.Json.JsonSerializer.Deserialize<object>(json);
        }

        /// <summary>
        /// Gets statistics about shared memory usage
        /// </summary>
        public SharedMemoryStats GetStats()
        {
            return _segment.GetStats();
        }

        /// <summary>
        /// Gets all registered processes
        /// </summary>
        public ProcessRegistration[] GetRegisteredProcesses()
        {
            return _processRegistry.GetAllProcesses();
        }

        private void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [SharedMemoryOrchestrator] {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;

            Log("Shutting down...");

            // Stop reader
            _cancellationTokenSource.Cancel();
            _readerTask.Wait(TimeSpan.FromSeconds(2));

            // Unregister process
            _processRegistry.UnregisterProcess(_thisProcess.ProcessId);

            // Dispose components
            _processRegistry?.Dispose();
            _localOrchestrator?.Dispose();
            _segment.Dispose();
            _cancellationTokenSource.Dispose();

            _disposed = true;
            Log("Shutdown complete");
        }
    }

    /// <summary>
    /// Wrapper around EventBusOrchestrator that intercepts RegisterMachine/UnregisterMachine calls
    /// Allows SharedMemoryOrchestrator to hook into machine lifecycle
    /// </summary>
    internal class InterceptingOrchestrator : EventBusOrchestrator
    {
        private readonly EventBusOrchestrator _inner;
        private readonly Action<string, IStateMachine> _onRegister;
        private readonly Action<string> _onUnregister;

        public InterceptingOrchestrator(
            EventBusOrchestrator inner,
            Action<string, IStateMachine> onRegister,
            Action<string> onUnregister)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _onRegister = onRegister ?? throw new ArgumentNullException(nameof(onRegister));
            _onUnregister = onUnregister ?? throw new ArgumentNullException(nameof(onUnregister));
        }

        public new void RegisterMachine(string machineId, IStateMachine machine)
        {
            // Call inner orchestrator first
            _inner.RegisterMachine(machineId, machine);

            // Then notify callback
            _onRegister(machineId, machine);
        }

        public new void RegisterMachine(string machineId, IStateMachine machine, int? channelGroupId)
        {
            // Call inner orchestrator first
            _inner.RegisterMachine(machineId, machine, channelGroupId);

            // Then notify callback
            _onRegister(machineId, machine);
        }

        public new void RegisterMachineWithContext(string machineId, IStateMachine machine, OrchestratedContext? context = null)
        {
            // Call inner orchestrator first
            _inner.RegisterMachineWithContext(machineId, machine, context);

            // Then notify callback
            _onRegister(machineId, machine);
        }

        public new void UnregisterMachine(string machineId)
        {
            // Call inner orchestrator first
            _inner.UnregisterMachine(machineId);

            // Then notify callback
            _onUnregister(machineId);
        }

        public new Task<EventResult> SendEventAsync(string fromMachineId, string toMachineId, string eventName, object? eventData = null, int timeoutMs = 5000)
        {
            return _inner.SendEventAsync(fromMachineId, toMachineId, eventName, eventData, timeoutMs);
        }
    }
}
