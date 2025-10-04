# Shared Memory Inter-Process Communication Design

## Executive Summary

This document outlines the design for a high-performance shared memory-based IPC mechanism for XStateNet, complementing the existing Named Pipe implementation. Shared memory provides significantly lower latency and higher throughput for local inter-process communication.

## Performance Comparison

| Transport | Throughput | Latency | Use Case |
|-----------|-----------|---------|----------|
| **Named Pipes** | 1,832 msg/sec | 0.54ms | General IPC, cross-platform |
| **Shared Memory** | **50,000+ msg/sec** | **<0.05ms** | High-performance local IPC |
| **In-Memory** | 100,000+ msg/sec | <0.01ms | Single process |

## Architecture Overview

### High-Level Design

```mermaid
graph TB
    subgraph "Process A"
        A_PRODUCER[Producer]
        A_WRITER[Shared Memory Writer]
    end

    subgraph "Shared Memory Segment"
        HEADER[Header Block]
        RING[Ring Buffer]
        META[Metadata]
    end

    subgraph "Process B"
        B_READER[Shared Memory Reader]
        B_CONSUMER[Consumer]
    end

    subgraph "Synchronization"
        SEM_WRITE[Write Semaphore]
        SEM_READ[Read Semaphore]
        MUTEX[Mutex Lock]
    end

    A_PRODUCER --> A_WRITER
    A_WRITER -->|Lock| MUTEX
    A_WRITER -->|Write| RING
    A_WRITER -->|Signal| SEM_READ

    SEM_READ -->|Wait| B_READER
    B_READER -->|Lock| MUTEX
    B_READER -->|Read| RING
    B_READER -->|Signal| SEM_WRITE

    MUTEX -.->|Protects| HEADER
    MUTEX -.->|Protects| META

    style RING fill:#ccffcc,stroke:#333,stroke-width:3px,color:#000
    style MUTEX fill:#ffcccc,stroke:#333,stroke-width:3px,color:#000
```

### Memory Layout

```mermaid
graph TB
    subgraph "Shared Memory Block"
        MAGIC[Magic Number<br/>4 bytes]
        VERSION[Version<br/>4 bytes]
        SIZE[Buffer Size<br/>8 bytes]

        WRITE_POS[Write Position<br/>8 bytes]
        READ_POS[Read Position<br/>8 bytes]
        MSG_COUNT[Message Count<br/>8 bytes]

        RING_START[Ring Buffer Start]
        RING_DATA[Message Data<br/>Configurable Size]
        RING_END[Ring Buffer End]
    end

    MAGIC --> VERSION
    VERSION --> SIZE
    SIZE --> WRITE_POS
    WRITE_POS --> READ_POS
    READ_POS --> MSG_COUNT
    MSG_COUNT --> RING_START
    RING_START --> RING_DATA
    RING_DATA --> RING_END

    style MAGIC fill:#ffcccc,color:#000
    style VERSION fill:#ccffcc,color:#000
    style SIZE fill:#ccccff,color:#000
    style WRITE_POS fill:#ffccff,color:#000
    style READ_POS fill:#ffccff,color:#000
```

## Detailed Design

### 1. Shared Memory Segment Structure

```csharp
/// <summary>
/// Header structure for shared memory segment
/// Total size: 64 bytes (cache-line aligned)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SharedMemoryHeader
{
    // Magic number for validation (0x584D4950 = "XMIP")
    public uint MagicNumber;

    // Protocol version
    public uint Version;

    // Total buffer size in bytes
    public long BufferSize;

    // Write cursor position
    public long WritePosition;

    // Read cursor position
    public long ReadPosition;

    // Total messages in buffer
    public long MessageCount;

    // Reserved for future use (padding to 64 bytes)
    public long Reserved1;
    public long Reserved2;
    public long Reserved3;
}

/// <summary>
/// Message envelope in shared memory
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MessageEnvelope
{
    // Message length (including header)
    public int Length;

    // Message type/event name length
    public int EventNameLength;

    // Target machine ID length
    public int MachineIdLength;

    // Payload length
    public int PayloadLength;

    // Timestamp (ticks)
    public long Timestamp;

    // Followed by variable-length data:
    // - EventName (UTF-8)
    // - MachineId (UTF-8)
    // - Payload (binary)
}
```

### 2. Ring Buffer Implementation

```mermaid
graph LR
    subgraph "Ring Buffer"
        M1[Message 1]
        M2[Message 2]
        M3[Message 3]
        EMPTY[Empty Space]
        M4[Message 4]
    end

    WRITE[Write Cursor] -.->|Next Write| EMPTY
    READ[Read Cursor] -.->|Next Read| M1

    M1 --> M2
    M2 --> M3
    M3 --> EMPTY
    EMPTY --> M4
    M4 -.->|Wrap Around| M1

    style WRITE fill:#ccffcc,color:#000
    style READ fill:#ffcccc,color:#000
    style EMPTY fill:#ccccff,color:#000
```

**Ring Buffer Characteristics:**
- **Lock-Free Reads**: Single reader, no lock needed for read cursor
- **Lock-Free Writes**: Single writer, no lock needed for write cursor
- **Wrap-Around**: Circular buffer reuses memory efficiently
- **Size Detection**: Each message has length prefix
- **Overflow Handling**: Blocks or drops based on policy

### 3. Synchronization Mechanisms

```mermaid
stateDiagram-v2
    [*] --> EMPTY

    EMPTY --> WRITING: Producer Acquires Lock
    WRITING --> HAS_DATA: Write Complete + Signal
    HAS_DATA --> READING: Consumer Acquires Lock
    READING --> EMPTY: Read Complete

    HAS_DATA --> HAS_DATA: More Messages Available

    note right of WRITING
        Mutex protects write cursor
        Prevents concurrent writes
    end note

    note right of READING
        Semaphore signals data ready
        Consumer waits efficiently
    end note
```

**Synchronization Primitives:**

1. **Mutex (Mutual Exclusion)**
   - Protects shared header updates
   - Short critical sections (<1μs)
   - Named mutex for cross-process

2. **Semaphore (Event Signaling)**
   - Read semaphore: Signals data available
   - Write semaphore: Signals space available
   - Efficient wait/notify mechanism

3. **Memory Barriers**
   - Ensures visibility across processes
   - Prevents reordering issues
   - Critical for correctness

## Implementation

### Core Classes

```mermaid
classDiagram
    class SharedMemoryMessageBus {
        -MemoryMappedFile _memoryMappedFile
        -MemoryMappedViewAccessor _accessor
        -Semaphore _readSemaphore
        -Semaphore _writeSemaphore
        -Mutex _headerMutex

        +SendAsync(machineId, eventName, data)
        +ReceiveAsync()
        +Dispose()
    }

    class SharedMemoryWriter {
        -MemoryMappedViewAccessor _accessor
        -long _writePosition

        +WriteMessage(envelope)
        +UpdateWriteCursor()
        -EnsureSpace(size)
    }

    class SharedMemoryReader {
        -MemoryMappedViewAccessor _accessor
        -long _readPosition

        +ReadMessage()
        +UpdateReadCursor()
        -WaitForData()
    }

    class RingBuffer {
        -byte[] _buffer
        -long _head
        -long _tail
        -long _capacity

        +Write(data)
        +Read()
        +AvailableSpace()
        +AvailableData()
    }

    SharedMemoryMessageBus --> SharedMemoryWriter
    SharedMemoryMessageBus --> SharedMemoryReader
    SharedMemoryWriter --> RingBuffer
    SharedMemoryReader --> RingBuffer
```

### Message Flow Sequence

```mermaid
sequenceDiagram
    participant Producer
    participant Writer
    participant SharedMem as Shared Memory
    participant Semaphore
    participant Reader
    participant Consumer

    Producer->>Writer: SendAsync(msg)
    Writer->>Writer: Acquire Mutex
    Writer->>SharedMem: Check Available Space

    alt Space Available
        Writer->>SharedMem: Write Message
        Writer->>SharedMem: Update Write Cursor
        Writer->>Writer: Release Mutex
        Writer->>Semaphore: Signal ReadSemaphore

        Semaphore->>Reader: Wake Up
        Reader->>Reader: Acquire Mutex
        Reader->>SharedMem: Read Message
        Reader->>SharedMem: Update Read Cursor
        Reader->>Reader: Release Mutex
        Reader->>Semaphore: Signal WriteSemaphore
        Reader->>Consumer: Deliver Message
    else Buffer Full
        Writer->>Writer: Wait on WriteSemaphore
        Writer->>Writer: Retry Write
    end
```

## Performance Optimizations

### 1. Memory Alignment

```csharp
// Align to CPU cache line (64 bytes)
[StructLayout(LayoutKind.Sequential, Pack = 64)]
public struct CacheAlignedHeader
{
    // Frequently read fields
    public volatile long ReadPosition;

    // Padding to next cache line
    private readonly long _pad1, _pad2, _pad3, _pad4, _pad5, _pad6, _pad7;

    // Frequently written fields
    public volatile long WritePosition;

    // Padding to next cache line
    private readonly long _pad8, _pad9, _pad10, _pad11, _pad12, _pad13, _pad14;
}
```

**Benefits:**
- Prevents false sharing between processes
- Each cursor on separate cache line
- Reduces cache coherency traffic
- 10-20% performance improvement

### 2. Lock-Free Read/Write

```mermaid
graph TB
    subgraph "Single Writer (Lock-Free)"
        W1[Check Write Position]
        W2[Calculate Space]
        W3[Write Data]
        W4[Memory Barrier]
        W5[Update Write Cursor]
    end

    subgraph "Single Reader (Lock-Free)"
        R1[Check Read Position]
        R2[Memory Barrier]
        R3[Read Data]
        R4[Update Read Cursor]
    end

    W1 --> W2
    W2 --> W3
    W3 --> W4
    W4 --> W5

    R1 --> R2
    R2 --> R3
    R3 --> R4

    style W4 fill:#ffcccc,stroke:#333,stroke-width:3px,color:#000
    style R2 fill:#ccffcc,stroke:#333,stroke-width:3px,color:#000
```

**Lock-Free Protocol:**
- Single producer: No write lock needed
- Single consumer: No read lock needed
- Memory barriers ensure ordering
- Atomic cursor updates with Interlocked

### 3. Batching

```csharp
public class BatchedSharedMemoryWriter
{
    private List<MessageEnvelope> _batch = new();
    private const int MaxBatchSize = 100;
    private const int MaxBatchDelayMs = 1;

    public async Task SendAsync(string machineId, string eventName, object data)
    {
        _batch.Add(CreateEnvelope(machineId, eventName, data));

        if (_batch.Count >= MaxBatchSize)
        {
            await FlushBatch();
        }
    }

    private async Task FlushBatch()
    {
        // Single mutex acquisition for entire batch
        await _mutex.WaitAsync();
        try
        {
            foreach (var msg in _batch)
            {
                WriteMessage(msg);
            }
            UpdateWriteCursor();
        }
        finally
        {
            _mutex.Release();
        }

        _batch.Clear();
        _readSemaphore.Release();
    }
}
```

**Batching Benefits:**
- Amortizes synchronization overhead
- 5-10x throughput improvement
- Reduced context switching
- Better cache utilization

## Comparison: Named Pipe vs Shared Memory

### Architecture Differences

```mermaid
graph TB
    subgraph "Named Pipe Architecture"
        NP_PROC1[Process 1]
        NP_KERNEL[Kernel Buffer]
        NP_PROC2[Process 2]

        NP_PROC1 -->|Write System Call| NP_KERNEL
        NP_KERNEL -->|Read System Call| NP_PROC2
    end

    subgraph "Shared Memory Architecture"
        SM_PROC1[Process 1]
        SM_SHARED[Shared Memory]
        SM_PROC2[Process 2]

        SM_PROC1 -->|Direct Write| SM_SHARED
        SM_SHARED -->|Direct Read| SM_PROC2
    end

    style NP_KERNEL fill:#ffcccc,stroke:#333,stroke-width:3px,color:#000
    style SM_SHARED fill:#ccffcc,stroke:#333,stroke-width:3px,color:#000
```

### Performance Characteristics

| Aspect | Named Pipe | Shared Memory |
|--------|-----------|---------------|
| **Latency** | 0.5-1ms | 0.02-0.05ms |
| **Throughput** | 2K msg/sec | 50K+ msg/sec |
| **CPU Usage** | Moderate (syscalls) | Low (user-mode) |
| **System Calls** | 2 per message | 0-1 per batch |
| **Memory Copy** | 2x (user→kernel→user) | 0x (shared) |
| **Scalability** | Good | Excellent |
| **Cross-Machine** | ❌ No | ❌ No |
| **Cross-Platform** | ✅ Yes | ✅ Yes (limited) |

### When to Use Each

**Named Pipes:**
- Cross-platform requirement
- Moderate throughput needs (<5K msg/sec)
- Process isolation important
- Simpler implementation preferred

**Shared Memory:**
- Ultra-low latency required (<0.1ms)
- High throughput needs (>10K msg/sec)
- Same machine communication
- Willing to manage synchronization

## Implementation Plan

### Phase 1: Core Infrastructure

```mermaid
graph LR
    subgraph "Week 1-2"
        T1[Shared Memory Manager]
        T2[Ring Buffer Implementation]
        T3[Synchronization Primitives]
    end

    subgraph "Week 3-4"
        T4[Writer Implementation]
        T5[Reader Implementation]
        T6[Message Serialization]
    end

    T1 --> T4
    T2 --> T4
    T3 --> T4

    T1 --> T5
    T2 --> T5
    T3 --> T5

    T4 --> T6
    T5 --> T6

    style T1 fill:#ccffcc,color:#000
    style T4 fill:#ccccff,color:#000
```

**Deliverables:**
1. `SharedMemorySegment.cs` - Memory management
2. `RingBuffer.cs` - Circular buffer logic
3. `SharedMemorySynchronization.cs` - Mutex/Semaphore wrappers
4. `SharedMemoryWriter.cs` - Producer implementation
5. `SharedMemoryReader.cs` - Consumer implementation
6. `MessageSerializer.cs` - Efficient serialization

### Phase 2: Integration

```mermaid
graph TB
    subgraph "Week 5-6"
        I1[IMessageBus Implementation]
        I2[Orchestrator Integration]
        I3[Error Handling]
    end

    subgraph "Week 7-8"
        I4[Performance Testing]
        I5[Benchmarking Suite]
        I6[Documentation]
    end

    I1 --> I4
    I2 --> I4
    I3 --> I4

    I4 --> I5
    I5 --> I6

    style I1 fill:#ffcccc,color:#000
    style I4 fill:#ccffcc,color:#000
```

**Deliverables:**
1. `SharedMemoryMessageBus.cs` - IMessageBus implementation
2. Integration with `EventBusOrchestrator`
3. Comprehensive error handling
4. Performance benchmark suite
5. Technical documentation

### Phase 3: Advanced Features

```mermaid
graph LR
    subgraph "Week 9-10"
        A1[Multi-Producer Support]
        A2[Batching Optimization]
        A3[Zero-Copy Transfer]
    end

    subgraph "Week 11-12"
        A4[Monitoring Integration]
        A5[Resilience Patterns]
        A6[Production Hardening]
    end

    A1 --> A4
    A2 --> A5
    A3 --> A6

    style A2 fill:#ccccff,color:#000
    style A5 fill:#ffccff,color:#000
```

**Deliverables:**
1. Lock-free multi-producer support
2. Adaptive batching algorithm
3. Zero-copy for large messages
4. Monitoring and metrics
5. Circuit breaker integration
6. Production deployment guide

## Code Structure

```
XStateNet.SharedMemory/
├── Core/
│   ├── SharedMemorySegment.cs          # Memory-mapped file wrapper
│   ├── SharedMemoryHeader.cs           # Header structure
│   ├── MessageEnvelope.cs              # Message format
│   └── RingBuffer.cs                   # Circular buffer
│
├── Synchronization/
│   ├── NamedMutex.cs                   # Cross-process mutex
│   ├── NamedSemaphore.cs               # Cross-process semaphore
│   └── MemoryBarrier.cs                # Memory fence helpers
│
├── IO/
│   ├── SharedMemoryWriter.cs           # Producer
│   ├── SharedMemoryReader.cs           # Consumer
│   └── MessageSerializer.cs            # Serialization
│
├── Transport/
│   ├── SharedMemoryMessageBus.cs       # IMessageBus impl
│   ├── SharedMemoryClient.cs           # Client wrapper
│   └── SharedMemoryServer.cs           # Server wrapper
│
├── Optimization/
│   ├── BatchedWriter.cs                # Batching support
│   ├── ZeroCopyWriter.cs               # Large message optimization
│   └── CacheAlignedStructs.cs          # Performance structures
│
└── Diagnostics/
    ├── SharedMemoryMonitor.cs          # Performance monitoring
    ├── SharedMemoryMetrics.cs          # Metrics collection
    └── DebugView.cs                    # Debug visualization
```

## Error Handling & Resilience

### Error Scenarios

```mermaid
graph TB
    START[Operation Start]

    CHECK1{Shared Memory<br/>Accessible?}
    CHECK2{Buffer Full?}
    CHECK3{Process Crashed?}
    CHECK4{Corruption<br/>Detected?}

    HANDLE1[Reconnect Logic]
    HANDLE2[Wait/Drop Policy]
    HANDLE3[Cleanup & Recreate]
    HANDLE4[Reinitialize]

    SUCCESS[Success]
    FAIL[Report Error]

    START --> CHECK1

    CHECK1 -->|No| HANDLE1
    CHECK1 -->|Yes| CHECK2

    CHECK2 -->|Yes| HANDLE2
    CHECK2 -->|No| CHECK3

    CHECK3 -->|Yes| HANDLE3
    CHECK3 -->|No| CHECK4

    CHECK4 -->|Yes| HANDLE4
    CHECK4 -->|No| SUCCESS

    HANDLE1 --> FAIL
    HANDLE2 --> SUCCESS
    HANDLE3 --> SUCCESS
    HANDLE4 --> SUCCESS

    style CHECK4 fill:#ffcccc,stroke:#333,stroke-width:3px,color:#000
    style SUCCESS fill:#ccffcc,stroke:#333,stroke-width:3px,color:#000
```

### Recovery Strategies

1. **Buffer Overflow**
   - Drop oldest messages (ring buffer)
   - Block producer (backpressure)
   - Expand buffer dynamically

2. **Process Crash**
   - Detect via heartbeat
   - Cleanup orphaned resources
   - Reinitialize segment

3. **Corruption Detection**
   - Magic number validation
   - CRC checksums
   - Structural validation

4. **Deadlock Prevention**
   - Timeout on all locks
   - Deadlock detection
   - Automatic recovery

## Expected Performance

### Benchmark Targets

| Metric | Target | Measured |
|--------|--------|----------|
| **Latency (p50)** | <0.05ms | TBD |
| **Latency (p99)** | <0.1ms | TBD |
| **Throughput** | >50K msg/sec | TBD |
| **CPU Usage** | <5% per process | TBD |
| **Memory** | <10MB per channel | TBD |

### Test Scenarios

```mermaid
graph LR
    subgraph "Latency Test"
        L1[Single Message]
        L2[Measure RTT]
        L3[100K iterations]
    end

    subgraph "Throughput Test"
        T1[Sustained Load]
        T2[1M messages]
        T3[Measure rate]
    end

    subgraph "Stress Test"
        S1[10 Producers]
        S2[10 Consumers]
        S3[24 hours]
    end

    L1 --> L2
    L2 --> L3

    T1 --> T2
    T2 --> T3

    S1 --> S2
    S2 --> S3

    style L2 fill:#ccffcc,color:#000
    style T3 fill:#ccccff,color:#000
    style S3 fill:#ffcccc,color:#000
```

## Conclusion

Shared memory IPC provides:

**✅ Advantages:**
- 10-50x lower latency than Named Pipes
- 20-30x higher throughput
- Zero-copy data transfer
- Minimal CPU overhead
- Predictable performance

**⚠️ Considerations:**
- More complex implementation
- Same-machine only
- Careful synchronization required
- Platform-specific optimizations

**Recommended Use Cases:**
- High-frequency trading systems
- Real-time control systems
- Gaming/simulation engines
- Low-latency microservices
- Performance-critical workflows

---

**Next Steps:**
1. Review and approve design
2. Begin Phase 1 implementation
3. Create benchmark baseline
4. Incremental feature rollout
5. Production validation
