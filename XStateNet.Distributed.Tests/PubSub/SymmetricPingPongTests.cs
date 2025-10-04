using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.Orchestration;

namespace XStateNet.Distributed.Tests.PubSub
{
    /// <summary>
    /// Tests demonstrating symmetric distributed state machines communicating via orchestrator
    /// Both machines can initiate and respond to messages
    /// </summary>
    [Collection("Sequential")]
    public class SymmetricPingPongTests : XStateNet.Tests.OrchestratorTestBase
    {
        private readonly ITestOutputHelper _output;

        public SymmetricPingPongTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task SymmetricMachines_BidirectionalCommunication()
        {
            // Arrange
            var machine1PingCount = 0;
            var machine1PongCount = 0;
            var machine2PingCount = 0;
            var machine2PongCount = 0;
            var maxExchanges = 3;
            var testStartTime = DateTime.UtcNow;

            // Store machine IDs for cross-machine communication
            string? machine1Id = null;
            string? machine2Id = null;

            // Create symmetric machine JSON - both can ping and pong
            var createSymmetricMachine = (string id) => $$"""
            {
                id: '{{id}}',
                initial: 'idle',
                states: {
                    idle: {
                        on: {
                            START: { target: 'active', actions: 'onStart' },
                            PING: { target: 'active', actions: 'onPing' },
                            PONG: { target: 'active', actions: 'onPong' }
                        }
                    },
                    active: {
                        on: {
                            PING: { actions: 'onPing' },
                            PONG: { actions: 'onPong' },
                            COMPLETE: 'done'
                        }
                    },
                    done: {
                        type: 'final'
                    }
                }
            }
            """;

            // Machine1 actions
            var machine1Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["onStart"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    machine1PingCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Sending PING #{machine1PingCount}");
                    ctx.RequestSend(machine2Id!, "PING", machine1PingCount);
                },
                ["onPing"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    machine1PongCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Received PING, sending PONG #{machine1PongCount}");
                    ctx.RequestSend(machine2Id!, "PONG", machine1PongCount);

                    if (machine1PongCount >= maxExchanges)
                    {
                        ctx.RequestSelfSend("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Complete");
                    }
                },
                ["onPong"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Received PONG");

                    if (machine1PingCount < maxExchanges)
                    {
                        machine1PingCount++;
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Sending PING #{machine1PingCount}");
                        ctx.RequestSend(machine2Id!, "PING", machine1PingCount);
                    }
                    else
                    {
                        ctx.RequestSelfSend("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine1] Complete");
                    }
                }
            };

            // Machine2 actions
            var machine2Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["onStart"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    machine2PingCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Sending PING #{machine2PingCount}");
                    ctx.RequestSend(machine1Id!, "PING", machine2PingCount);
                },
                ["onPing"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    machine2PongCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Received PING, sending PONG #{machine2PongCount}");
                    ctx.RequestSend(machine1Id!, "PONG", machine2PongCount);

                    if (machine2PongCount >= maxExchanges)
                    {
                        ctx.RequestSelfSend("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Complete");
                    }
                },
                ["onPong"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Received PONG");

                    if (machine2PingCount < maxExchanges)
                    {
                        machine2PingCount++;
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Sending PING #{machine2PingCount}");
                        ctx.RequestSend(machine1Id!, "PING", machine2PingCount);
                    }
                    else
                    {
                        ctx.RequestSelfSend("COMPLETE");
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Machine2] Complete");
                    }
                }
            };

            // Create machines using orchestrated pattern
            var machine1 = CreateMachine("machine1", createSymmetricMachine("machine1"), machine1Actions);
            var machine2 = CreateMachine("machine2", createSymmetricMachine("machine2"), machine2Actions);

            // Capture actual machine IDs after creation (includes GUID suffix)
            machine1Id = machine1.Id;
            machine2Id = machine2.Id;
            _output.WriteLine($"Machine1 actual ID: {machine1Id}");
            _output.WriteLine($"Machine2 actual ID: {machine2Id}");

            // Act
            await machine1.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Machine1 started");

            await machine2.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Machine2 started");

            // Only machine1 initiates for this test
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Machine1 initiating communication");
            await _orchestrator.SendEventAsync("test", machine1Id, "START");

            // Wait for both machines to complete
            await WaitForStateAsync(machine1, "#machine1.done", timeoutMs: 5000);
            await WaitForStateAsync(machine2, "#machine2.done", timeoutMs: 5000);

            // Assert
            Assert.Equal(maxExchanges, machine1PingCount);
            Assert.Equal(maxExchanges, machine2PongCount);
            Assert.Contains("done", machine1.CurrentState);
            Assert.Contains("done", machine2.CurrentState);

            var totalElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
            _output.WriteLine($"\n[{totalElapsed:F1}ms] === Test Complete ===");
            _output.WriteLine($"[{totalElapsed:F1}ms] Machine1 sent {machine1PingCount} pings, received {machine1PongCount} pongs");
            _output.WriteLine($"[{totalElapsed:F1}ms] Machine2 sent {machine2PongCount} pongs, received {machine2PingCount} pings");
        }

        [Fact]
        public async Task SymmetricMachines_SimultaneousInitiation()
        {
            // Arrange
            var machine1MessageCount = 0;
            var machine2MessageCount = 0;
            var testStartTime = DateTime.UtcNow;

            // Store machine IDs for cross-machine communication
            string? machine1Id = null;
            string? machine2Id = null;

            // Create symmetric machines with IDs
            var createScript = (string id) => $$"""
            {
                id: '{{id}}',
                initial: 'ready',
                states: {
                    ready: {
                        on: {
                            START: { target: 'sending', actions: 'onStart' },
                            MESSAGE: { target: 'receiving', actions: 'onMessage' },
                            DONE: 'complete'
                        }
                    },
                    sending: {
                        on: {
                            SENT: 'ready',
                            MESSAGE: { target: 'receiving', actions: 'onMessage' },
                            DONE: 'complete'
                        }
                    },
                    receiving: {
                        on: {
                            REPLY: { target: 'ready', actions: 'onReply' },
                            DONE: 'complete'
                        }
                    },
                    complete: {
                        type: 'final'
                    }
                }
            }
            """;

            // Machine1 actions
            var machine1Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["onStart"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Sending MESSAGE");
                    ctx.RequestSend(machine2Id!, "MESSAGE", new { from = "sym1" });
                    ctx.RequestSelfSend("SENT");
                },
                ["onMessage"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    machine1MessageCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Received MESSAGE (count: {machine1MessageCount})");

                    if (machine1MessageCount < 3)
                    {
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Sending REPLY");
                        ctx.RequestSend(machine2Id!, "REPLY", new { from = "sym1" });
                        ctx.RequestSelfSend("REPLY");
                    }
                    else
                    {
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Complete");
                        ctx.RequestSelfSend("DONE");
                    }
                },
                ["onReply"] = ctx =>
                {
                    // Don't send more messages if we've already reached the limit
                    if (machine1MessageCount >= 3) return;

                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym1] Received REPLY, sending MESSAGE");
                    ctx.RequestSend(machine2Id!, "MESSAGE", new { from = "sym1" });
                }
            };

            // Machine2 actions
            var machine2Actions = new Dictionary<string, Action<OrchestratedContext>>
            {
                ["onStart"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Sending MESSAGE");
                    ctx.RequestSend(machine1Id!, "MESSAGE", new { from = "sym2" });
                    ctx.RequestSelfSend("SENT");
                },
                ["onMessage"] = ctx =>
                {
                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    machine2MessageCount++;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Received MESSAGE (count: {machine2MessageCount})");

                    if (machine2MessageCount < 3)
                    {
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Sending REPLY");
                        ctx.RequestSend(machine1Id!, "REPLY", new { from = "sym2" });
                        ctx.RequestSelfSend("REPLY");
                    }
                    else
                    {
                        _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Complete");
                        ctx.RequestSelfSend("DONE");
                    }
                },
                ["onReply"] = ctx =>
                {
                    // Don't send more messages if we've already reached the limit
                    if (machine2MessageCount >= 3) return;

                    var elapsedMs = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
                    _output.WriteLine($"[{elapsedMs:F1}ms] [Sym2] Received REPLY, sending MESSAGE");
                    ctx.RequestSend(machine1Id!, "MESSAGE", new { from = "sym2" });
                }
            };

            // Create machines using orchestrated pattern
            var machine1 = CreateMachine("sym1", createScript("sym1"), machine1Actions);
            var machine2 = CreateMachine("sym2", createScript("sym2"), machine2Actions);

            // Capture actual machine IDs after creation (includes GUID suffix)
            machine1Id = machine1.Id;
            machine2Id = machine2.Id;
            _output.WriteLine($"Sym1 actual ID: {machine1Id}");
            _output.WriteLine($"Sym2 actual ID: {machine2Id}");

            // Act
            await machine1.StartAsync();
            await machine2.StartAsync();
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Both machines started");

            // Both machines initiate simultaneously
            _output.WriteLine($"[{(DateTime.UtcNow - testStartTime).TotalMilliseconds:F1}ms] Both machines initiating simultaneously");
            await Task.WhenAll(
                _orchestrator.SendEventAsync(machine1Id, machine1Id, "START"),
                _orchestrator.SendEventAsync(machine2Id, machine2Id, "START")
            );

            // Wait for both to complete
            await WaitForStateAsync(machine1, "#sym1.complete", timeoutMs: 5000);
            await WaitForStateAsync(machine2, "#sym2.complete", timeoutMs: 5000);

            // Assert
            Assert.True(machine1MessageCount >= 3, $"Machine1 should have received at least 3 messages, got {machine1MessageCount}");
            Assert.True(machine2MessageCount >= 3, $"Machine2 should have received at least 3 messages, got {machine2MessageCount}");
            Assert.Contains("complete", machine1.CurrentState);
            Assert.Contains("complete", machine2.CurrentState);

            var totalElapsed = (DateTime.UtcNow - testStartTime).TotalMilliseconds;
            _output.WriteLine($"\n[{totalElapsed:F1}ms] === Test Complete ===");
            _output.WriteLine($"[{totalElapsed:F1}ms] Sym1 received: {machine1MessageCount} messages");
            _output.WriteLine($"[{totalElapsed:F1}ms] Sym2 received: {machine2MessageCount} messages");
        }
    }
}
