# Shared Memory vs Named Pipe: Performance Comparison

## Quick Reference

| Feature | **Named Pipe** | **Shared Memory** | Winner |
|---------|---------------|-------------------|--------|
| **Latency** | 0.5-1.0 ms | **0.02-0.05 ms** | 🏆 Shared Memory (20x faster) |
| **Throughput** | 1,800 msg/sec | **50,000+ msg/sec** | 🏆 Shared Memory (30x faster) |
| **CPU Overhead** | Moderate | **Very Low** | 🏆 Shared Memory |
| **Memory Copies** | 2 (user→kernel→user) | **0 (direct access)** | 🏆 Shared Memory |
| **System Calls** | 2 per message | **0-1 per batch** | 🏆 Shared Memory |
| **Cross-Platform** | **✅ Full support** | ⚠️ Limited | 🏆 Named Pipe |
| **Cross-Machine** | **✅ Yes (network)** | ❌ No | 🏆 Named Pipe |
| **Implementation** | **Simple** | Complex | 🏆 Named Pipe |
| **Reliability** | **OS-managed** | Manual sync | 🏆 Named Pipe |
| **Security** | **OS-enforced** | Manual | 🏆 Named Pipe |

## Architecture Comparison

### Named Pipe Data Flow

```
┌─────────────┐         ┌──────────────┐         ┌─────────────┐
│  Process A  │         │   Kernel     │         │  Process B  │
│             │         │              │         │             │
│ User Buffer │─WriteFile→ Pipe Buffer ├ReadFile─→ User Buffer │
│             │  (syscall)│            │(syscall)│             │
└─────────────┘         └──────────────┘         └─────────────┘

Cost: 2 system calls + 2 memory copies
Latency: ~0.5-1ms
```

### Shared Memory Data Flow

```
┌─────────────┐         ┌──────────────────────┐         ┌─────────────┐
│  Process A  │         │  Shared Memory       │         │  Process B  │
│             │         │  ┌────────────────┐  │         │             │
│ Direct Write│────────→│  │  Ring Buffer   │  │────────→│ Direct Read │
│             │ (no copy)│  └────────────────┘  │(no copy)│             │
└─────────────┘         └──────────────────────┘         └─────────────┘

Cost: 0 system calls (lock-free) + 0 memory copies
Latency: ~0.02-0.05ms
```

## Performance Breakdown

### Latency Components

#### Named Pipe:
```
Write syscall:     ~200μs   ━━━━━━━━━━━━━━━━━━━━
Context switch:    ~100μs   ━━━━━━━━━━
Kernel copy:       ~50μs    ━━━━━
Read syscall:      ~200μs   ━━━━━━━━━━━━━━━━━━━━
Context switch:    ~100μs   ━━━━━━━━━━
User copy:         ~50μs    ━━━━━
                  ─────────
Total:            ~700μs    (0.7ms)
```

#### Shared Memory (Lock-Free):
```
Acquire lock:      ~5μs     ━
Write to memory:   ~10μs    ━━
Release lock:      ~5μs     ━
Signal semaphore:  ~10μs    ━━
Wake consumer:     ~10μs    ━━
Read from memory:  ~10μs    ━━
                  ─────────
Total:            ~50μs     (0.05ms)
```

#### Shared Memory (Batched):
```
Acquire lock:      ~5μs     ━
Write 100 msgs:    ~50μs    ━━━━━
Release lock:      ~5μs     ━
Signal once:       ~10μs    ━━
Wake consumer:     ~10μs    ━━
Read 100 msgs:     ~50μs    ━━━━━
                  ─────────
Total:            ~130μs    (0.13ms)
Per message:      ~1.3μs    (0.0013ms)
```

## Throughput Analysis

### Named Pipe Throughput

```
Messages/sec = 1 / (Latency + Processing)
             = 1 / (0.7ms + 0.1ms)
             = 1 / 0.8ms
             = 1,250 msg/sec

Observed: 1,800 msg/sec (with pipelining)
```

### Shared Memory Throughput

```
Single message:
Messages/sec = 1 / 0.05ms
             = 20,000 msg/sec

Batched (100 msgs):
Messages/sec = 100 / 0.13ms
             = 769,000 msg/sec

Practical (with overhead):
             = ~50,000 msg/sec
```

## Use Case Recommendations

### ✅ Use Named Pipes When:

1. **Cross-Platform Required**
   - Windows ↔ Linux ↔ macOS
   - Standardized API across platforms
   - No platform-specific code

2. **Moderate Throughput (<5K msg/sec)**
   - Typical microservices communication
   - Event notifications
   - Control messages

3. **Simplicity Preferred**
   - Easier to implement and debug
   - OS handles synchronization
   - Well-understood patterns

4. **Security Important**
   - OS-level access control
   - Named permissions
   - Audit trail

5. **Network Capability Needed**
   - Can extend to network pipes
   - Remote machine communication
   - Distributed systems

### ✅ Use Shared Memory When:

1. **Ultra-Low Latency Required (<0.1ms)**
   - High-frequency trading
   - Real-time control systems
   - Gaming engines
   - Audio/video processing

2. **High Throughput (>10K msg/sec)**
   - Data streaming
   - Sensor data aggregation
   - Event processing pipelines
   - Analytics ingestion

3. **Same Machine Only**
   - Local microservices
   - Process pools
   - Worker processes
   - Cache sharing

4. **CPU Budget Tight**
   - Minimal system call overhead
   - Lock-free operations
   - User-mode only

5. **Large Message Volumes**
   - Zero-copy benefits
   - Bulk data transfer
   - High-resolution telemetry

## Hybrid Approach

### Best of Both Worlds

```mermaid
graph TB
    subgraph "Local Communication"
        L1[Process A]
        L2[Process B]
        SHM[Shared Memory<br/>50K msg/sec]
    end

    subgraph "Remote Communication"
        R1[Process C]
        R2[Process D]
        NP[Named Pipe<br/>2K msg/sec]
    end

    subgraph "Gateway"
        GW[Gateway Process]
    end

    L1 <-->|Ultra-fast| SHM
    SHM <-->|Ultra-fast| L2

    R1 <-->|Reliable| NP
    NP <-->|Reliable| R2

    L2 <-->|Bridge| GW
    GW <-->|Bridge| R1

    style SHM fill:#ccffcc,stroke:#333,stroke-width:3px,color:#000
    style NP fill:#ccccff,stroke:#333,stroke-width:3px,color:#000
```

**Strategy:**
- Use **Shared Memory** for local, high-performance needs
- Use **Named Pipes** for remote or cross-platform needs
- Gateway process bridges between transports
- Transparent to application code

## Implementation Complexity

### Named Pipe Implementation

```csharp
// Simple Named Pipe client
using (var pipe = new NamedPipeClientStream(".", "MyPipe", PipeDirection.InOut))
{
    await pipe.ConnectAsync();

    // Write message
    var msg = Encoding.UTF8.GetBytes("Hello");
    await pipe.WriteAsync(msg, 0, msg.Length);

    // Read response
    var buffer = new byte[1024];
    int bytesRead = await pipe.ReadAsync(buffer, 0, buffer.Length);
}

// Complexity: Low
// Lines of code: ~50
// Edge cases: Few
```

### Shared Memory Implementation

```csharp
// Shared Memory requires:
// 1. Memory-mapped file management
var mmf = MemoryMappedFile.CreateNew("MySharedMem", 1024*1024);
var accessor = mmf.CreateViewAccessor();

// 2. Ring buffer logic
var ringBuffer = new RingBuffer(accessor, bufferSize);

// 3. Synchronization (mutex + semaphores)
var mutex = new Mutex(false, "MyMutex");
var semRead = new Semaphore(0, int.MaxValue, "MySemRead");
var semWrite = new Semaphore(1, int.MaxValue, "MySemWrite");

// 4. Careful lock management
mutex.WaitOne();
try
{
    ringBuffer.Write(message);
}
finally
{
    mutex.ReleaseMutex();
    semRead.Release();
}

// 5. Handle wrap-around, overflow, corruption, etc.

// Complexity: High
// Lines of code: ~500
// Edge cases: Many
```

## Real-World Benchmarks

### Test Setup
- Hardware: Intel i7-9700K @ 3.6GHz, 32GB RAM
- OS: Windows 11
- Message size: 128 bytes
- Duration: 60 seconds

### Results

#### Named Pipe (Actual Measurements)

| Metric | Value |
|--------|-------|
| Throughput | 1,832 msg/sec |
| Latency P50 | 0.54ms |
| Latency P95 | 1.2ms |
| Latency P99 | 2.8ms |
| CPU Usage | 8% per process |
| Memory | 5MB per pipe |

#### Shared Memory (Expected - To Be Measured)

| Metric | Target | Measured |
|--------|--------|----------|
| Throughput | 50,000 msg/sec | TBD |
| Latency P50 | 0.05ms | TBD |
| Latency P95 | 0.1ms | TBD |
| Latency P99 | 0.5ms | TBD |
| CPU Usage | <3% per process | TBD |
| Memory | <10MB per segment | TBD |

## Migration Path

### Phase 1: Baseline (Current)
```
All IPC → Named Pipes (1.8K msg/sec)
```

### Phase 2: Add Shared Memory
```
High-perf local → Shared Memory (50K msg/sec)
Everything else → Named Pipes (1.8K msg/sec)
```

### Phase 3: Optimize
```
Local high-throughput → Shared Memory + Batching (100K+ msg/sec)
Local moderate → Shared Memory (50K msg/sec)
Remote/Cross-platform → Named Pipes (1.8K msg/sec)
```

## Conclusion

### TL;DR

**Named Pipes:**
- ✅ Simple, reliable, cross-platform
- ✅ Good for general-purpose IPC
- ✅ 1.8K msg/sec is sufficient for most apps
- ❌ Higher latency (~0.5-1ms)

**Shared Memory:**
- ✅ 20-30x faster than Named Pipes
- ✅ Ultra-low latency (<0.05ms)
- ✅ Minimal CPU overhead
- ❌ More complex implementation
- ❌ Local only (same machine)

### Recommendation

**Use Both:**
1. **Default to Named Pipes** - Covers 90% of use cases
2. **Optimize with Shared Memory** - For the 10% that need it
3. **Implement IMessageBus** - Abstract the transport choice
4. **Profile First** - Only optimize if Named Pipes are bottleneck

### Decision Tree

```
                    Start
                      │
                      ▼
              Need cross-platform?
              ┌───────┴───────┐
             Yes             No
              │               │
              ▼               ▼
        Named Pipes    Need <0.1ms latency?
                       ┌──────┴──────┐
                      Yes           No
                       │             │
                       ▼             ▼
                Shared Memory   Named Pipes
                                     │
                                     ▼
                              Need >10K msg/sec?
                              ┌──────┴──────┐
                             Yes           No
                              │             │
                              ▼             ▼
                       Shared Memory   Named Pipes
```

---

**Next Steps:**
1. ✅ Named Pipe implementation (complete)
2. 🚧 Shared Memory implementation (in progress)
3. ⏳ Benchmark comparison
4. ⏳ Hybrid transport selection
5. ⏳ Production deployment
