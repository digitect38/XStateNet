# XStateNet Kubernetes Integration

## Overview

The Kubernetes integration enables XStateNet to operate as a cloud-native, distributed state machine orchestration platform. It provides seamless deployment, scaling, and management of state machines across Kubernetes clusters.

## Architecture Components

### 1. Custom Resource Definition (CRD)
- **StateMachine**: Kubernetes custom resource for declarative state machine management
- Supports scaling, persistence, transport selection, and resource allocation
- Status subresource for real-time state tracking

### 2. Operator Pattern
The `KubernetesStateMachineOperator` implements the operator pattern to:
- Watch for StateMachine custom resources
- Reconcile desired state with actual deployments
- Manage Deployments, Services, ConfigMaps, and PersistentVolumeClaims
- Handle lifecycle events (create, update, delete, scale)

### 3. Distributed Components
- **Redis Registry**: Centralized state machine registry with heartbeat monitoring
- **RabbitMQ Event Bus**: Message-based communication between state machines
- **Orchestrator Service**: Coordinates complex workflows, sagas, and group operations

## Deployment

### Quick Start

1. **Install the operator:**
```bash
kubectl apply -f operator-deployment.yaml
```

2. **Deploy a state machine:**
```yaml
apiVersion: xstatenet.io/v1
kind: StateMachine
metadata:
  name: my-machine
spec:
  replicas: 3
  definition: |
    {
      "id": "my-machine",
      "initial": "idle",
      "states": {
        "idle": {
          "on": { "START": "running" }
        },
        "running": {
          "on": { "STOP": "idle" }
        }
      }
    }
  transport: rabbitmq
  persistence:
    enabled: true
    size: 5Gi
```

3. **Apply the manifest:**
```bash
kubectl apply -f my-machine.yaml
```

### Using Helm

```bash
# Add the XStateNet repository
helm repo add xstatenet https://charts.xstatenet.io

# Install with custom values
helm install xstatenet xstatenet/xstatenet \
  --set redis.enabled=true \
  --set rabbitmq.enabled=true \
  --set monitoring.enabled=true
```

## Features

### Scaling
```bash
# Manual scaling
kubectl scale statemachine/my-machine --replicas=10

# Auto-scaling with HPA
kubectl autoscale statemachine/my-machine --min=3 --max=20 --cpu-percent=70
```

### Monitoring
- Prometheus metrics exposed on `/metrics`
- Grafana dashboards for visualization
- Health checks and readiness probes
- Distributed tracing support

### High Availability
- Multi-replica deployments
- Anti-affinity rules for pod distribution
- Automatic failover via registry
- Pod Disruption Budgets

### Persistence
- StatefulSet support for ordered deployments
- PersistentVolumeClaims for state persistence
- Configurable storage classes
- Backup and restore capabilities

## Transport Options

### RabbitMQ (Default)
- Reliable message delivery
- Topic-based routing
- Request/response patterns
- Group messaging

### Kafka
- High-throughput event streaming
- Log-based persistence
- Partition-based scalability
- Event sourcing support

### ZeroMQ
- Low-latency communication
- Direct peer-to-peer messaging
- Pub/sub patterns
- No broker required

## Configuration

### Environment Variables
- `MACHINE_ID`: Unique identifier for the state machine
- `TRANSPORT`: Communication transport (rabbitmq, kafka, zeromq)
- `REDIS_URL`: Redis connection string for registry
- `RABBITMQ_URL`: RabbitMQ connection string
- `LOG_LEVEL`: Logging level (debug, info, warn, error)

### Resource Requirements
```yaml
resources:
  requests:
    cpu: 100m
    memory: 128Mi
  limits:
    cpu: 500m
    memory: 512Mi
```

### Persistence Configuration
```yaml
persistence:
  enabled: true
  storageClass: fast-ssd
  size: 10Gi
```

## Advanced Features

### Workflows
Execute complex workflows across multiple state machines:
```csharp
var workflow = new WorkflowDefinition
{
    Steps = new[]
    {
        new WorkflowStep { MachineId = "validator", EventName = "VALIDATE" },
        new WorkflowStep { MachineId = "processor", EventName = "PROCESS", DependsOn = ["validator"] },
        new WorkflowStep { MachineId = "notifier", EventName = "NOTIFY", DependsOn = ["processor"] }
    }
};
await orchestrator.ExecuteWorkflowAsync(workflow);
```

### Saga Pattern
Implement distributed transactions with automatic compensation:
```csharp
var saga = new SagaDefinition
{
    Steps = new[]
    {
        new SagaStep 
        { 
            MachineId = "payment", 
            Action = "CHARGE", 
            CompensationAction = "REFUND" 
        },
        new SagaStep 
        { 
            MachineId = "inventory", 
            Action = "RESERVE", 
            CompensationAction = "RELEASE" 
        }
    }
};
await orchestrator.ExecuteSagaAsync(saga);
```

### Group Coordination
Coordinate multiple state machines as a group:
```csharp
await orchestrator.CreateStateMachineGroupAsync("processors", 
    new GroupOptions { CoordinationType = GroupCoordinationType.LoadBalanced },
    "processor-1", "processor-2", "processor-3");

await orchestrator.SendGroupEventAsync("processors", "PROCESS", payload);
```

## Monitoring and Observability

### Metrics
- `xstatenet_events_total`: Total events processed
- `xstatenet_state_transitions_total`: State transitions count
- `xstatenet_event_processing_duration`: Event processing latency
- `xstatenet_machine_status`: Current machine status

### Health Endpoints
- `/health/live`: Liveness probe
- `/health/ready`: Readiness probe
- `/metrics`: Prometheus metrics

### Distributed Tracing
Supports OpenTelemetry for distributed tracing across state machines.

## Security

### RBAC
The operator requires minimal permissions:
- Read/Write: StateMachines, Deployments, Services, ConfigMaps, PVCs
- Read: Pods, Events
- Create: CustomResourceDefinitions (one-time)

### Network Policies
Optional network policies to restrict communication between components.

### TLS/SSL
Support for encrypted communication between state machines and infrastructure components.

## Troubleshooting

### Common Issues

1. **State machine not starting:**
   - Check pod logs: `kubectl logs -l statemachine=my-machine`
   - Verify Redis/RabbitMQ connectivity
   - Check resource limits

2. **Events not being processed:**
   - Verify event bus connectivity
   - Check queue bindings in RabbitMQ
   - Review state machine definition

3. **Persistence issues:**
   - Verify PVC is bound: `kubectl get pvc`
   - Check storage class availability
   - Review volume mount permissions

### Debug Commands
```bash
# View state machine status
kubectl get statemachines

# Describe state machine
kubectl describe statemachine my-machine

# View operator logs
kubectl logs -n xstatenet-system deployment/xstatenet-operator

# Access RabbitMQ management UI
kubectl port-forward -n xstatenet-system svc/rabbitmq 15672:15672
```

## Best Practices

1. **Resource Allocation**: Set appropriate resource requests and limits
2. **Persistence**: Enable persistence for stateful machines
3. **Monitoring**: Always enable monitoring in production
4. **Scaling**: Use HPA for dynamic scaling based on load
5. **High Availability**: Deploy Redis and RabbitMQ in HA mode
6. **Security**: Enable RBAC and network policies
7. **Backup**: Regularly backup persistent volumes

## Integration with CI/CD

### GitOps with ArgoCD
```yaml
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: xstatenet-machines
spec:
  source:
    repoURL: https://github.com/yourorg/state-machines
    path: manifests
    targetRevision: main
  destination:
    server: https://kubernetes.default.svc
  syncPolicy:
    automated:
      prune: true
      selfHeal: true
```

### Flux CD
```yaml
apiVersion: source.toolkit.fluxcd.io/v1beta2
kind: GitRepository
metadata:
  name: state-machines
spec:
  interval: 1m
  url: https://github.com/yourorg/state-machines
---
apiVersion: kustomize.toolkit.fluxcd.io/v1beta2
kind: Kustomization
metadata:
  name: state-machines
spec:
  interval: 10m
  sourceRef:
    kind: GitRepository
    name: state-machines
  path: ./manifests
  prune: true
```

## License

XStateNet is licensed under the MIT License. See LICENSE file for details.