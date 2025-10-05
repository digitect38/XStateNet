using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using XStateNet.Orchestration;

namespace XStateNet.Semi.Standards;

/// <summary>
/// SEMI E164 Enhanced Data Collection Management
/// Extends E134 with trace data collection, streaming, and advanced filtering
/// </summary>
public class E164EnhancedDataCollectionManager
{
    private readonly string _equipmentId;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly E134DataCollectionManager _baseManager;
    private readonly ConcurrentDictionary<string, TraceDataPlan> _tracePlans = new();
    private readonly ConcurrentDictionary<string, StreamingSession> _streamingSessions = new();

    public string MachineId => $"E164_ENHANCED_DCM_{_equipmentId}";

    public E164EnhancedDataCollectionManager(string equipmentId, EventBusOrchestrator orchestrator, E134DataCollectionManager baseManager)
    {
        _equipmentId = equipmentId;
        _orchestrator = orchestrator;
        _baseManager = baseManager;
    }

    /// <summary>
    /// Create trace data collection plan
    /// </summary>
    public async Task<TraceDataPlan> CreateTracePlanAsync(
        string planId,
        string[] dataItemIds,
        TimeSpan samplePeriod,
        int maxSamples,
        FilterCriteria? filter = null)
    {
        if (_tracePlans.ContainsKey(planId))
        {
            return _tracePlans[planId];
        }

        var plan = new TraceDataPlan(planId, dataItemIds, samplePeriod, maxSamples, filter, _equipmentId, _orchestrator);
        _tracePlans[planId] = plan;

        await plan.StartAsync();
        await plan.EnableAsync();

        return plan;
    }

    /// <summary>
    /// Start streaming session
    /// </summary>
    public async Task<StreamingSession> StartStreamingAsync(string sessionId, string[] dataItemIds, int updateRateMs = 100)
    {
        if (_streamingSessions.ContainsKey(sessionId))
        {
            return _streamingSessions[sessionId];
        }

        var session = new StreamingSession(sessionId, dataItemIds, updateRateMs, _equipmentId, _orchestrator);
        _streamingSessions[sessionId] = session;

        await session.StartAsync();

        return session;
    }

    /// <summary>
    /// Stop streaming session
    /// </summary>
    public async Task<bool> StopStreamingAsync(string sessionId)
    {
        if (_streamingSessions.TryRemove(sessionId, out var session))
        {
            await session.StopAsync();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Get trace data plan
    /// </summary>
    public TraceDataPlan? GetTracePlan(string planId)
    {
        return _tracePlans.TryGetValue(planId, out var plan) ? plan : null;
    }

    /// <summary>
    /// Get streaming session
    /// </summary>
    public StreamingSession? GetStreamingSession(string sessionId)
    {
        return _streamingSessions.TryGetValue(sessionId, out var session) ? session : null;
    }
}

/// <summary>
/// Filter criteria for data collection
/// </summary>
public class FilterCriteria
{
    public string? DataItemId { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public string? Condition { get; set; } // >, <, ==, !=, CONTAINS
    public object? CompareValue { get; set; }
}

/// <summary>
/// Trace data collection plan with buffering and filtering
/// </summary>
public class TraceDataPlan
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly string _instanceId;
    private readonly TimeSpan _samplePeriod;
    private readonly int _maxSamples;
    private readonly FilterCriteria? _filter;
    private readonly ConcurrentQueue<TraceSample> _buffer = new();

    public string PlanId { get; }
    public string[] DataItemIds { get; }
    public bool IsEnabled { get; private set; }
    public int SampleCount => _buffer.Count;

    public string MachineId => $"E164_TRACE_PLAN_{PlanId}_{_instanceId}";

    public TraceDataPlan(
        string planId,
        string[] dataItemIds,
        TimeSpan samplePeriod,
        int maxSamples,
        FilterCriteria? filter,
        string equipmentId,
        EventBusOrchestrator orchestrator)
    {
        PlanId = planId;
        DataItemIds = dataItemIds;
        _samplePeriod = samplePeriod;
        _maxSamples = maxSamples;
        _filter = filter;
        _orchestrator = orchestrator;
        _instanceId = Guid.NewGuid().ToString("N").Substring(0, 8);

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'Disabled',
            context: {
                planId: '{{planId}}',
                sampleCount: 0,
                bufferSize: {{maxSamples}}
            },
            states: {
                Disabled: {
                    entry: 'logDisabled',
                    on: {
                        ENABLE: {
                            target: 'Enabled',
                            actions: 'enableTrace'
                        }
                    }
                },
                Enabled: {
                    entry: 'logEnabled',
                    on: {
                        DISABLE: {
                            target: 'Disabled',
                            actions: 'disableTrace'
                        },
                        START_TRACE: {
                            target: 'Tracing',
                            actions: 'startTracing'
                        }
                    }
                },
                Tracing: {
                    entry: 'logTracing',
                    on: {
                        SAMPLE: {
                            target: 'Tracing',
                            actions: 'recordSample'
                        },
                        STOP_TRACE: {
                            target: 'Enabled',
                            actions: 'stopTracing'
                        },
                        BUFFER_FULL: {
                            target: 'Enabled',
                            actions: 'handleBufferFull'
                        },
                        DISABLE: {
                            target: 'Disabled',
                            actions: 'disableTrace'
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logDisabled"] = (ctx) =>
            {
                IsEnabled = false;
                Console.WriteLine($"[{MachineId}] 🔴 Trace data collection disabled");
            },

            ["enableTrace"] = (ctx) =>
            {
                IsEnabled = true;
                Console.WriteLine($"[{MachineId}] 🟢 Trace enabled - Period: {_samplePeriod.TotalMilliseconds}ms, Max: {_maxSamples}");

                ctx.RequestSend("E134_DCM_MGR", "TRACE_PLAN_ENABLED", new JObject
                {
                    ["planId"] = PlanId,
                    ["samplePeriod"] = _samplePeriod.TotalMilliseconds,
                    ["maxSamples"] = _maxSamples
                });
            },

            ["disableTrace"] = (ctx) =>
            {
                IsEnabled = false;
                _buffer.Clear();
                Console.WriteLine($"[{MachineId}] 🔴 Trace disabled, buffer cleared");
            },

            ["logEnabled"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ✅ Ready to trace: {string.Join(", ", DataItemIds)}");
            },

            ["startTracing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📊 Starting trace collection...");
            },

            ["logTracing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📈 Tracing {DataItemIds.Length} items every {_samplePeriod.TotalMilliseconds}ms");
            },

            ["recordSample"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📝 Sample recorded ({_buffer.Count}/{_maxSamples})");

                if (_buffer.Count >= _maxSamples)
                {
                    ctx.RequestSend(MachineId, "BUFFER_FULL", null);
                }
            },

            ["stopTracing"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⏹️ Trace stopped - {_buffer.Count} samples collected");

                ctx.RequestSend("E134_DCM_MGR", "TRACE_COMPLETE", new JObject
                {
                    ["planId"] = PlanId,
                    ["sampleCount"] = _buffer.Count
                });
            },

            ["handleBufferFull"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] ⚠️ Buffer full ({_maxSamples} samples) - stopping trace");

                ctx.RequestSend("E134_DCM_MGR", "TRACE_BUFFER_FULL", new JObject
                {
                    ["planId"] = PlanId,
                    ["sampleCount"] = _buffer.Count
                });
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            enableGuidIsolation: false
        );
    }

    public async Task<string> StartAsync()
    {
        return await _machine.StartAsync();
    }

    public async Task<EventResult> EnableAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "ENABLE", null);
    }

    public async Task<EventResult> DisableAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "DISABLE", null);
    }

    public async Task<EventResult> StartTraceAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START_TRACE", null);
    }

    public async Task<EventResult> StopTraceAsync()
    {
        return await _orchestrator.SendEventAsync("SYSTEM", MachineId, "STOP_TRACE", null);
    }

    public async Task AddSampleAsync(Dictionary<string, object> data)
    {
        // Apply filter if configured
        if (_filter != null && !PassesFilter(data))
        {
            return;
        }

        var sample = new TraceSample
        {
            Timestamp = DateTime.UtcNow,
            Data = data
        };

        _buffer.Enqueue(sample);

        // Remove oldest if over max
        if (_buffer.Count > _maxSamples)
        {
            _buffer.TryDequeue(out _);
        }

        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "SAMPLE", JObject.FromObject(sample));
    }

    private bool PassesFilter(Dictionary<string, object> data)
    {
        if (_filter == null) return true;

        if (_filter.DataItemId != null && data.TryGetValue(_filter.DataItemId, out var value))
        {
            if (value is double dValue)
            {
                if (_filter.MinValue.HasValue && dValue < _filter.MinValue.Value) return false;
                if (_filter.MaxValue.HasValue && dValue > _filter.MaxValue.Value) return false;
            }
        }

        return true;
    }

    public TraceSample[] GetSamples()
    {
        return _buffer.ToArray();
    }
}

/// <summary>
/// Streaming data session for real-time monitoring
/// </summary>
public class StreamingSession
{
    private readonly IPureStateMachine _machine;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly int _updateRateMs;
    private CancellationTokenSource? _streamingCts;

    public string SessionId { get; }
    public string[] DataItemIds { get; }
    public bool IsStreaming { get; private set; }
    public int UpdateCount { get; private set; }

    public string MachineId => $"E164_STREAMING_{SessionId}";

    public StreamingSession(string sessionId, string[] dataItemIds, int updateRateMs, string equipmentId, EventBusOrchestrator orchestrator)
    {
        SessionId = sessionId;
        DataItemIds = dataItemIds;
        _updateRateMs = updateRateMs;
        _orchestrator = orchestrator;

        var definition = $$"""
        {
            id: '{{MachineId}}',
            initial: 'Idle',
            states: {
                Idle: {
                    entry: 'logIdle',
                    on: {
                        START_STREAMING: {
                            target: 'Streaming',
                            actions: 'startStreaming'
                        }
                    }
                },
                Streaming: {
                    entry: 'logStreaming',
                    on: {
                        UPDATE: {
                            target: 'Streaming',
                            actions: 'publishUpdate'
                        },
                        STOP_STREAMING: {
                            target: 'Idle',
                            actions: 'stopStreaming'
                        }
                    }
                }
            }
        }
        """;

        var actions = new Dictionary<string, Action<OrchestratedContext>>
        {
            ["logIdle"] = (ctx) =>
            {
                IsStreaming = false;
                Console.WriteLine($"[{MachineId}] 💤 Streaming session idle");
            },

            ["startStreaming"] = (ctx) =>
            {
                IsStreaming = true;
                Console.WriteLine($"[{MachineId}] 📡 Streaming started - Rate: {_updateRateMs}ms");

                ctx.RequestSend("E134_DCM_MGR", "STREAMING_STARTED", new JObject
                {
                    ["sessionId"] = SessionId,
                    ["dataItemIds"] = new JArray(DataItemIds),
                    ["updateRate"] = _updateRateMs
                });
            },

            ["logStreaming"] = (ctx) =>
            {
                Console.WriteLine($"[{MachineId}] 📡 Streaming {DataItemIds.Length} items at {_updateRateMs}ms intervals");
            },

            ["publishUpdate"] = (ctx) =>
            {
                UpdateCount++;
                Console.WriteLine($"[{MachineId}] 📤 Update #{UpdateCount} published");
            },

            ["stopStreaming"] = (ctx) =>
            {
                IsStreaming = false;
                Console.WriteLine($"[{MachineId}] ⏹️ Streaming stopped - {UpdateCount} updates sent");

                ctx.RequestSend("E134_DCM_MGR", "STREAMING_STOPPED", new JObject
                {
                    ["sessionId"] = SessionId,
                    ["updateCount"] = UpdateCount
                });
            }
        };

        _machine = ExtendedPureStateMachineFactory.CreateFromScriptWithGuardsAndServices(
            id: MachineId,
            json: definition,
            orchestrator: orchestrator,
            orchestratedActions: actions,
            enableGuidIsolation: false
        );
    }

    public async Task<string> StartAsync()
    {
        var state = await _machine.StartAsync();
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "START_STREAMING", null);

        // Start streaming loop
        _streamingCts = new CancellationTokenSource();
        _ = StreamingLoopAsync(_streamingCts.Token);

        return state;
    }

    public async Task StopAsync()
    {
        _streamingCts?.Cancel();
        await _orchestrator.SendEventAsync("SYSTEM", MachineId, "STOP_STREAMING", null);
    }

    private async Task StreamingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsStreaming)
        {
            try
            {
                await _orchestrator.SendEventAsync("SYSTEM", MachineId, "UPDATE", new JObject
                {
                    ["timestamp"] = DateTime.UtcNow,
                    ["updateCount"] = UpdateCount
                });

                await Task.Delay(_updateRateMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>
/// Trace data sample
/// </summary>
public class TraceSample
{
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}
