using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XStateNet.Distributed.Registry;
using XStateNet.Distributed.Orchestration;

namespace XStateNet.Distributed.Kubernetes
{
    /// <summary>
    /// Kubernetes operator for managing XStateNet state machines as custom resources
    /// </summary>
    public class KubernetesStateMachineOperator : BackgroundService
    {
        private readonly IKubernetes _kubernetes;
        private readonly IStateMachineRegistry _registry;
        private readonly IStateMachineOrchestrator _orchestrator;
        private readonly ILogger<KubernetesStateMachineOperator> _logger;
        private readonly string _namespace;
        
        // Custom Resource Definition constants
        private const string CRD_GROUP = "xstatenet.io";
        private const string CRD_VERSION = "v1";
        private const string CRD_PLURAL = "statemachines";
        private const string CRD_KIND = "StateMachine";
        
        public KubernetesStateMachineOperator(
            IKubernetes kubernetes,
            IStateMachineRegistry registry,
            IStateMachineOrchestrator orchestrator,
            ILogger<KubernetesStateMachineOperator> logger,
            string @namespace = "default")
        {
            _kubernetes = kubernetes ?? throw new ArgumentNullException(nameof(kubernetes));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _namespace = @namespace;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // First, ensure CRD exists
            await EnsureCustomResourceDefinitionAsync();
            
            // Watch for StateMachine custom resources
            await WatchStateMachinesAsync(stoppingToken);
        }
        
        private async Task EnsureCustomResourceDefinitionAsync()
        {
            try
            {
                var crd = new V1CustomResourceDefinition
                {
                    ApiVersion = "apiextensions.k8s.io/v1",
                    Kind = "CustomResourceDefinition",
                    Metadata = new V1ObjectMeta
                    {
                        Name = $"{CRD_PLURAL}.{CRD_GROUP}"
                    },
                    Spec = new V1CustomResourceDefinitionSpec
                    {
                        Group = CRD_GROUP,
                        Versions = new List<V1CustomResourceDefinitionVersion>
                        {
                            new V1CustomResourceDefinitionVersion
                            {
                                Name = CRD_VERSION,
                                Served = true,
                                Storage = true,
                                Schema = new V1CustomResourceValidation
                                {
                                    OpenAPIV3Schema = new V1JSONSchemaProps
                                    {
                                        Type = "object",
                                        Properties = new Dictionary<string, V1JSONSchemaProps>
                                        {
                                            ["spec"] = new V1JSONSchemaProps
                                            {
                                                Type = "object",
                                                Properties = new Dictionary<string, V1JSONSchemaProps>
                                                {
                                                    ["definition"] = new V1JSONSchemaProps { Type = "string" },
                                                    ["replicas"] = new V1JSONSchemaProps { Type = "integer", DefaultProperty = 1 },
                                                    ["config"] = new V1JSONSchemaProps { Type = "object" },
                                                    ["resources"] = new V1JSONSchemaProps
                                                    {
                                                        Type = "object",
                                                        Properties = new Dictionary<string, V1JSONSchemaProps>
                                                        {
                                                            ["cpu"] = new V1JSONSchemaProps { Type = "string" },
                                                            ["memory"] = new V1JSONSchemaProps { Type = "string" }
                                                        }
                                                    },
                                                    ["transport"] = new V1JSONSchemaProps 
                                                    { 
                                                        Type = "string",
                                                        EnumProperty = new List<object> { "zeromq", "rabbitmq", "kafka" }
                                                    },
                                                    ["persistence"] = new V1JSONSchemaProps
                                                    {
                                                        Type = "object",
                                                        Properties = new Dictionary<string, V1JSONSchemaProps>
                                                        {
                                                            ["enabled"] = new V1JSONSchemaProps { Type = "boolean" },
                                                            ["storageClass"] = new V1JSONSchemaProps { Type = "string" },
                                                            ["size"] = new V1JSONSchemaProps { Type = "string" }
                                                        }
                                                    }
                                                }
                                            },
                                            ["status"] = new V1JSONSchemaProps
                                            {
                                                Type = "object",
                                                Properties = new Dictionary<string, V1JSONSchemaProps>
                                                {
                                                    ["phase"] = new V1JSONSchemaProps { Type = "string" },
                                                    ["readyReplicas"] = new V1JSONSchemaProps { Type = "integer" },
                                                    ["currentState"] = new V1JSONSchemaProps { Type = "string" },
                                                    ["lastTransition"] = new V1JSONSchemaProps { Type = "string" }
                                                }
                                            }
                                        }
                                    }
                                },
                                Subresources = new V1CustomResourceSubresources
                                {
                                    Status = new object(),
                                    Scale = new V1CustomResourceSubresourceScale
                                    {
                                        SpecReplicasPath = ".spec.replicas",
                                        StatusReplicasPath = ".status.readyReplicas"
                                    }
                                }
                            }
                        },
                        Scope = "Namespaced",
                        Names = new V1CustomResourceDefinitionNames
                        {
                            Plural = CRD_PLURAL,
                            Singular = "statemachine",
                            Kind = CRD_KIND,
                            ShortNames = new List<string> { "sm" }
                        }
                    }
                };
                
                try
                {
                    await _kubernetes.ApiextensionsV1.CreateCustomResourceDefinitionAsync(crd);
                    _logger.LogInformation("Created StateMachine CRD");
                }
                catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    _logger.LogInformation("StateMachine CRD already exists");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure CRD exists");
                throw;
            }
        }
        
        private async Task WatchStateMachinesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var listResponse = await _kubernetes.CustomObjects.ListNamespacedCustomObjectAsync(
                        group: CRD_GROUP,
                        version: CRD_VERSION,
                        namespaceParameter: _namespace,
                        plural: CRD_PLURAL,
                        watch: true,
                        cancellationToken: cancellationToken);
                    
                    // Process watch events
                    // In real implementation, this would use the Watch API properly
                    await ProcessStateMachineEventsAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error watching StateMachine resources");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        
        private async Task ProcessStateMachineEventsAsync(CancellationToken cancellationToken)
        {
            // This would handle ADDED, MODIFIED, DELETED events
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Reconcile a StateMachine custom resource with actual deployments
        /// </summary>
        public async Task ReconcileStateMachineAsync(StateMachineResource resource)
        {
            try
            {
                _logger.LogInformation("Reconciling StateMachine {Name}", resource.Metadata.Name);
                
                // Check if Deployment exists
                var deploymentName = $"sm-{resource.Metadata.Name}";
                V1Deployment? existingDeployment = null;
                
                try
                {
                    existingDeployment = await _kubernetes.AppsV1.ReadNamespacedDeploymentAsync(
                        deploymentName, _namespace);
                }
                catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Deployment doesn't exist, need to create it
                }
                
                if (existingDeployment == null)
                {
                    // Create new deployment
                    await CreateStateMachineDeploymentAsync(resource);
                }
                else
                {
                    // Update existing deployment if needed
                    await UpdateStateMachineDeploymentAsync(resource, existingDeployment);
                }
                
                // Create or update Service
                await EnsureStateMachineServiceAsync(resource);
                
                // Create or update ConfigMap for state machine definition
                await EnsureStateMachineConfigMapAsync(resource);
                
                // Update status
                await UpdateStateMachineStatusAsync(resource);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconcile StateMachine {Name}", resource.Metadata.Name);
                throw;
            }
        }
        
        private async Task CreateStateMachineDeploymentAsync(StateMachineResource resource)
        {
            var deployment = new V1Deployment
            {
                Metadata = new V1ObjectMeta
                {
                    Name = $"sm-{resource.Metadata.Name}",
                    NamespaceProperty = _namespace,
                    Labels = new Dictionary<string, string>
                    {
                        ["app"] = "xstatenet",
                        ["statemachine"] = resource.Metadata.Name,
                        ["managed-by"] = "xstatenet-operator"
                    }
                },
                Spec = new V1DeploymentSpec
                {
                    Replicas = resource.Spec.Replicas,
                    Selector = new V1LabelSelector
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            ["statemachine"] = resource.Metadata.Name
                        }
                    },
                    Template = new V1PodTemplateSpec
                    {
                        Metadata = new V1ObjectMeta
                        {
                            Labels = new Dictionary<string, string>
                            {
                                ["app"] = "xstatenet",
                                ["statemachine"] = resource.Metadata.Name
                            }
                        },
                        Spec = new V1PodSpec
                        {
                            Containers = new List<V1Container>
                            {
                                new V1Container
                                {
                                    Name = "statemachine",
                                    Image = "xstatenet/runtime:latest",
                                    Ports = new List<V1ContainerPort>
                                    {
                                        new V1ContainerPort { ContainerPort = 5000, Name = "api" },
                                        new V1ContainerPort { ContainerPort = 5555, Name = "zeromq" },
                                        new V1ContainerPort { ContainerPort = 9090, Name = "metrics" }
                                    },
                                    Env = new List<V1EnvVar>
                                    {
                                        new V1EnvVar { Name = "MACHINE_ID", Value = resource.Metadata.Name },
                                        new V1EnvVar { Name = "TRANSPORT", Value = resource.Spec.Transport },
                                        new V1EnvVar { Name = "REDIS_URL", Value = "redis://redis-service:6379" },
                                        new V1EnvVar { Name = "RABBITMQ_URL", Value = "amqp://rabbitmq-service:5672" }
                                    },
                                    Resources = new V1ResourceRequirements
                                    {
                                        Requests = new Dictionary<string, ResourceQuantity>
                                        {
                                            ["cpu"] = new ResourceQuantity(resource.Spec.Resources?.Cpu ?? "100m"),
                                            ["memory"] = new ResourceQuantity(resource.Spec.Resources?.Memory ?? "128Mi")
                                        },
                                        Limits = new Dictionary<string, ResourceQuantity>
                                        {
                                            ["cpu"] = new ResourceQuantity(resource.Spec.Resources?.Cpu ?? "500m"),
                                            ["memory"] = new ResourceQuantity(resource.Spec.Resources?.Memory ?? "512Mi")
                                        }
                                    },
                                    VolumeMounts = new List<V1VolumeMount>
                                    {
                                        new V1VolumeMount
                                        {
                                            Name = "config",
                                            MountPath = "/etc/xstatenet",
                                            ReadOnlyProperty = true
                                        }
                                    },
                                    LivenessProbe = new V1Probe
                                    {
                                        HttpGet = new V1HTTPGetAction
                                        {
                                            Path = "/health/live",
                                            Port = 5000
                                        },
                                        InitialDelaySeconds = 30,
                                        PeriodSeconds = 10
                                    },
                                    ReadinessProbe = new V1Probe
                                    {
                                        HttpGet = new V1HTTPGetAction
                                        {
                                            Path = "/health/ready",
                                            Port = 5000
                                        },
                                        InitialDelaySeconds = 10,
                                        PeriodSeconds = 5
                                    }
                                }
                            },
                            Volumes = new List<V1Volume>
                            {
                                new V1Volume
                                {
                                    Name = "config",
                                    ConfigMap = new V1ConfigMapVolumeSource
                                    {
                                        Name = $"sm-{resource.Metadata.Name}-config"
                                    }
                                }
                            }
                        }
                    }
                }
            };
            
            // Add persistence if enabled
            if (resource.Spec.Persistence?.Enabled == true)
            {
                deployment.Spec.Template.Spec.Volumes.Add(new V1Volume
                {
                    Name = "data",
                    PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                    {
                        ClaimName = $"sm-{resource.Metadata.Name}-pvc"
                    }
                });
                
                deployment.Spec.Template.Spec.Containers[0].VolumeMounts.Add(new V1VolumeMount
                {
                    Name = "data",
                    MountPath = "/data"
                });
                
                // Create PVC
                await CreatePersistentVolumeClaimAsync(resource);
            }
            
            await _kubernetes.AppsV1.CreateNamespacedDeploymentAsync(deployment, _namespace);
            _logger.LogInformation("Created deployment for StateMachine {Name}", resource.Metadata.Name);
        }
        
        private async Task UpdateStateMachineDeploymentAsync(StateMachineResource resource, V1Deployment deployment)
        {
            // Update replicas if changed
            if (deployment.Spec.Replicas != resource.Spec.Replicas)
            {
                deployment.Spec.Replicas = resource.Spec.Replicas;
                await _kubernetes.AppsV1.ReplaceNamespacedDeploymentAsync(deployment, deployment.Metadata.Name, _namespace);
                _logger.LogInformation("Updated replicas for StateMachine {Name} to {Replicas}", 
                    resource.Metadata.Name, resource.Spec.Replicas);
            }
        }
        
        private async Task EnsureStateMachineServiceAsync(StateMachineResource resource)
        {
            var serviceName = $"sm-{resource.Metadata.Name}-svc";
            
            try
            {
                await _kubernetes.CoreV1.ReadNamespacedServiceAsync(serviceName, _namespace);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Service doesn't exist, create it
                var service = new V1Service
                {
                    Metadata = new V1ObjectMeta
                    {
                        Name = serviceName,
                        NamespaceProperty = _namespace,
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = "xstatenet",
                            ["statemachine"] = resource.Metadata.Name
                        }
                    },
                    Spec = new V1ServiceSpec
                    {
                        Selector = new Dictionary<string, string>
                        {
                            ["statemachine"] = resource.Metadata.Name
                        },
                        Ports = new List<V1ServicePort>
                        {
                            new V1ServicePort { Name = "api", Port = 5000, TargetPort = 5000 },
                            new V1ServicePort { Name = "zeromq", Port = 5555, TargetPort = 5555 },
                            new V1ServicePort { Name = "metrics", Port = 9090, TargetPort = 9090 }
                        },
                        Type = "ClusterIP"
                    }
                };
                
                await _kubernetes.CoreV1.CreateNamespacedServiceAsync(service, _namespace);
                _logger.LogInformation("Created service for StateMachine {Name}", resource.Metadata.Name);
            }
        }
        
        private async Task EnsureStateMachineConfigMapAsync(StateMachineResource resource)
        {
            var configMapName = $"sm-{resource.Metadata.Name}-config";
            
            var configMap = new V1ConfigMap
            {
                Metadata = new V1ObjectMeta
                {
                    Name = configMapName,
                    NamespaceProperty = _namespace
                },
                Data = new Dictionary<string, string>
                {
                    ["definition.json"] = resource.Spec.Definition,
                    ["config.json"] = System.Text.Json.JsonSerializer.Serialize(resource.Spec.Config)
                }
            };
            
            try
            {
                await _kubernetes.CoreV1.ReplaceNamespacedConfigMapAsync(configMap, configMapName, _namespace);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await _kubernetes.CoreV1.CreateNamespacedConfigMapAsync(configMap, _namespace);
            }
        }
        
        private async Task CreatePersistentVolumeClaimAsync(StateMachineResource resource)
        {
            var pvc = new V1PersistentVolumeClaim
            {
                Metadata = new V1ObjectMeta
                {
                    Name = $"sm-{resource.Metadata.Name}-pvc",
                    NamespaceProperty = _namespace
                },
                Spec = new V1PersistentVolumeClaimSpec
                {
                    AccessModes = new List<string> { "ReadWriteOnce" },
                    StorageClassName = resource.Spec.Persistence?.StorageClass,
                    Resources = new V1VolumeResourceRequirements
                    {
                        Requests = new Dictionary<string, ResourceQuantity>
                        {
                            ["storage"] = new ResourceQuantity(resource.Spec.Persistence?.Size ?? "1Gi")
                        }
                    }
                }
            };
            
            try
            {
                await _kubernetes.CoreV1.CreateNamespacedPersistentVolumeClaimAsync(pvc, _namespace);
                _logger.LogInformation("Created PVC for StateMachine {Name}", resource.Metadata.Name);
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // PVC already exists
            }
        }
        
        private async Task UpdateStateMachineStatusAsync(StateMachineResource resource)
        {
            try
            {
                // Get deployment status
                var deploymentName = $"sm-{resource.Metadata.Name}";
                var deployment = await _kubernetes.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, _namespace);
                
                // Update custom resource status
                var status = new StateMachineStatus
                {
                    Phase = deployment.Status.ReadyReplicas == deployment.Spec.Replicas ? "Ready" : "Pending",
                    ReadyReplicas = deployment.Status.ReadyReplicas ?? 0,
                    LastTransition = DateTime.UtcNow.ToString("O")
                };
                
                // Get current state from registry
                var info = await _registry.GetAsync(resource.Metadata.Name);
                if (info != null)
                {
                    status.CurrentState = info.CurrentState;
                }
                
                // Update status subresource
                var patch = new
                {
                    status = status
                };
                
                await _kubernetes.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                    new V1Patch(patch, V1Patch.PatchType.MergePatch),
                    CRD_GROUP,
                    CRD_VERSION,
                    _namespace,
                    CRD_PLURAL,
                    resource.Metadata.Name);
                
                _logger.LogDebug("Updated status for StateMachine {Name}", resource.Metadata.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status for StateMachine {Name}", resource.Metadata.Name);
            }
        }
    }
    
    /// <summary>
    /// Custom Resource Definition for StateMachine
    /// </summary>
    public class StateMachineResource
    {
        public V1ObjectMeta Metadata { get; set; } = new();
        public StateMachineSpec Spec { get; set; } = new();
        public StateMachineStatus Status { get; set; } = new();
    }
    
    public class StateMachineSpec
    {
        public string Definition { get; set; } = string.Empty;
        public int Replicas { get; set; } = 1;
        public Dictionary<string, object> Config { get; set; } = new();
        public ResourceRequirements? Resources { get; set; }
        public string Transport { get; set; } = "zeromq";
        public PersistenceConfig? Persistence { get; set; }
    }
    
    public class StateMachineStatus
    {
        public string Phase { get; set; } = "Pending";
        public int ReadyReplicas { get; set; }
        public string? CurrentState { get; set; }
        public string? LastTransition { get; set; }
    }
    
    public class ResourceRequirements
    {
        public string Cpu { get; set; } = "100m";
        public string Memory { get; set; } = "128Mi";
    }
    
    public class PersistenceConfig
    {
        public bool Enabled { get; set; }
        public string? StorageClass { get; set; }
        public string Size { get; set; } = "1Gi";
    }
}