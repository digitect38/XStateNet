using System.Runtime.InteropServices;
using System.Text;
using XStateNet.SharedMemory.Core;

namespace XStateNet.SharedMemory
{
    /// <summary>
    /// Process registration structure in shared memory
    /// Fixed size for direct memory mapping (256 bytes, cache-line aligned)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 256)]
    public struct ProcessRegistration
    {
        public int ProcessId;
        public int Status; // 0=Inactive, 1=Active, 2=ShuttingDown
        public long LastHeartbeat;
        public int MachineCount;
        public int Reserved1;
        public int Reserved2;
        public int Reserved3;

        // Fixed-size arrays
        public unsafe fixed byte ProcessName[64];
        public unsafe fixed byte MachineName[128];

        public const int Size = 256;
        public const int MaxProcesses = 128;

        public string GetProcessName()
        {
            unsafe
            {
                fixed (byte* ptr = ProcessName)
                {
                    int length = 0;
                    while (length < 64 && ptr[length] != 0) length++;
                    return Encoding.UTF8.GetString(ptr, length);
                }
            }
        }

        public void SetProcessName(string name)
        {
            unsafe
            {
                fixed (byte* ptr = ProcessName)
                {
                    var bytes = Encoding.UTF8.GetBytes(name);
                    int length = Math.Min(bytes.Length, 63);
                    Marshal.Copy(bytes, 0, (IntPtr)ptr, length);
                    ptr[length] = 0; // Null terminator
                }
            }
        }
    }

    /// <summary>
    /// Machine registration entry in shared memory
    /// Maps machine IDs to process IDs for routing
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 128)]
    public struct MachineRegistration
    {
        public int ProcessId;
        public int Status; // 0=Inactive, 1=Active
        public long RegisteredAt;
        public int Reserved1;

        public unsafe fixed byte MachineId[96];

        public const int Size = 128;
        public const int MaxMachines = 1024;

        public string GetMachineId()
        {
            unsafe
            {
                fixed (byte* ptr = MachineId)
                {
                    int length = 0;
                    while (length < 96 && ptr[length] != 0) length++;
                    return Encoding.UTF8.GetString(ptr, length);
                }
            }
        }

        public void SetMachineId(string machineId)
        {
            unsafe
            {
                fixed (byte* ptr = MachineId)
                {
                    var bytes = Encoding.UTF8.GetBytes(machineId);
                    int length = Math.Min(bytes.Length, 95);
                    Marshal.Copy(bytes, 0, (IntPtr)ptr, length);
                    ptr[length] = 0; // Null terminator
                }
            }
        }
    }

    /// <summary>
    /// Process Registry Header in shared memory
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 64)]
    public struct ProcessRegistryHeader
    {
        public uint MagicNumber; // 0x50524547 = "PREG"
        public uint Version;
        public int ProcessCount;
        public int MachineCount;
        public int NextProcessId;
        public int Reserved1;
        public long LastUpdate;
        public long Reserved2;
        public long Reserved3;
        public long Reserved4;

        public const int Size = 64;
    }

    /// <summary>
    /// Manages process and machine registry in shared memory
    /// Provides discovery and routing for state machines across processes
    /// </summary>
    public class ProcessRegistry : IDisposable
    {
        private const uint MAGIC_NUMBER = 0x50524547; // "PREG"
        private const uint VERSION = 1;
        private const int HEADER_OFFSET = 0;
        private const int PROCESS_TABLE_OFFSET = 64;
        private const int MACHINE_TABLE_OFFSET = PROCESS_TABLE_OFFSET + (ProcessRegistration.Size * ProcessRegistration.MaxProcesses);

        private readonly SharedMemorySegment _segment;
        private readonly Mutex _registryMutex;
        private readonly Timer _heartbeatTimer;
        private ProcessRegistration _thisProcess;
        private bool _disposed;

        public ProcessRegistry(SharedMemorySegment segment)
        {
            _segment = segment ?? throw new ArgumentNullException(nameof(segment));
            _registryMutex = new Mutex(false, $"Global\\{segment.Name}_RegistryMutex");

            // Initialize header if we're the first process
            if (_segment.IsInitialized)
            {
                InitializeHeaderIfNeeded();
            }

            // Start heartbeat timer (every 1 second)
            _heartbeatTimer = new Timer(SendHeartbeat, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Registers a new process in the registry
        /// </summary>
        public ProcessRegistration RegisterProcess(string processName)
        {
            _registryMutex.WaitOne();
            try
            {
                var header = ReadHeader();

                // Find free slot or reuse inactive slot
                int processId = header.NextProcessId++;
                int slotIndex = FindFreeProcessSlot();

                var registration = new ProcessRegistration
                {
                    ProcessId = processId,
                    Status = 1, // Active
                    LastHeartbeat = DateTime.UtcNow.Ticks,
                    MachineCount = 0
                };
                registration.SetProcessName(processName);

                // Write to shared memory
                WriteProcess(slotIndex, registration);

                // Update header
                header.ProcessCount++;
                header.LastUpdate = DateTime.UtcNow.Ticks;
                WriteHeader(header);

                _thisProcess = registration;
                return registration;
            }
            finally
            {
                _registryMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Unregisters a process from the registry
        /// </summary>
        public void UnregisterProcess(int processId)
        {
            _registryMutex.WaitOne();
            try
            {
                int slotIndex = FindProcessSlot(processId);
                if (slotIndex >= 0)
                {
                    var registration = ReadProcess(slotIndex);
                    registration.Status = 0; // Inactive
                    WriteProcess(slotIndex, registration);

                    var header = ReadHeader();
                    header.ProcessCount--;
                    header.LastUpdate = DateTime.UtcNow.Ticks;
                    WriteHeader(header);
                }
            }
            finally
            {
                _registryMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Registers a machine in the registry
        /// </summary>
        public void RegisterMachine(int processId, string machineId)
        {
            _registryMutex.WaitOne();
            try
            {
                int slotIndex = FindFreeMachineSlot();

                var registration = new MachineRegistration
                {
                    ProcessId = processId,
                    Status = 1, // Active
                    RegisteredAt = DateTime.UtcNow.Ticks
                };
                registration.SetMachineId(machineId);

                WriteMachine(slotIndex, registration);

                // Update global header
                var header = ReadHeader();
                header.MachineCount++;
                header.LastUpdate = DateTime.UtcNow.Ticks;
                WriteHeader(header);

                // Update process-specific machine count
                int processSlot = FindProcessSlot(processId);
                if (processSlot >= 0)
                {
                    var processReg = ReadProcess(processSlot);
                    processReg.MachineCount++;
                    WriteProcess(processSlot, processReg);
                }
            }
            finally
            {
                _registryMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Unregisters a machine from the registry
        /// </summary>
        public void UnregisterMachine(int processId, string machineId)
        {
            _registryMutex.WaitOne();
            try
            {
                int slotIndex = FindMachineSlot(machineId);
                if (slotIndex >= 0)
                {
                    var registration = ReadMachine(slotIndex);
                    registration.Status = 0; // Inactive
                    WriteMachine(slotIndex, registration);

                    // Update global header
                    var header = ReadHeader();
                    header.MachineCount--;
                    header.LastUpdate = DateTime.UtcNow.Ticks;
                    WriteHeader(header);

                    // Update process-specific machine count
                    int processSlot = FindProcessSlot(processId);
                    if (processSlot >= 0)
                    {
                        var processReg = ReadProcess(processSlot);
                        if (processReg.MachineCount > 0)
                            processReg.MachineCount--;
                        WriteProcess(processSlot, processReg);
                    }
                }
            }
            finally
            {
                _registryMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Finds which process owns a given machine
        /// Returns -1 if not found
        /// </summary>
        public int FindMachineProcess(string machineId)
        {
            _registryMutex.WaitOne();
            try
            {
                for (int i = 0; i < MachineRegistration.MaxMachines; i++)
                {
                    var registration = ReadMachine(i);
                    if (registration.Status == 1 && registration.GetMachineId() == machineId)
                    {
                        return registration.ProcessId;
                    }
                }
                return -1;
            }
            finally
            {
                _registryMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Gets all active processes
        /// </summary>
        public ProcessRegistration[] GetAllProcesses()
        {
            _registryMutex.WaitOne();
            try
            {
                var processes = new List<ProcessRegistration>();
                for (int i = 0; i < ProcessRegistration.MaxProcesses; i++)
                {
                    var registration = ReadProcess(i);
                    if (registration.Status == 1)
                    {
                        processes.Add(registration);
                    }
                }
                return processes.ToArray();
            }
            finally
            {
                _registryMutex.ReleaseMutex();
            }
        }

        private void InitializeHeaderIfNeeded()
        {
            _registryMutex.WaitOne();
            try
            {
                var header = ReadHeader();
                if (header.MagicNumber != MAGIC_NUMBER)
                {
                    // First time initialization
                    header = new ProcessRegistryHeader
                    {
                        MagicNumber = MAGIC_NUMBER,
                        Version = VERSION,
                        ProcessCount = 0,
                        MachineCount = 0,
                        NextProcessId = 1,
                        LastUpdate = DateTime.UtcNow.Ticks
                    };
                    WriteHeader(header);
                }
            }
            finally
            {
                _registryMutex.ReleaseMutex();
            }
        }

        private ProcessRegistryHeader ReadHeader()
        {
            var buffer = new byte[ProcessRegistryHeader.Size];
            _segment.ReadData(HEADER_OFFSET, buffer, 0, buffer.Length);

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<ProcessRegistryHeader>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private void WriteHeader(ProcessRegistryHeader header)
        {
            var buffer = new byte[ProcessRegistryHeader.Size];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(header, handle.AddrOfPinnedObject(), false);
                _segment.WriteData(HEADER_OFFSET, buffer, 0, buffer.Length);
            }
            finally
            {
                handle.Free();
            }
        }

        private ProcessRegistration ReadProcess(int index)
        {
            var buffer = new byte[ProcessRegistration.Size];
            long offset = PROCESS_TABLE_OFFSET + (index * ProcessRegistration.Size);
            _segment.ReadData(offset, buffer, 0, buffer.Length);

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<ProcessRegistration>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private void WriteProcess(int index, ProcessRegistration registration)
        {
            var buffer = new byte[ProcessRegistration.Size];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(registration, handle.AddrOfPinnedObject(), false);
                long offset = PROCESS_TABLE_OFFSET + (index * ProcessRegistration.Size);
                _segment.WriteData(offset, buffer, 0, buffer.Length);
            }
            finally
            {
                handle.Free();
            }
        }

        private MachineRegistration ReadMachine(int index)
        {
            var buffer = new byte[MachineRegistration.Size];
            long offset = MACHINE_TABLE_OFFSET + (index * MachineRegistration.Size);
            _segment.ReadData(offset, buffer, 0, buffer.Length);

            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<MachineRegistration>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        private void WriteMachine(int index, MachineRegistration registration)
        {
            var buffer = new byte[MachineRegistration.Size];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(registration, handle.AddrOfPinnedObject(), false);
                long offset = MACHINE_TABLE_OFFSET + (index * MachineRegistration.Size);
                _segment.WriteData(offset, buffer, 0, buffer.Length);
            }
            finally
            {
                handle.Free();
            }
        }

        private int FindFreeProcessSlot()
        {
            for (int i = 0; i < ProcessRegistration.MaxProcesses; i++)
            {
                var registration = ReadProcess(i);
                if (registration.Status == 0)
                    return i;
            }
            throw new InvalidOperationException("Process registry is full");
        }

        private int FindProcessSlot(int processId)
        {
            for (int i = 0; i < ProcessRegistration.MaxProcesses; i++)
            {
                var registration = ReadProcess(i);
                if (registration.ProcessId == processId && registration.Status == 1)
                    return i;
            }
            return -1;
        }

        private int FindFreeMachineSlot()
        {
            for (int i = 0; i < MachineRegistration.MaxMachines; i++)
            {
                var registration = ReadMachine(i);
                if (registration.Status == 0)
                    return i;
            }
            throw new InvalidOperationException("Machine registry is full");
        }

        private int FindMachineSlot(string machineId)
        {
            for (int i = 0; i < MachineRegistration.MaxMachines; i++)
            {
                var registration = ReadMachine(i);
                if (registration.Status == 1 && registration.GetMachineId() == machineId)
                    return i;
            }
            return -1;
        }

        private void SendHeartbeat(object? state)
        {
            if (_disposed || _thisProcess.ProcessId == 0) return;

            try
            {
                _registryMutex.WaitOne();
                try
                {
                    if (_disposed) return; // Double check after acquiring mutex

                    int slotIndex = FindProcessSlot(_thisProcess.ProcessId);
                    if (slotIndex >= 0)
                    {
                        var registration = ReadProcess(slotIndex);
                        registration.LastHeartbeat = DateTime.UtcNow.Ticks;
                        WriteProcess(slotIndex, registration);
                    }
                }
                finally
                {
                    _registryMutex.ReleaseMutex();
                }
            }
            catch (ObjectDisposedException)
            {
                // Segment was disposed, ignore
            }
            catch (Exception)
            {
                // Ignore other errors during heartbeat
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop heartbeat timer
            _heartbeatTimer?.Dispose();

            // Dispose mutex
            _registryMutex?.Dispose();
        }
    }
}
