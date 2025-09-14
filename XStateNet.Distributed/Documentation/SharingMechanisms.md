# XStateNet Pub/Sub Sharing Mechanisms

## Overview
The XStateNet pub/sub architecture implements multiple sharing mechanisms to support different deployment scenarios and performance requirements.

## 1. **In-Process Sharing (Shared Memory)**

### Standard Implementation (InMemoryEventBus)
- **Mechanism**: Direct object reference sharing
- **Scope**: Same process, multiple threads
- **Concurrency**: Thread-safe collections (`ConcurrentDictionary`, `ConcurrentQueue`)
- **Performance**: Nanosecond latency
- **Use Case**: Single application, multiple state machines

```csharp
// Objects are shared directly via references
public class InMemoryEventBus
{
    // Shared collections across threads
    private readonly ConcurrentDictionary<string, List<SubscriptionInfo>> _subscriptions;
    private readonly ConcurrentDictionary<string, Channel<StateMachineEvent>> _channels;
}
```

### Optimized Implementation (OptimizedInMemoryEventBus)
- **Mechanism**: Lock-free sharing with object pooling
- **Memory Model**: Uses volatile fields and memory barriers
- **Synchronization**: Minimal locking with `ReaderWriterLockSlim`
- **Optimization**: Object pooling to reduce allocations

```csharp
// Lock-free data structures for maximum performance
private readonly ConcurrentDictionary<string, SubscriptionSet> _topicSubscriptions;
private readonly Channel<PublishWorkItem> _publishChannel; // Lock-free channel
private volatile int _isConnected; // Volatile for thread visibility
```

## 2. **Inter-Process Communication (IPC)**

### ZeroMQ Transport
- **Mechanism**: Message passing via sockets
- **Protocols**: TCP, IPC (named pipes), InProc
- **Pattern**: Pub/Sub, Request/Reply, Push/Pull
- **Serialization**: MessagePack binary format

```csharp
public class ZeroMQTransport : IStateMachineTransport
{
    // Socket-based communication
    private PublisherSocket? _publisher;  // PUB socket for broadcasting
    private SubscriberSocket? _subscriber; // SUB socket for receiving
    private RouterSocket? _router;        // ROUTER for request/reply

    // Brokerless architecture - direct peer-to-peer
    public async Task SendAsync(StateMachineMessage message)
    {
        var data = MessagePackSerializer.Serialize(message);
        _publisher?.SendFrame(data);
    }
}
```

### Named Pipes / Unix Domain Sockets
- **Mechanism**: OS-level IPC
- **Performance**: Microsecond latency
- **Use Case**: Same machine, different processes

## 3. **Distributed Sharing (Network)**

### RabbitMQ Transport
- **Mechanism**: Message broker with persistent queues
- **Pattern**: Topic Exchange, Fanout, Direct routing
- **Guarantees**: At-least-once delivery, message persistence
- **Protocol**: AMQP 0.9.1

```csharp
public class RabbitMQEventBus : IStateMachineEventBus
{
    // Exchange-based routing for topic isolation
    private const string StateChangeExchange = "xstatenet.state.changes";  // Topic exchange
    private const string EventExchange = "xstatenet.events";               // Direct exchange
    private const string BroadcastExchange = "xstatenet.broadcast";        // Fanout exchange
    private const string GroupExchange = "xstatenet.groups";               // Topic exchange

    // Durable queues for reliability
    _channel.ExchangeDeclareAsync(StateChangeExchange, ExchangeType.Topic, durable: true);
}
```

### Redis Pub/Sub (Planned)
- **Mechanism**: In-memory data structure server
- **Pattern**: Pub/Sub channels, Pattern subscriptions
- **Performance**: Sub-millisecond latency
- **Use Case**: Cache-based event distribution

## 4. **Hybrid Sharing Mechanism**

### Location-Aware Transport Selection
```csharp
public enum MachineLocation
{
    SameThread,    // Direct method calls
    SameProcess,   // Shared memory with synchronization
    SameMachine,   // IPC (named pipes, Unix sockets)
    Remote         // Network (TCP, AMQP)
}

// Automatic transport selection based on location
private IStateMachineTransport SelectTransport(MachineLocation location)
{
    return location switch
    {
        MachineLocation.SameThread => new DirectTransport(),      // No serialization
        MachineLocation.SameProcess => new InMemoryTransport(),   // Shared collections
        MachineLocation.SameMachine => new ZeroMQTransport(),     // IPC sockets
        MachineLocation.Remote => new RabbitMQTransport(),        // Network broker
        _ => new InMemoryTransport()
    };
}
```

## 5. **Synchronization Mechanisms**

### Channels (Producer-Consumer)
```csharp
// Bounded channel with backpressure
_publishChannel = Channel.CreateBounded<StateMachineEvent>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait,  // Block when full
    SingleWriter = false,                     // Multiple producers
    SingleReader = true                       // Single consumer
});

// Unbounded for maximum throughput
_broadcastChannel = Channel.CreateUnbounded<BroadcastWorkItem>();
```

### Lock-Free Collections
```csharp
// Thread-safe without explicit locking
ConcurrentDictionary<string, SubscriptionSet> _subscriptions;
ConcurrentQueue<EventWorkItem> _eventQueue;
ConcurrentBag<StateMachineEvent> _eventPool;
```

### Reader-Writer Locks
```csharp
private class SubscriptionSet
{
    private readonly ReaderWriterLockSlim _lock = new();

    public void Notify(StateMachineEvent evt)
    {
        _lock.EnterReadLock();  // Multiple readers
        try
        {
            // Notify all subscriptions
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}
```

## 6. **Memory Sharing Patterns**

### Object Pooling
```csharp
// Shared pool of reusable objects
private readonly ObjectPool<StateMachineEvent> _eventPool;

// Rent from pool
var evt = _eventPool.Get();
evt.EventName = "StateChange";

// Return to pool
_eventPool.Return(evt);
```

### Copy-on-Write
```csharp
// Immutable data structures for safe sharing
public record StateChangeEvent
{
    public string OldState { get; init; }
    public string NewState { get; init; }
    // Immutable - safe to share across threads
}
```

### Zero-Copy Techniques
```csharp
// ArrayPool for large buffers
private readonly ArrayPool<byte> _byteArrayPool = ArrayPool<byte>.Shared;

// Rent buffer
var buffer = _byteArrayPool.Rent(size);
try
{
    // Use buffer
}
finally
{
    _byteArrayPool.Return(buffer, clearArray: true);
}
```

## 7. **Event Distribution Patterns**

### Topic-Based Routing
```csharp
// Hierarchical topics for selective subscription
"state.machine1.idle"      // Specific state change
"state.machine1.*"         // All states for machine1
"state.*"                  // All state changes
"*.machine1.*"             // All events for machine1
```

### Content-Based Filtering
```csharp
// Filter events based on content
service.SubscribeWithFilter(
    evt => evt.Payload?.ToString()?.Contains("important") == true,
    evt => ProcessImportantEvent(evt)
);
```

### Group-Based Distribution
```csharp
// Load balancing across group members
await PublishToGroupAsync("worker-group", "WORK_ITEM", payload);
// Only one member of the group receives the message
```

## Performance Characteristics

| Mechanism | Latency | Throughput | Reliability | Use Case |
|-----------|---------|------------|-------------|----------|
| **Same Thread** | <1ns | 10M+ msg/s | Perfect | Unit tests, single-threaded |
| **Shared Memory** | 1-100ns | 1-5M msg/s | Process crash | High-frequency trading |
| **IPC (Same Machine)** | 1-10μs | 100K-1M msg/s | Process isolation | Microservices |
| **Network (LAN)** | 100μs-1ms | 10K-100K msg/s | Network partitions | Distributed systems |
| **Network (WAN)** | 10-100ms | 1K-10K msg/s | Geographic distribution | Global systems |

## Best Practices

1. **Choose the Right Mechanism**
   - Use in-process for maximum performance
   - Use IPC for process isolation
   - Use network for distributed systems

2. **Optimize for Your Scenario**
   - High throughput: Use batching and object pooling
   - Low latency: Use lock-free structures
   - Reliability: Use persistent queues (RabbitMQ)

3. **Consider Failure Modes**
   - In-process: Thread safety, deadlocks
   - IPC: Process crashes, resource leaks
   - Network: Partitions, message loss

4. **Monitor Performance**
   - Track event rates and latencies
   - Monitor memory usage and GC
   - Watch for queue backlogs

## Configuration Examples

### High-Throughput Configuration
```csharp
var options = new EventServiceOptions
{
    BatchSize = 1000,                   // Large batches
    PublishChannelCapacity = 100000,    // Large buffers
    DropEventsWhenFull = true,          // Drop vs block
    BatchTimeout = TimeSpan.FromMilliseconds(10)
};
```

### Low-Latency Configuration
```csharp
var options = new EventServiceOptions
{
    BatchSize = 1,                      // No batching
    PublishChannelCapacity = 100,       // Small buffers
    DropEventsWhenFull = false,         // Never drop
    BatchTimeout = TimeSpan.Zero        // Immediate processing
};
```

### Reliable Configuration
```csharp
// Use RabbitMQ with persistence
var eventBus = new RabbitMQEventBus(connectionString)
{
    Durable = true,                     // Persist messages
    AutoAck = false,                    // Manual acknowledgment
    RetryPolicy = new ExponentialBackoff()
};
```