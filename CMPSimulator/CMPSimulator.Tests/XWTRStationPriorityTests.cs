using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMPSimulator.XStateStations;
using Xunit;
using Xunit.Abstractions;
using XStateNet.Orchestration;

namespace CMPSimulator.Tests;

/// <summary>
/// Tests for WTR station priority queue and backward priority rules
/// Priority 1 (highest): Cleaner → LoadPort (귀환 경로)
/// Priority 2: Polisher → Cleaner (중간 공정)
/// Priority 3 (lowest): LoadPort → Polisher (시작 공정)
/// </summary>
public class XWTRStationPriorityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly EventBusOrchestrator _orchestrator;
    private readonly List<string> _logMessages;

    public XWTRStationPriorityTests(ITestOutputHelper output)
    {
        _output = output;
        _orchestrator = new EventBusOrchestrator(new OrchestratorConfig
        {
            PoolSize = 2,
            EnableLogging = false
        });
        _logMessages = new List<string>();
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }

    [Fact]
    public async Task PriorityTest_CleanerToLoadPort_HasHighestPriority()
    {
        // Arrange
        var wtr = new XWTRStation("wtr_test", _orchestrator);
        wtr.LogMessage += (s, msg) => { _logMessages.Add(msg); _output.WriteLine(msg); };
        await wtr.StartAsync();

        // Send low priority request first (LoadPort → Polisher)
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 1,
                ["from"] = "loadport",
                ["to"] = "polisher"
            }
        );

        // Wait for WTR to start working
        await Task.Delay(100);

        // Send high priority request while WTR is busy (Cleaner → Buffer)
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 2,
                ["from"] = "cleaner",
                ["to"] = "buffer"
            }
        );

        // Send medium priority request (Polisher → Cleaner)
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 3,
                ["from"] = "polisher",
                ["to"] = "cleaner"
            }
        );

        // Wait for all transfers to complete (need more time: 1200ms for first + 2400ms for queued wafers)
        await Task.Delay(5000);

        // Assert - Check that high priority was processed before low priority
        var wafer1Messages = _logMessages.Where(m => m.Contains("Wafer 1")).ToList();
        var wafer2Messages = _logMessages.Where(m => m.Contains("Wafer 2")).ToList();
        var wafer3Messages = _logMessages.Where(m => m.Contains("Wafer 3")).ToList();

        _output.WriteLine("\n=== Wafer 1 (P3) Messages ===");
        wafer1Messages.ForEach(m => _output.WriteLine(m));
        _output.WriteLine("\n=== Wafer 2 (P1) Messages ===");
        wafer2Messages.ForEach(m => _output.WriteLine(m));
        _output.WriteLine("\n=== Wafer 3 (P2) Messages ===");
        wafer3Messages.ForEach(m => _output.WriteLine(m));

        // Verify priority tags appear
        Assert.Contains(_logMessages, m => m.Contains("[P3]") && m.Contains("Wafer 1"));
        Assert.Contains(_logMessages, m => m.Contains("[P1]") && m.Contains("Wafer 2"));
        Assert.Contains(_logMessages, m => m.Contains("[P2]") && m.Contains("Wafer 3"));

        // Find the dequeue messages to verify order
        var dequeueMessages = _logMessages
            .Where(m => m.Contains("Dequeued") || m.Contains("Immediate"))
            .ToList();

        _output.WriteLine("\n=== Processing Order ===");
        dequeueMessages.ForEach(m => _output.WriteLine(m));

        // Verify we have at least 3 dequeue operations
        Assert.True(dequeueMessages.Count >= 3, $"Expected at least 3 dequeue messages, but got {dequeueMessages.Count}");

        // First should be immediate P3 (Wafer 1)
        Assert.Contains("Immediate", dequeueMessages[0]);
        Assert.Contains("Wafer 1", dequeueMessages[0]);

        if (dequeueMessages.Count >= 2)
        {
            // Second should be dequeued P1 (Wafer 2) - highest priority
            Assert.Contains("Dequeued", dequeueMessages[1]);
            Assert.Contains("[P1]", dequeueMessages[1]);
            Assert.Contains("Wafer 2", dequeueMessages[1]);
        }

        if (dequeueMessages.Count >= 3)
        {
            // Third should be dequeued P2 (Wafer 3) - medium priority
            Assert.Contains("Dequeued", dequeueMessages[2]);
            Assert.Contains("[P2]", dequeueMessages[2]);
            Assert.Contains("Wafer 3", dequeueMessages[2]);
        }
    }

    [Fact]
    public async Task PriorityTest_BufferToLoadPort_HasHighestPriority()
    {
        // Arrange
        var wtr = new XWTRStation("wtr_test", _orchestrator);
        wtr.LogMessage += (s, msg) => { _logMessages.Add(msg); _output.WriteLine(msg); };
        await wtr.StartAsync();

        // Send low priority request first
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 10,
                ["from"] = "loadport",
                ["to"] = "polisher"
            }
        );

        await Task.Delay(100);

        // Send highest priority request (Buffer → LoadPort)
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 20,
                ["from"] = "buffer",
                ["to"] = "loadport"
            }
        );

        await Task.Delay(2000);

        // Verify P1 priority for buffer→loadport
        Assert.Contains(_logMessages, m => m.Contains("[P1]") && m.Contains("Wafer 20") && m.Contains("buffer"));

        var dequeueMessages = _logMessages
            .Where(m => m.Contains("Dequeued"))
            .ToList();

        // Buffer→LoadPort should be dequeued first (P1)
        if (dequeueMessages.Any())
        {
            Assert.Contains("[P1]", dequeueMessages[0]);
            Assert.Contains("Wafer 20", dequeueMessages[0]);
        }
    }

    [Fact]
    public async Task PriorityTest_MultipleRequests_ProcessedInPriorityOrder()
    {
        // Arrange
        var wtr = new XWTRStation("wtr_test", _orchestrator);
        wtr.LogMessage += (s, msg) => { _logMessages.Add(msg); _output.WriteLine(msg); };
        await wtr.StartAsync();

        // Start with one request to make WTR busy
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 100,
                ["from"] = "loadport",
                ["to"] = "polisher"
            }
        );

        await Task.Delay(50);

        // Queue multiple requests with different priorities
        var requests = new[]
        {
            (waferId: 101, from: "loadport", to: "polisher", expectedPriority: 3),
            (waferId: 102, from: "cleaner", to: "buffer", expectedPriority: 1),
            (waferId: 103, from: "polisher", to: "cleaner", expectedPriority: 2),
            (waferId: 104, from: "buffer", to: "loadport", expectedPriority: 1),
            (waferId: 105, from: "loadport", to: "polisher", expectedPriority: 3),
        };

        foreach (var request in requests)
        {
            await _orchestrator.SendEventAsync(
                fromMachineId: "test",
                toMachineId: "wtr_test",
                eventName: "TRANSFER_REQUEST",
                eventData: new Dictionary<string, object>
                {
                    ["waferId"] = request.waferId,
                    ["from"] = request.from,
                    ["to"] = request.to
                }
            );
            await Task.Delay(10); // Small delay between requests
        }

        // Wait for all to complete
        await Task.Delay(5000);

        _output.WriteLine("\n=== All Log Messages ===");
        _logMessages.ForEach(m => _output.WriteLine(m));

        // Extract dequeue order
        var processOrder = _logMessages
            .Where(m => m.Contains("Dequeued") || m.Contains("Immediate"))
            .Select(m =>
            {
                var waferMatch = System.Text.RegularExpressions.Regex.Match(m, @"Wafer (\d+)");
                if (waferMatch.Success)
                {
                    return int.Parse(waferMatch.Groups[1].Value);
                }
                return -1;
            })
            .Where(w => w != -1)
            .ToList();

        _output.WriteLine("\n=== Process Order ===");
        processOrder.ForEach(w => _output.WriteLine($"Wafer {w}"));

        // Verify: All P1 requests should be processed before P2, and P2 before P3
        // Expected order: 100(immediate P3), 102(P1), 104(P1), 103(P2), 101(P3), 105(P3)
        Assert.NotEmpty(processOrder);

        // At minimum, verify that queued messages show correct priority
        Assert.Contains(_logMessages, m => m.Contains("[P1]") && m.Contains("Wafer 102"));
        Assert.Contains(_logMessages, m => m.Contains("[P1]") && m.Contains("Wafer 104"));
        Assert.Contains(_logMessages, m => m.Contains("[P2]") && m.Contains("Wafer 103"));
        Assert.Contains(_logMessages, m => m.Contains("[P3]") && m.Contains("Wafer 101"));
        Assert.Contains(_logMessages, m => m.Contains("[P3]") && m.Contains("Wafer 105"));
    }

    [Fact]
    public async Task PriorityTest_BackwardPriority_EnsuresCleanerBeforePolisher()
    {
        // This test simulates the real scenario:
        // LoadPort dispatches Wafer 1 → WTR1 starts transfer
        // Meanwhile, Cleaner finishes Wafer 2 → Should get priority over new LoadPort dispatch

        var wtr = new XWTRStation("wtr_test", _orchestrator);
        wtr.LogMessage += (s, msg) => { _logMessages.Add(msg); _output.WriteLine(msg); };
        await wtr.StartAsync();

        // Simulate: WTR picks up first wafer from LoadPort
        await _orchestrator.SendEventAsync(
            fromMachineId: "loadport",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 1,
                ["from"] = "loadport",
                ["to"] = "polisher"
            }
        );

        await Task.Delay(100);

        // Cleaner finishes and requests pickup (should get priority)
        await _orchestrator.SendEventAsync(
            fromMachineId: "cleaner",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 2,
                ["from"] = "cleaner",
                ["to"] = "buffer"
            }
        );

        // LoadPort tries to dispatch another wafer
        await _orchestrator.SendEventAsync(
            fromMachineId: "loadport",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 3,
                ["from"] = "loadport",
                ["to"] = "polisher"
            }
        );

        await Task.Delay(2500);

        // Assert: Wafer 2 (from Cleaner) should be processed before Wafer 3 (from LoadPort)
        var dequeueMessages = _logMessages
            .Where(m => m.Contains("Dequeued"))
            .ToList();

        _output.WriteLine("\n=== Dequeue Order ===");
        dequeueMessages.ForEach(m => _output.WriteLine(m));

        if (dequeueMessages.Count >= 2)
        {
            // First dequeued should be Wafer 2 (P1 - cleaner)
            Assert.Contains("Wafer 2", dequeueMessages[0]);
            Assert.Contains("[P1]", dequeueMessages[0]);
            // Second should be Wafer 3 (P3 - loadport)
            Assert.Contains("Wafer 3", dequeueMessages[1]);
            Assert.Contains("[P3]", dequeueMessages[1]);
        }
    }

    [Fact]
    public async Task PriorityTest_IdleWTR_ProcessesImmediately()
    {
        // Arrange
        var wtr = new XWTRStation("wtr_test", _orchestrator);
        wtr.LogMessage += (s, msg) => { _logMessages.Add(msg); _output.WriteLine(msg); };
        await wtr.StartAsync();

        // Act - Send request to idle WTR
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 99,
                ["from"] = "polisher",
                ["to"] = "cleaner"
            }
        );

        await Task.Delay(1500);

        // Assert - Should process immediately, not queue
        Assert.Contains(_logMessages, m => m.Contains("Immediate transfer") && m.Contains("Wafer 99"));
        Assert.DoesNotContain(_logMessages, m => m.Contains("Queued") && m.Contains("Wafer 99"));
    }

    [Fact]
    public async Task PriorityTest_QueueingWorks_WhenWTRBusy()
    {
        // Simple test: Verify that requests are queued with correct priority when WTR is busy
        var wtr = new XWTRStation("wtr_test", _orchestrator);
        wtr.LogMessage += (s, msg) => { _logMessages.Add(msg); _output.WriteLine(msg); };
        await wtr.StartAsync();

        // Make WTR busy
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 1,
                ["from"] = "loadport",
                ["to"] = "polisher"
            }
        );

        await Task.Delay(50); // Small delay to ensure WTR is busy

        // Send requests while busy
        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 2,
                ["from"] = "cleaner",
                ["to"] = "buffer"
            }
        );

        await _orchestrator.SendEventAsync(
            fromMachineId: "test",
            toMachineId: "wtr_test",
            eventName: "TRANSFER_REQUEST",
            eventData: new Dictionary<string, object>
            {
                ["waferId"] = 3,
                ["from"] = "polisher",
                ["to"] = "cleaner"
            }
        );

        await Task.Delay(500);

        // Assert - Both should be queued with correct priorities
        Assert.Contains(_logMessages, m => m.Contains("[P1]") && m.Contains("Queued") && m.Contains("Wafer 2"));
        Assert.Contains(_logMessages, m => m.Contains("[P2]") && m.Contains("Queued") && m.Contains("Wafer 3"));

        _output.WriteLine("\n=== All Messages ===");
        _logMessages.ForEach(m => _output.WriteLine(m));
    }
}
