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
        private readonly SharedMemorySegment _registrySegment;
        private readonly SharedMemorySegment _inboxSegment;
        private readonly ProcessRegistry _processRegistry;
        private readonly SharedMemoryRingBuffer _inboxBuffer;
        private readonly ProcessRegistration _thisProcess;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _readerTask;
        private readonly ConcurrentDictionary<string, int> _machineToProcessMap;
        private readonly ConcurrentDictionary<int, SharedMemoryRingBuffer> _processInboxCache;
        private readonly string _segmentBaseName;
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
            _segmentBaseName = segmentName;
            _processInboxCache = new ConcurrentDictionary<int, SharedMemoryRingBuffer>();

            // Create local orchestrator for in-process machines
            _localOrchestrator = new EventBusOrchestrator();

            // Create intercepting wrapper that hooks RegisterMachine calls
            _interceptingOrchestrator = new InterceptingOrchestrator(_localOrchestrator, OnMachineRegistered, OnMachineUnregistered);

            // Determine if we're the first process (owner) or joining existing registry segment
            bool isOwner = false;
            try
            {
                _registrySegment = new SharedMemorySegment($"{segmentName}_Registry", bufferSize, createNew: true);
                isOwner = true;
            }
            catch
            {
                _registrySegment = new SharedMemorySegment($"{segmentName}_Registry", bufferSize, createNew: false);
                isOwner = false;
            }

            // Initialize components
            _processRegistry = new ProcessRegistry(_registrySegment);
            _cancellationTokenSource = new CancellationTokenSource();
            _machineToProcessMap = new ConcurrentDictionary<string, int>();

            // Register this process
            _thisProcess = _processRegistry.RegisterProcess(
                processName ?? $"Process_{System.Diagnostics.Process.GetCurrentProcess().Id}"
            );

            // Create dedicated inbox segment for THIS process only
            // Each process gets its own inbox that only it reads from
            string inboxName = $"{segmentName}_Inbox_P{_thisProcess.ProcessId}";
            try
            {
                _inboxSegment = new SharedMemorySegment(inboxName, bufferSize, createNew: true);
            }
            catch
            {
                // If inbox already exists (process restarted), open it
                _inboxSegment = new SharedMemorySegment(inboxName, bufferSize, createNew: false);
            }
            _inboxBuffer = new SharedMemoryRingBuffer(_inboxSegment);

            // Start background reader for OUR inbox only
            _readerTask = Task.Run(() => ReadMessagesAsync(_cancellationTokenSource.Token));

            Log($"SharedMemoryOrchestrator initialized: ProcessId={_thisProcess.ProcessId}, IsOwner={isOwner}, Inbox={inboxName}");
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
                // Different process - write to target process's inbox
                Log($"Shared memory delivery: {fromMachineId} -> {toMachineId} ({eventName}) [Process {_thisProcess.ProcessId} -> {targetProcessId}]");

                // Get or create ring buffer for target process's inbox
                var targetInbox = GetOrCreateProcessInbox(targetProcessId);
                if (targetInbox == null)
                {
                    return new EventResult
                    {
                        Success = false,
                        EventId = Guid.NewGuid(),
                        ErrorMessage = $"Failed to access inbox for Process {targetProcessId}"
                    };
                }

                // Build message envelope
                var message = MessageEnvelopeHelper.BuildMessage(toMachineId, eventName, SerializeEventData(eventData));

                // Write to target process's inbox
                bool written = await targetInbox.WriteAsync(message, timeoutMs: 1000, _cancellationTokenSource.Token);

                if (!written)
                {
                    return new EventResult
                    {
                        Success = false,
                        EventId = Guid.NewGuid(),
                        ErrorMessage = $"Failed to write message to Process {targetProcessId} inbox within timeout. Buffer may be full."
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
        /// Gets or creates a ring buffer for writing to the target process's inbox
        /// </summary>
        private SharedMemoryRingBuffer? GetOrCreateProcessInbox(int targetProcessId)
        {
            return _processInboxCache.GetOrAdd(targetProcessId, pid =>
            {
                try
                {
                    string inboxName = $"{_segmentBaseName}_Inbox_P{pid}";
                    var segment = new SharedMemorySegment(inboxName, 1024 * 1024, createNew: false);
                    return new SharedMemoryRingBuffer(segment);
                }
                catch (Exception ex)
                {
                    Log($"Failed to open inbox for Process {pid}: {ex.Message}");
                    return null!;
                }
            });
        }

        /// <summary>
        /// Background task that reads messages from THIS process's dedicated inbox
        /// </summary>
        private async Task ReadMessagesAsync(CancellationToken cancellationToken)
        {
            Log("Message reader started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read message from OUR inbox (no competition from other processes!)
                    var message = await _inboxBuffer.ReadAsync(timeoutMs: 100, cancellationToken);

                    if (message != null)
                    {
                        // Parse message
                        var (machineId, eventName, payload, timestamp) = MessageEnvelopeHelper.ParseMessage(message);

                        // Deliver locally via EventBusOrchestrator
                        var eventData = DeserializeEventData(payload);
                        await _localOrchestrator.SendEventAsync("SharedMemory", machineId, eventName, eventData);

                        Log($"Message delivered: {machineId} ({eventName}) [age={(DateTime.UtcNow.Ticks - timestamp) / 10000.0:F2}ms]");
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
        /// Gets statistics about THIS process's inbox buffer usage
        /// </summary>
        public SharedMemoryStats GetStats()
        {
            return _inboxSegment.GetStats();
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
            _registrySegment?.Dispose();
            _inboxSegment?.Dispose();

            // Dispose cached inbox buffers
            foreach (var inbox in _processInboxCache.Values)
            {
                // Note: We don't dispose the segments here as they're owned by other processes
                // Just clear the cache
            }
            _processInboxCache.Clear();

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
