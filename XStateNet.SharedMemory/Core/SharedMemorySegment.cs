using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace XStateNet.SharedMemory.Core
{
    /// <summary>
    /// Manages a shared memory segment for inter-process communication
    /// Provides ultra-low latency message passing via memory-mapped files
    /// </summary>
    public class SharedMemorySegment : IDisposable
    {
        private const uint MAGIC_NUMBER = 0x584D4950; // "XMIP" in hex
        private const uint VERSION = 1;
        private const int DEFAULT_BUFFER_SIZE = 1024 * 1024; // 1MB

        private readonly string _name;
        private readonly long _bufferSize;
        private readonly bool _isOwner;

        private MemoryMappedFile? _memoryMappedFile;
        private MemoryMappedViewAccessor? _accessor;
        private Mutex? _headerMutex;
        private Semaphore? _readSemaphore;
        private Semaphore? _writeSemaphore;
        private bool _disposed;

        public string Name => _name;
        public long BufferSize => _bufferSize;
        public bool IsInitialized => _memoryMappedFile != null;

        /// <summary>
        /// Creates or opens a shared memory segment
        /// </summary>
        public SharedMemorySegment(string name, long bufferSize = DEFAULT_BUFFER_SIZE, bool createNew = true)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _bufferSize = bufferSize;
            _isOwner = createNew;

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // Create or open memory-mapped file
                if (_isOwner)
                {
                    _memoryMappedFile = MemoryMappedFile.CreateNew(
                        _name,
                        Marshal.SizeOf<SharedMemoryHeader>() + _bufferSize,
                        MemoryMappedFileAccess.ReadWrite
                    );
                }
                else
                {
                    _memoryMappedFile = MemoryMappedFile.OpenExisting(
                        _name,
                        MemoryMappedFileRights.ReadWrite
                    );
                }

                // Create accessor for the entire segment
                _accessor = _memoryMappedFile.CreateViewAccessor();

                // Create synchronization primitives
                _headerMutex = new Mutex(false, $"Global\\{_name}_HeaderMutex");
                _readSemaphore = new Semaphore(0, int.MaxValue, $"Global\\{_name}_ReadSem");
                _writeSemaphore = new Semaphore(1, int.MaxValue, $"Global\\{_name}_WriteSem");

                // Initialize header if owner
                if (_isOwner)
                {
                    InitializeHeader();
                }
                else
                {
                    ValidateHeader();
                }
            }
            catch (Exception ex)
            {
                Dispose();
                throw new InvalidOperationException($"Failed to initialize shared memory segment '{_name}'", ex);
            }
        }

        private void InitializeHeader()
        {
            var header = new SharedMemoryHeader
            {
                MagicNumber = MAGIC_NUMBER,
                Version = VERSION,
                BufferSize = _bufferSize,
                WritePosition = 0,
                ReadPosition = 0,
                MessageCount = 0,
                Reserved1 = 0,
                Reserved2 = 0,
                Reserved3 = 0
            };

            WriteHeader(header);
        }

        private void ValidateHeader()
        {
            var header = ReadHeader();

            if (header.MagicNumber != MAGIC_NUMBER)
            {
                throw new InvalidOperationException($"Invalid magic number in shared memory '{_name}'. Expected 0x{MAGIC_NUMBER:X}, got 0x{header.MagicNumber:X}");
            }

            if (header.Version != VERSION)
            {
                throw new InvalidOperationException($"Version mismatch in shared memory '{_name}'. Expected {VERSION}, got {header.Version}");
            }

            if (header.BufferSize != _bufferSize)
            {
                throw new InvalidOperationException($"Buffer size mismatch in shared memory '{_name}'. Expected {_bufferSize}, got {header.BufferSize}");
            }
        }

        /// <summary>
        /// Reads the shared memory header
        /// </summary>
        public SharedMemoryHeader ReadHeader()
        {
            if (_accessor == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            SharedMemoryHeader header;
            _accessor.Read(0, out header);
            return header;
        }

        /// <summary>
        /// Writes the shared memory header
        /// </summary>
        public void WriteHeader(SharedMemoryHeader header)
        {
            if (_accessor == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            _accessor.Write(0, ref header);

            // Memory barrier to ensure visibility across processes
            Thread.MemoryBarrier();
        }

        /// <summary>
        /// Acquires the header mutex for safe updates
        /// </summary>
        public bool AcquireHeaderLock(int timeoutMs = 1000)
        {
            if (_headerMutex == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            return _headerMutex.WaitOne(timeoutMs);
        }

        /// <summary>
        /// Releases the header mutex
        /// </summary>
        public void ReleaseHeaderLock()
        {
            if (_headerMutex == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            _headerMutex.ReleaseMutex();
        }

        /// <summary>
        /// Signals that data is available for reading
        /// </summary>
        public void SignalDataAvailable()
        {
            if (_readSemaphore == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            _readSemaphore.Release();
        }

        /// <summary>
        /// Waits for data to become available
        /// </summary>
        public bool WaitForData(int timeoutMs = -1)
        {
            if (_readSemaphore == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            return _readSemaphore.WaitOne(timeoutMs);
        }

        /// <summary>
        /// Signals that space is available for writing
        /// </summary>
        public void SignalSpaceAvailable()
        {
            if (_writeSemaphore == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            _writeSemaphore.Release();
        }

        /// <summary>
        /// Waits for space to become available
        /// </summary>
        public bool WaitForSpace(int timeoutMs = -1)
        {
            if (_writeSemaphore == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            return _writeSemaphore.WaitOne(timeoutMs);
        }

        /// <summary>
        /// Writes data at the specified position in the ring buffer
        /// </summary>
        public void WriteData(long position, byte[] data, int offset, int count)
        {
            if (_accessor == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            long bufferOffset = Marshal.SizeOf<SharedMemoryHeader>() + position;
            _accessor.WriteArray(bufferOffset, data, offset, count);

            // CRITICAL: Memory barrier to ensure write visibility across processes
            Thread.MemoryBarrier();

            // Flush to ensure data is written to backing store and visible to other processes
            _accessor.Flush();
        }

        /// <summary>
        /// Reads data from the specified position in the ring buffer
        /// </summary>
        public int ReadData(long position, byte[] buffer, int offset, int count)
        {
            if (_accessor == null)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));

            // CRITICAL: Memory barrier to ensure we read fresh data, not stale cached data
            Thread.MemoryBarrier();

            long bufferOffset = Marshal.SizeOf<SharedMemoryHeader>() + position;
            return _accessor.ReadArray(bufferOffset, buffer, offset, count);
        }

        /// <summary>
        /// Gets statistics about the shared memory segment
        /// </summary>
        public SharedMemoryStats GetStats()
        {
            var header = ReadHeader();

            return new SharedMemoryStats
            {
                BufferSize = header.BufferSize,
                WritePosition = header.WritePosition,
                ReadPosition = header.ReadPosition,
                MessageCount = header.MessageCount,
                UsedSpace = CalculateUsedSpace(header),
                FreeSpace = CalculateFreeSpace(header)
            };
        }

        private long CalculateUsedSpace(SharedMemoryHeader header)
        {
            if (header.WritePosition >= header.ReadPosition)
            {
                return header.WritePosition - header.ReadPosition;
            }
            else
            {
                return header.BufferSize - header.ReadPosition + header.WritePosition;
            }
        }

        private long CalculateFreeSpace(SharedMemoryHeader header)
        {
            return header.BufferSize - CalculateUsedSpace(header);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _accessor?.Dispose();
            _memoryMappedFile?.Dispose();
            _headerMutex?.Dispose();
            _readSemaphore?.Dispose();
            _writeSemaphore?.Dispose();

            _disposed = true;
        }
    }

    /// <summary>
    /// Header structure for shared memory segment (64 bytes, cache-aligned)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 64)]
    public struct SharedMemoryHeader
    {
        public uint MagicNumber;
        public uint Version;
        public long BufferSize;
        public long WritePosition;
        public long ReadPosition;
        public long MessageCount;
        public long Reserved1;
        public long Reserved2;
        public long Reserved3;
    }

    /// <summary>
    /// Statistics about shared memory usage
    /// </summary>
    public struct SharedMemoryStats
    {
        public long BufferSize;
        public long WritePosition;
        public long ReadPosition;
        public long MessageCount;
        public long UsedSpace;
        public long FreeSpace;

        public double UsagePercentage => BufferSize > 0 ? (UsedSpace * 100.0 / BufferSize) : 0;
    }
}
