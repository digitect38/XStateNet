using XStateNet.SharedMemory.Core;

namespace XStateNet.SharedMemory
{
    /// <summary>
    /// High-performance lock-free ring buffer for shared memory message passing
    /// Uses memory-mapped file with atomic operations for synchronization
    /// Target: 50,000+ msg/sec, 0.02-0.05ms latency
    /// </summary>
    public class SharedMemoryRingBuffer
    {
        private readonly SharedMemorySegment _segment;
        private readonly long _bufferSize;

        public SharedMemoryRingBuffer(SharedMemorySegment segment)
        {
            _segment = segment ?? throw new ArgumentNullException(nameof(segment));
            _bufferSize = segment.BufferSize;
        }

        /// <summary>
        /// Writes a message to the ring buffer
        /// Returns true if successful, false if timeout or buffer full
        /// </summary>
        public async Task<bool> WriteAsync(byte[] message, int timeoutMs, CancellationToken cancellationToken)
        {
            if (message == null || message.Length == 0)
                throw new ArgumentException("Message cannot be null or empty");

            if (message.Length > _bufferSize / 2)
                throw new ArgumentException($"Message too large ({message.Length} bytes). Maximum: {_bufferSize / 2}");

            var startTime = DateTime.UtcNow;

            while (true)
            {
                // Check timeout
                if (timeoutMs > 0 && (DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                    return false;

                // Check cancellation
                if (cancellationToken.IsCancellationRequested)
                    return false;

                // Acquire header lock
                if (!_segment.AcquireHeaderLock(timeoutMs: 100))
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                bool bufferFull = false;
                bool writeSuccess = false;

                try
                {
                    var header = _segment.ReadHeader();

                    // Calculate available space
                    long usedSpace = CalculateUsedSpace(header);
                    long freeSpace = _bufferSize - usedSpace;

                    // Check if message fits (need space for message + 4-byte length prefix)
                    if (freeSpace < message.Length + 4)
                    {
                        // Buffer full - wait for reader to consume
                        bufferFull = true;
                        // Don't continue here - let finally block release the lock
                    }
                    else
                    {
                        // Write message length prefix
                        long writePos = header.WritePosition;
                        WriteLengthPrefix(writePos, message.Length);
                        writePos = IncrementPosition(writePos, 4);

                        // Write message data (handle wrap-around)
                        if (writePos + message.Length <= _bufferSize)
                        {
                            // Contiguous write
                            _segment.WriteData(writePos, message, 0, message.Length);
                            writePos = IncrementPosition(writePos, message.Length);
                        }
                        else
                        {
                            // Wrapped write (in two parts)
                            int firstPart = (int)(_bufferSize - writePos);
                            int secondPart = message.Length - firstPart;

                            _segment.WriteData(writePos, message, 0, firstPart);
                            _segment.WriteData(0, message, firstPart, secondPart);

                            writePos = secondPart;
                        }

                        // Update header
                        header.WritePosition = writePos;
                        header.MessageCount++;
                        _segment.WriteHeader(header);

                        // Signal data available
                        _segment.SignalDataAvailable();

                        writeSuccess = true;
                    }
                }
                finally
                {
                    _segment.ReleaseHeaderLock();
                }

                // If write succeeded, return success
                if (writeSuccess)
                    return true;

                // If buffer was full, wait before retrying
                if (bufferFull)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }
            }
        }

        /// <summary>
        /// Reads a message from the ring buffer
        /// Returns null if timeout or no messages available
        /// </summary>
        public async Task<byte[]?> ReadAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;

            while (true)
            {
                // Check timeout
                if (timeoutMs > 0 && (DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                    return null;

                // Check cancellation
                if (cancellationToken.IsCancellationRequested)
                    return null;

                // Wait for data with short timeout
                bool dataAvailable = _segment.WaitForData(timeoutMs: 100);

                // Acquire header lock
                if (!_segment.AcquireHeaderLock(timeoutMs: 100))
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                bool bufferEmpty = false;
                byte[]? message = null;

                try
                {
                    var header = _segment.ReadHeader();

                    // Check if buffer is empty
                    if (header.ReadPosition == header.WritePosition)
                    {
                        // No data - set flag and let finally release the lock
                        bufferEmpty = true;
                    }
                    else
                    {

                        // Read message length prefix
                        long readPos = header.ReadPosition;
                        int messageLength = ReadLengthPrefix(readPos);
                        readPos = IncrementPosition(readPos, 4);

                        // Validate message length
                        if (messageLength <= 0 || messageLength > _bufferSize / 2)
                        {
                            throw new InvalidOperationException($"Invalid message length: {messageLength}");
                        }

                        // Read message data (handle wrap-around)
                        message = new byte[messageLength];

                        if (readPos + messageLength <= _bufferSize)
                        {
                            // Contiguous read
                            _segment.ReadData(readPos, message, 0, messageLength);
                            readPos = IncrementPosition(readPos, messageLength);
                        }
                        else
                        {
                            // Wrapped read (in two parts)
                            int firstPart = (int)(_bufferSize - readPos);
                            int secondPart = messageLength - firstPart;

                            _segment.ReadData(readPos, message, 0, firstPart);
                            _segment.ReadData(0, message, firstPart, secondPart);

                            readPos = secondPart;
                        }

                        // Update header
                        header.ReadPosition = readPos;
                        header.MessageCount--;
                        _segment.WriteHeader(header);

                        // Signal space available
                        _segment.SignalSpaceAvailable();
                    }
                }
                finally
                {
                    _segment.ReleaseHeaderLock();
                }

                // If we read a message, return it
                if (message != null)
                    return message;

                // If buffer was empty, wait before retrying
                if (bufferEmpty)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }
            }
        }

        /// <summary>
        /// Tries to read a message without waiting
        /// Returns null if no messages available
        /// </summary>
        public byte[]? TryRead()
        {
            if (!_segment.AcquireHeaderLock(timeoutMs: 100))
                return null;

            try
            {
                var header = _segment.ReadHeader();

                // Check if buffer is empty
                if (header.ReadPosition == header.WritePosition)
                    return null;

                // Read message length prefix
                long readPos = header.ReadPosition;
                int messageLength = ReadLengthPrefix(readPos);
                readPos = IncrementPosition(readPos, 4);

                // Read message data
                byte[] message = new byte[messageLength];

                if (readPos + messageLength <= _bufferSize)
                {
                    _segment.ReadData(readPos, message, 0, messageLength);
                    readPos = IncrementPosition(readPos, messageLength);
                }
                else
                {
                    int firstPart = (int)(_bufferSize - readPos);
                    int secondPart = messageLength - firstPart;

                    _segment.ReadData(readPos, message, 0, firstPart);
                    _segment.ReadData(0, message, firstPart, secondPart);

                    readPos = secondPart;
                }

                // Update header
                header.ReadPosition = readPos;
                header.MessageCount--;
                _segment.WriteHeader(header);

                return message;
            }
            finally
            {
                _segment.ReleaseHeaderLock();
            }
        }

        /// <summary>
        /// Gets the number of messages currently in the buffer
        /// </summary>
        public long GetMessageCount()
        {
            var header = _segment.ReadHeader();
            return header.MessageCount;
        }

        /// <summary>
        /// Gets statistics about buffer usage
        /// </summary>
        public (long usedSpace, long freeSpace, double usagePercent) GetBufferStats()
        {
            var header = _segment.ReadHeader();
            long usedSpace = CalculateUsedSpace(header);
            long freeSpace = _bufferSize - usedSpace;
            double usagePercent = (usedSpace * 100.0) / _bufferSize;

            return (usedSpace, freeSpace, usagePercent);
        }

        private long CalculateUsedSpace(SharedMemoryHeader header)
        {
            if (header.WritePosition >= header.ReadPosition)
            {
                return header.WritePosition - header.ReadPosition;
            }
            else
            {
                return _bufferSize - header.ReadPosition + header.WritePosition;
            }
        }

        private long IncrementPosition(long position, long increment)
        {
            long newPosition = position + increment;
            if (newPosition >= _bufferSize)
                newPosition -= _bufferSize;
            return newPosition;
        }

        private void WriteLengthPrefix(long position, int length)
        {
            byte[] buffer = BitConverter.GetBytes(length);
            _segment.WriteData(position, buffer, 0, 4);
        }

        private int ReadLengthPrefix(long position)
        {
            byte[] buffer = new byte[4];
            _segment.ReadData(position, buffer, 0, 4);
            return BitConverter.ToInt32(buffer, 0);
        }
    }
}
