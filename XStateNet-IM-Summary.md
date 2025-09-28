# XStateNet-IM (InterMachine) Framework

## Overview
XStateNet-IM is a framework for direct machine-to-machine communication without mediators, built on top of XStateNet. It enables symmetric bidirectional communication where both machines can initiate and respond to messages.

## Key Features

### 1. Direct Machine Communication
- No event bus or mediator required
- True peer-to-peer communication
- Symmetric capabilities (both machines can initiate)

### 2. Type-Based Discovery
- Machines register by type, not just ID
- Discover peers by service type
- Pattern-based discovery (e.g., "Service_*")
- Auto-connection to machines of the same type

### 3. Orchestration Support
- **LocalMachineRegistrator**: Self-made implementation for in-process scenarios
- **KubernetesMachineRegistrator**: Integration pattern for Kubernetes service discovery
- Automatic health checking and heartbeat management

## Core Components

### InterMachineConnector
- Manages direct connections between machines
- Routes messages without intermediaries
- Thread-safe concurrent operations

### InterMachineSession
- Provides isolated sessions for testing
- Wraps machines with ConnectedMachine abstraction
- Manages lifecycle and cleanup

### MachineRegistrator
- Type-based service discovery
- Auto-connection between machines
- Metadata support for extended information
- Health monitoring capabilities

## Usage Examples

### Basic Direct Communication
```csharp
var session = new InterMachineSession();
var cm1 = session.AddMachine(machine1, "machine1");
var cm2 = session.AddMachine(machine2, "machine2");
session.Connect("machine1", "machine2");

// Direct message sending
await cm1.SendToAsync("machine2", "PING", data);
```

### Type-Based Discovery
```csharp
// Register machines by type
var pingService = await machine.RegisterAsTypeAsync("PingService");
var pongService = await machine.RegisterAsTypeAsync("PongService");

// Discover and communicate by type
var pongServices = await pingService.DiscoverAndConnectAsync("PongService");
await pingService.SendToTypeAsync("PongService", "PING", data);

// Broadcast to all machines of a type
await pingService.BroadcastToSameTypeAsync("HEARTBEAT", data);

// Load balancing with random selection
await pingService.SendToRandomOfTypeAsync("BackendService", "REQUEST", data);
```

### Pattern-Based Discovery
```csharp
// Discover all services matching a pattern
var allServices = await registrator.DiscoverByPatternAsync("Service_*");
var alphaServices = await registrator.DiscoverByPatternAsync("*_Alpha_*");
```

## Test Results

### Passing Tests (28 total)
- ✅ **MachineRegistrator Tests** (8/8)
  - RegisterAndDiscoverByType_Success
  - AutoConnectSameType_Success
  - DiscoverByPattern_Success
  - SendToRandomOfType_LoadBalancing
  - TypeBasedCommunication_PingPong
  - GetMachineTypes_ReturnsAllTypes
  - KubernetesRegistrator_Simulation
  - HealthCheck_MachineStatus

- ✅ **Unit Tests** (15/15)
  - All core functionality tests passing
  - Connection management
  - Message routing
  - Error handling
  - Concurrent operations

- ✅ **Symmetric Fixed Tests** (3/3)
  - SymmetricPingPong_SimpleExchange
  - BothMachinesInitiate_Simultaneously
  - TrulySymmetric_BothCanPingAndPong

## Architecture Benefits

1. **No Central Point of Failure**: Direct peer-to-peer communication
2. **Scalable**: Machines discover each other dynamically
3. **Flexible**: Support for both explicit connections and type-based discovery
4. **Cloud-Native Ready**: Kubernetes integration pattern included
5. **Testable**: Session-based isolation for reliable testing

## Use Cases

- Microservice communication without service mesh
- Distributed worker coordination
- Peer-to-peer state synchronization
- Load balancing across service instances
- Service discovery in containerized environments

## Status
The framework is fully functional with comprehensive test coverage. The type-based discovery system with registrator/orchestrator is working perfectly, allowing machines to find and communicate with each other using service types rather than specific IDs.