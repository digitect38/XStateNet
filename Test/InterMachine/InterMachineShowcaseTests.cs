using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using XStateNet;
using XStateNet.InterMachine;

namespace Test.InterMachine
{
    /// <summary>
    /// Showcase tests demonstrating XStateNet-IM capabilities
    /// </summary>
    [Collection("Sequential")]
    public class InterMachineShowcaseTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly InterMachineSession _session;

        public InterMachineShowcaseTests(ITestOutputHelper output)
        {
            _output = output;
            _session = new InterMachineSession();
        }

        [Fact]
        public async Task Showcase_SymmetricPingPongWithoutMediator()
        {
            _output.WriteLine("=== XStateNet-IM: Symmetric Ping-Pong Without Mediator ===\n");

            // Create two symmetric state machines - simplified for direct exchange
            var machine1Json = @"{
                ""id"": ""pingPongMachine1"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {
                        ""on"": {
                            ""PING"": ""ready"",
                            ""PONG"": ""ready"",
                            ""DONE"": ""complete""
                        }
                    },
                    ""complete"": {
                        ""type"": ""final""
                    }
                }
            }";

            var exchangeCount = 0;
            var maxExchanges = 10;
            var completed = new TaskCompletionSource<bool>();
            var stopwatch = Stopwatch.StartNew();

            // Create Machine 1
            var machine1 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine1, machine1Json);

            // Create Machine 2 (symmetric)
            var machine2 = new StateMachine();
            StateMachineFactory.CreateFromScript(machine2, machine1Json.Replace("pingPongMachine1", "pingPongMachine2"));

            // Add to session and connect directly (no mediator!)
            var cm1 = _session.AddMachine(machine1, "machine1");
            var cm2 = _session.AddMachine(machine2, "machine2");
            _session.Connect("machine1", "machine2");

            // Set up message handlers for symmetric ping-pong
            cm1.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PONG")
                {
                    await Task.Delay(1); // Reduce load
                    var count = Interlocked.Increment(ref exchangeCount);
                    _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] Machine1: Received PONG (exchange #{count})");
                    await machine1.SendAsync("PONG");

                    if (count < maxExchanges)
                    {
                        // Continue exchange - send PING back
                        _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] Machine1: Sending PING #{count + 1}");
                        await cm1.SendToAsync("machine2", "PING", count + 1);
                    }
                    else
                    {
                        // Complete
                        await machine1.SendAsync("DONE");
                        _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] Machine1: Complete!");
                        completed.TrySetResult(true);
                    }
                }
            });

            cm2.OnMessage(async (msg) =>
            {
                if (msg.EventName == "PING")
                {
                    await Task.Delay(1); // Reduce load
                    _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] Machine2: Received PING, sending PONG");
                    await machine2.SendAsync("PING");

                    // Always respond with PONG
                    await cm2.SendToAsync("machine1", "PONG", msg.Data);

                    // Check if this was the last exchange
                    var pingCount = msg.Data as int? ?? 0;
                    _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] Machine2: PING count = {pingCount}, maxExchanges = {maxExchanges}");
                    if (pingCount >= maxExchanges)
                    {
                        await machine2.SendAsync("DONE");
                        _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] Machine2: Complete!");
                    }
                }
            });

            await machine1.StartAsync();
            await machine2.StartAsync();
            _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] Both machines started and connected directly\n");

            // Start the symmetric exchange - Machine1 sends first PING
            _output.WriteLine($"[{stopwatch.ElapsedMilliseconds,4}ms] === Starting Symmetric Exchange ===");
            await cm1.SendToAsync("machine2", "PING", 0);

            // Wait for completion
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopwatch.Stop();

            // Summary
            _output.WriteLine($"\n[{stopwatch.ElapsedMilliseconds,4}ms] === Summary ===");
            _output.WriteLine($"??Completed {exchangeCount} symmetric exchanges");
            _output.WriteLine($"??Total time: {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"??Throughput: {exchangeCount * 1000.0 / stopwatch.ElapsedMilliseconds:F0} exchanges/second");
            _output.WriteLine($"??No mediator or event bus required!");
            _output.WriteLine($"??Both machines can initiate and respond symmetrically");

            Assert.Equal(maxExchanges, exchangeCount);
            Assert.Equal("#pingPongMachine1.complete", machine1.GetActiveStateNames());
            Assert.Equal("#pingPongMachine2.complete", machine2.GetActiveStateNames());
        }

        [Fact]
        public async Task Showcase_DistributedMeshCommunication()
        {
            _output.WriteLine("=== XStateNet-IM: Distributed Mesh Communication ===\n");

            var nodeCount = 5;
            var nodes = new StateMachine[nodeCount];
            var connectedNodes = new ConnectedMachine[nodeCount];
            var messageStats = new Dictionary<string, int>();
            var allComplete = new TaskCompletionSource<bool>();
            var completedNodes = 0;

            // Create node state machine JSON
            var createNodeJson = (string id) => $@"{{
                ""id"": ""{id}"",
                ""initial"": ""online"",
                ""states"": {{
                    ""online"": {{
                        ""on"": {{
                            ""HEARTBEAT"": {{
                                ""actions"": [""processHeartbeat""]
                            }},
                            ""DATA"": {{
                                ""actions"": [""processData""]
                            }},
                            ""SHUTDOWN"": ""offline""
                        }}
                    }},
                    ""offline"": {{
                        ""type"": ""final""
                    }}
                }}
            }}";

            _output.WriteLine("Creating distributed mesh network...");

            // Create nodes
            for (int i = 0; i < nodeCount; i++)
            {
                var nodeId = $"node{i}";
                nodes[i] = new StateMachine();
                messageStats[nodeId] = 0;

                var index = i;
                var actions = new ActionMap
                {
                    ["processHeartbeat"] = new List<NamedAction>
                    {
                        new NamedAction("processHeartbeat", async (sm) =>
                        {
                            messageStats[$"node{index}"]++;
                            _output.WriteLine($"Node{index}: Heartbeat received (total: {messageStats[$"node{index}"]})");
                            await Task.CompletedTask;
                        })
                    },
                    ["processData"] = new List<NamedAction>
                    {
                        new NamedAction("processData", async (sm) =>
                        {
                            messageStats[$"node{index}"]++;
                            _output.WriteLine($"Node{index}: Data received (total: {messageStats[$"node{index}"]})");

                            if (messageStats[$"node{index}"] >= (nodeCount - 1) * 2)
                            {
                                await sm.SendAsync("SHUTDOWN");
                                _output.WriteLine($"Node{index}: Going offline");

                                if (Interlocked.Increment(ref completedNodes) == nodeCount)
                                {
                                    allComplete.TrySetResult(true);
                                }
                            }
                            await Task.CompletedTask;
                        })
                    }
                };

                StateMachineFactory.CreateFromScript(nodes[i], createNodeJson(nodeId), false, false, actions);
                connectedNodes[i] = _session.AddMachine(nodes[i], nodeId);
            }

            // Create full mesh topology
            _output.WriteLine("\nEstablishing mesh connections:");
            for (int i = 0; i < nodeCount; i++)
            {
                for (int j = i + 1; j < nodeCount; j++)
                {
                    _session.Connect($"node{i}", $"node{j}");
                    _output.WriteLine($"  Connected: node{i} <--> node{j}");
                }
            }

            // Start all nodes
            _output.WriteLine("\nStarting all nodes...");
            foreach (var node in nodes)
            {
                await node.StartAsync();
            }

            // Phase 1: Heartbeat broadcast
            _output.WriteLine("\n=== Phase 1: Heartbeat Broadcast ===");
            for (int i = 0; i < nodeCount; i++)
            {
                for (int j = 0; j < nodeCount; j++)
                {
                    if (i != j)
                    {
                        await connectedNodes[i].SendToAsync($"node{j}", "HEARTBEAT", new { from = $"node{i}" });
                    }
                }
            }

            await Task.Delay(100);

            // Phase 2: Data exchange
            _output.WriteLine("\n=== Phase 2: Data Exchange ===");
            for (int i = 0; i < nodeCount; i++)
            {
                for (int j = 0; j < nodeCount; j++)
                {
                    if (i != j)
                    {
                        await connectedNodes[i].SendToAsync($"node{j}", "DATA", new { from = $"node{i}", payload = $"data_{i}_{j}" });
                    }
                }
            }

            // Wait for all nodes to complete
            await allComplete.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Verify results
            _output.WriteLine("\n=== Results ===");
            for (int i = 0; i < nodeCount; i++)
            {
                Assert.Equal("#node" + i + ".offline", nodes[i].GetActiveStateNames());
                _output.WriteLine($"??Node{i}: Processed {messageStats[$"node{i}"]} messages");
            }

            var totalMessages = messageStats.Values.Sum();
            _output.WriteLine($"\n??Total messages exchanged: {totalMessages}");
            _output.WriteLine($"??All {nodeCount} nodes successfully communicated in mesh topology");
            _output.WriteLine($"??No central coordinator or event bus required!");
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}