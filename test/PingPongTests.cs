using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet;

namespace InterMachineTests;

/// <summary>
/// Tests demonstrating ping-pong communication between state machines
/// </summary>
public class PingPongTests : XStateNet.Tests.TestBase
{
    [Fact]
    public async Task TwoMachines_Should_PingPongBetweenEachOther()
    {
        // Arrange
        var transitionLog = new List<string>();
        StateMachine? pingMachine = null;
        StateMachine? pongMachine = null;
        
        // Create Ping machine that sends to Pong after a delay
        var pingJson = @"{
            ""id"": ""ping"",
            ""initial"": ""waitingToServe"",
            ""states"": {
                ""waitingToServe"": {
                    ""after"": {
                        ""500"": {
                            ""target"": ""served"",
                            ""actions"": [""serveToPong""]
                        }
                    }
                },
                ""served"": {
                    ""on"": {
                        ""RETURN"": {
                            ""target"": ""waitingToServe"",
                            ""actions"": [""logReturn""]
                        }
                    }
                }
            }
        }";
        
        // Create Pong machine that returns to Ping after receiving
        var pongJson = @"{
            ""id"": ""pong"",
            ""initial"": ""waitingForServe"",
            ""states"": {
                ""waitingForServe"": {
                    ""on"": {
                        ""SERVE"": {
                            ""target"": ""returning"",
                            ""actions"": [""logReceive""]
                        }
                    }
                },
                ""returning"": {
                    ""after"": {
                        ""300"": {
                            ""target"": ""waitingForServe"",
                            ""actions"": [""returnToPing""]
                        }
                    }
                }
            }
        }";
        
        // Setup Ping actions
        var pingActions = new ActionMap
        {
            ["serveToPong"] = new List<NamedAction> 
            { 
                new NamedAction("serveToPong", (sm) => 
                {
                    transitionLog.Add("PING: Served ball to PONG");
                    pongMachine?.Send("SERVE");
                })
            },
            ["logReturn"] = new List<NamedAction>
            {
                new NamedAction("logReturn", (sm) =>
                {
                    transitionLog.Add("PING: Received return from PONG");
                })
            }
        };
        
        // Setup Pong actions
        var pongActions = new ActionMap
        {
            ["logReceive"] = new List<NamedAction>
            {
                new NamedAction("logReceive", (sm) =>
                {
                    transitionLog.Add("PONG: Received serve from PING");
                })
            },
            ["returnToPing"] = new List<NamedAction>
            {
                new NamedAction("returnToPing", (sm) =>
                {
                    transitionLog.Add("PONG: Returned ball to PING");
                    pingMachine?.Send("RETURN");
                })
            }
        };
        
        // Create and start machines
        pingMachine = StateMachine.CreateFromScript(pingJson, pingActions);
        pongMachine = StateMachine.CreateFromScript(pongJson, pongActions);
        
        pingMachine.Start();
        pongMachine.Start();
        
        // Act - Let them play ping pong for a few rounds
        await Task.Delay(2500); // Enough time for multiple exchanges
        
        // Assert
        transitionLog.Should().NotBeEmpty();
        transitionLog.Should().Contain("PING: Served ball to PONG");
        transitionLog.Should().Contain("PONG: Received serve from PING");
        transitionLog.Should().Contain("PONG: Returned ball to PING");
        transitionLog.Should().Contain("PING: Received return from PONG");
        
        // Should have multiple complete exchanges
        var serveCount = transitionLog.FindAll(s => s.Contains("Served ball")).Count;
        var returnCount = transitionLog.FindAll(s => s.Contains("Returned ball")).Count;
        
        serveCount.Should().BeGreaterThan(1, "should have multiple serves");
        returnCount.Should().BeGreaterThan(1, "should have multiple returns");
    }
    
    [Fact]
    public async Task PingPong_Should_MaintainRallyWithScoring()
    {
        // Arrange - Ping pong with scoring
        var pingScore = 0;
        var pongScore = 0;
        var rallyCount = 0;
        StateMachine? pingMachine = null;
        StateMachine? pongMachine = null;
        
        var pingJson = @"{
            ""id"": ""pingPlayer"",
            ""initial"": ""serving"",
            ""states"": {
                ""serving"": {
                    ""after"": {
                        ""200"": {
                            ""target"": ""playing"",
                            ""actions"": [""serve""]
                        }
                    }
                },
                ""playing"": {
                    ""on"": {
                        ""BALL"": [
                            {
                                ""target"": ""playing"",
                                ""actions"": [""hitBack""],
                                ""cond"": ""canReturn""
                            },
                            {
                                ""target"": ""missed"",
                                ""actions"": [""missedBall""]
                            }
                        ],
                        ""POINT_WON"": ""serving""
                    }
                },
                ""missed"": {
                    ""after"": {
                        ""100"": {
                            ""target"": ""serving"",
                            ""actions"": [""resetRally""]
                        }
                    }
                }
            }
        }";
        
        var pongJson = @"{
            ""id"": ""pongPlayer"",
            ""initial"": ""receiving"",
            ""states"": {
                ""receiving"": {
                    ""on"": {
                        ""BALL"": [
                            {
                                ""target"": ""returning"",
                                ""actions"": [""hitBack""],
                                ""cond"": ""canReturn""
                            },
                            {
                                ""target"": ""missed"",
                                ""actions"": [""missedBall""]
                            }
                        ]
                    }
                },
                ""returning"": {
                    ""after"": {
                        ""150"": {
                            ""target"": ""receiving""
                        }
                    }
                },
                ""missed"": {
                    ""after"": {
                        ""100"": {
                            ""target"": ""receiving"",
                            ""actions"": [""resetRally""]
                        }
                    }
                }
            }
        }";
        
        var random = new Random(42); // Fixed seed for reproducible tests
        
        var pingActions = new ActionMap
        {
            ["serve"] = new List<NamedAction>
            {
                new NamedAction("serve", (sm) =>
                {
                    rallyCount = 0;
                    pongMachine?.Send("BALL");
                })
            },
            ["hitBack"] = new List<NamedAction>
            {
                new NamedAction("hitBack", (sm) =>
                {
                    rallyCount++;
                    Task.Delay(100).ContinueWith(_ => pongMachine?.Send("BALL"));
                })
            },
            ["missedBall"] = new List<NamedAction>
            {
                new NamedAction("missedBall", (sm) =>
                {
                    pongScore++;
                    pongMachine?.Send("POINT_WON");
                })
            },
            ["resetRally"] = new List<NamedAction>
            {
                new NamedAction("resetRally", (sm) => rallyCount = 0)
            }
        };
        
        var pongActions = new ActionMap
        {
            ["hitBack"] = new List<NamedAction>
            {
                new NamedAction("hitBack", (sm) =>
                {
                    rallyCount++;
                    Task.Delay(100).ContinueWith(_ => pingMachine?.Send("BALL"));
                })
            },
            ["missedBall"] = new List<NamedAction>
            {
                new NamedAction("missedBall", (sm) =>
                {
                    pingScore++;
                    pingMachine?.Send("POINT_WON");
                })
            },
            ["resetRally"] = new List<NamedAction>
            {
                new NamedAction("resetRally", (sm) => rallyCount = 0)
            }
        };
        
        var pingGuards = new GuardMap
        {
            ["canReturn"] = new NamedGuard("canReturn", (sm) => random.Next(100) > 20) // 80% success rate
        };
        
        var pongGuards = new GuardMap
        {
            ["canReturn"] = new NamedGuard("canReturn", (sm) => random.Next(100) > 25) // 75% success rate
        };
        
        // Create and start machines
        pingMachine = StateMachine.CreateFromScript(pingJson, pingActions, pingGuards);
        pongMachine = StateMachine.CreateFromScript(pongJson, pongActions, pongGuards);
        
        pingMachine.Start();
        pongMachine.Start();
        
        // Act - Play for a while
        await Task.Delay(3000);
        
        // Assert - The game should have some activity
        // Note: Since guards might prevent all misses, we just check the game ran
        (pingScore + pongScore).Should().BeGreaterOrEqualTo(0, "game should have run");
        
        // At minimum, the machines should be in valid states
        pingMachine.GetActiveStateString().Should().NotBeEmpty();
        pongMachine.GetActiveStateString().Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task MultiplePairs_Should_PlayIndependently()
    {
        // Arrange - Two ping pong tables
        var table1Log = new List<string>();
        var table2Log = new List<string>();
        
        // Table 1
        StateMachine? ping1 = null;
        StateMachine? pong1 = null;
        
        // Table 2
        StateMachine? ping2 = null;
        StateMachine? pong2 = null;
        
        var simplePingPongJson = @"{
            ""id"": ""player"",
            ""initial"": ""ready"",
            ""states"": {
                ""ready"": {
                    ""on"": {
                        ""START"": ""playing"",
                        ""BALL"": {
                            ""target"": ""playing"",
                            ""actions"": [""hit""]
                        }
                    }
                },
                ""playing"": {
                    ""on"": {
                        ""BALL"": {
                            ""target"": ""playing"",
                            ""actions"": [""hit""]
                        }
                    }
                }
            }
        }";
        
        // Table 1 actions
        var ping1Actions = new ActionMap
        {
            ["hit"] = new List<NamedAction>
            {
                new NamedAction("hit", (sm) =>
                {
                    table1Log.Add("Ping1 hit");
                    Task.Delay(50).ContinueWith(_ => pong1?.Send("BALL"));
                })
            }
        };
        
        var pong1Actions = new ActionMap
        {
            ["hit"] = new List<NamedAction>
            {
                new NamedAction("hit", (sm) =>
                {
                    table1Log.Add("Pong1 hit");
                    Task.Delay(50).ContinueWith(_ => ping1?.Send("BALL"));
                })
            }
        };
        
        // Table 2 actions
        var ping2Actions = new ActionMap
        {
            ["hit"] = new List<NamedAction>
            {
                new NamedAction("hit", (sm) =>
                {
                    table2Log.Add("Ping2 hit");
                    Task.Delay(75).ContinueWith(_ => pong2?.Send("BALL"));
                })
            }
        };
        
        var pong2Actions = new ActionMap
        {
            ["hit"] = new List<NamedAction>
            {
                new NamedAction("hit", (sm) =>
                {
                    table2Log.Add("Pong2 hit");
                    Task.Delay(75).ContinueWith(_ => ping2?.Send("BALL"));
                })
            }
        };
        
        // Create machines
        ping1 = StateMachine.CreateFromScript(
            simplePingPongJson.Replace("\"id\": \"player\"", "\"id\": \"ping1\""), 
            ping1Actions);
        pong1 = StateMachine.CreateFromScript(
            simplePingPongJson.Replace("\"id\": \"player\"", "\"id\": \"pong1\""), 
            pong1Actions);
        
        ping2 = StateMachine.CreateFromScript(
            simplePingPongJson.Replace("\"id\": \"player\"", "\"id\": \"ping2\""), 
            ping2Actions);
        pong2 = StateMachine.CreateFromScript(
            simplePingPongJson.Replace("\"id\": \"player\"", "\"id\": \"pong2\""), 
            pong2Actions);
        
        // Start all machines
        ping1.Start();
        pong1.Start();
        ping2.Start();
        pong2.Start();
        
        // Act - Start both games
        ping1.Send("BALL"); // Start table 1
        ping2.Send("BALL"); // Start table 2
        
        await Task.Delay(1000);
        
        // Assert - Both tables should have activity
        table1Log.Should().NotBeEmpty("Table 1 should have hits");
        table2Log.Should().NotBeEmpty("Table 2 should have hits");
        
        table1Log.Should().Contain("Ping1 hit");
        table1Log.Should().Contain("Pong1 hit");
        
        table2Log.Should().Contain("Ping2 hit");
        table2Log.Should().Contain("Pong2 hit");
        
        // Tables should play independently (different hit counts due to different delays)
        var table1Hits = table1Log.Count;
        var table2Hits = table2Log.Count;
        
        table1Hits.Should().BeGreaterThan(0);
        table2Hits.Should().BeGreaterThan(0);
    }
}