using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using XStateNet;

namespace InterMachineTests;

/// <summary>
/// Tests demonstrating how to check state machine states from outside
/// </summary>
public class StateCheckingTests
{
    [Fact]
    public async Task IsInState_Should_CheckCurrentState()
    {
        // Arrange
        var json = @"{
            ""id"": ""trafficLight"",
            ""initial"": ""red"",
            ""states"": {
                ""red"": {
                    ""on"": {
                        ""TIMER"": ""yellow""
                    }
                },
                ""yellow"": {
                    ""on"": {
                        ""TIMER"": ""green""
                    }
                },
                ""green"": {
                    ""on"": {
                        ""TIMER"": ""red""
                    }
                }
            }
        }";
        
        var machine = StateMachine.CreateFromScript(json);
        machine.Start();
        
        // Assert - Initial state (use full path with machine ID)
        machine.IsInState(machine, "#trafficLight.red").Should().BeTrue();
        machine.IsInState(machine, "#trafficLight.yellow").Should().BeFalse();
        machine.IsInState(machine, "#trafficLight.green").Should().BeFalse();
        
        // Act - Transition to yellow
        machine.Send("TIMER");
        await Task.Delay(50);
        
        // Assert - After first transition
        machine.IsInState(machine, "#trafficLight.red").Should().BeFalse();
        machine.IsInState(machine, "#trafficLight.yellow").Should().BeTrue();
        machine.IsInState(machine, "#trafficLight.green").Should().BeFalse();
        
        // Act - Transition to green
        machine.Send("TIMER");
        await Task.Delay(50);
        
        // Assert - After second transition
        machine.IsInState(machine, "#trafficLight.red").Should().BeFalse();
        machine.IsInState(machine, "#trafficLight.yellow").Should().BeFalse();
        machine.IsInState(machine, "#trafficLight.green").Should().BeTrue();
    }
    
    [Fact]
    public async Task GetActiveStateString_Should_ReturnCurrentStates()
    {
        // Arrange
        var json = @"{
            ""id"": ""hierarchicalMachine"",
            ""initial"": ""idle"",
            ""states"": {
                ""idle"": {
                    ""on"": {
                        ""START"": ""working""
                    }
                },
                ""working"": {
                    ""initial"": ""processing"",
                    ""states"": {
                        ""processing"": {
                            ""on"": {
                                ""PAUSE"": ""paused""
                            }
                        },
                        ""paused"": {
                            ""on"": {
                                ""RESUME"": ""processing""
                            }
                        }
                    },
                    ""on"": {
                        ""STOP"": ""idle""
                    }
                }
            }
        }";
        
        var machine = StateMachine.CreateFromScript(json);
        machine.Start();
        
        // Assert - Initial state
        var initialState = machine.GetActiveStateString(leafOnly: true);
        initialState.Should().Contain("idle");
        
        // Act - Start working
        machine.Send("START");
        await Task.Delay(50);
        
        // Assert - Working state
        var workingState = machine.GetActiveStateString(leafOnly: true);
        workingState.Should().Contain("processing");
        
        // Get full path
        var fullPath = machine.GetActiveStateString(leafOnly: false);
        fullPath.Should().Contain("working");
        fullPath.Should().Contain("processing");
    }
    
    [Fact]
    public async Task GetSourceSubStateCollection_Should_ReturnStateHierarchy()
    {
        // Arrange
        var json = @"{
            ""id"": ""nestedMachine"",
            ""initial"": ""level1"",
            ""states"": {
                ""level1"": {
                    ""initial"": ""level2"",
                    ""states"": {
                        ""level2"": {
                            ""initial"": ""level3"",
                            ""states"": {
                                ""level3"": {
                                    ""on"": {
                                        ""NEXT"": ""level3b""
                                    }
                                },
                                ""level3b"": {}
                            }
                        }
                    }
                }
            }
        }";
        
        var machine = StateMachine.CreateFromScript(json);
        machine.Start();
        await Task.Delay(50);
        
        // Act - Get current state hierarchy
        var states = machine.GetSourceSubStateCollection(null);
        var stateString = states.ToCsvString(machine, true);
        
        // Assert
        stateString.Should().Contain("level1");
        stateString.Should().Contain("level2");
        stateString.Should().Contain("level3");
        
        // Check nested state (use full paths)
        machine.IsInState(machine, "#nestedMachine.level1").Should().BeTrue();
        machine.IsInState(machine, "#nestedMachine.level1.level2").Should().BeTrue();
        machine.IsInState(machine, "#nestedMachine.level1.level2.level3").Should().BeTrue();
    }
    
    [Fact]
    public void ExternalStateChecking_Should_WorkWithMultipleMachines()
    {
        // Arrange - Create two independent machines
        var machine1Json = @"{
            ""id"": ""machine1"",
            ""initial"": ""on"",
            ""states"": {
                ""on"": {
                    ""on"": { ""TOGGLE"": ""off"" }
                },
                ""off"": {
                    ""on"": { ""TOGGLE"": ""on"" }
                }
            }
        }";
        
        var machine2Json = @"{
            ""id"": ""machine2"",
            ""initial"": ""inactive"",
            ""states"": {
                ""inactive"": {
                    ""on"": { ""ACTIVATE"": ""active"" }
                },
                ""active"": {
                    ""on"": { ""DEACTIVATE"": ""inactive"" }
                }
            }
        }";
        
        var machine1 = StateMachine.CreateFromScript(machine1Json);
        var machine2 = StateMachine.CreateFromScript(machine2Json);
        
        machine1.Start();
        machine2.Start();
        
        // Act & Assert - Check states independently
        machine1.IsInState(machine1, "#machine1.on").Should().BeTrue();
        machine2.IsInState(machine2, "#machine2.inactive").Should().BeTrue();
        
        // Toggle machine1
        machine1.Send("TOGGLE");
        machine1.IsInState(machine1, "#machine1.off").Should().BeTrue();
        machine1.IsInState(machine1, "#machine1.on").Should().BeFalse();
        
        // Machine2 should be unaffected
        machine2.IsInState(machine2, "#machine2.inactive").Should().BeTrue();
        
        // Activate machine2
        machine2.Send("ACTIVATE");
        machine2.IsInState(machine2, "#machine2.active").Should().BeTrue();
        machine2.IsInState(machine2, "#machine2.inactive").Should().BeFalse();
        
        // Machine1 should still be off
        machine1.IsInState(machine1, "#machine1.off").Should().BeTrue();
    }
    
    [Fact]
    public async Task ConditionalLogic_Should_UseStateChecking()
    {
        // Arrange
        var json = @"{
            ""id"": ""door"",
            ""initial"": ""closed"",
            ""states"": {
                ""closed"": {
                    ""on"": {
                        ""OPEN"": ""open""
                    }
                },
                ""open"": {
                    ""on"": {
                        ""CLOSE"": ""closed""
                    }
                }
            }
        }";
        
        var door = StateMachine.CreateFromScript(json);
        door.Start();
        
        // Act - Conditional logic based on state
        var result = "";
        
        if (door.IsInState(door, "#door.closed"))
        {
            result = "Door is closed, opening...";
            door.Send("OPEN");
        }
        await Task.Delay(50);
        
        if (door.IsInState(door, "#door.open"))
        {
            result += " Door is now open!";
        }
        
        // Assert
        result.Should().Be("Door is closed, opening... Door is now open!");
        door.IsInState(door, "#door.open").Should().BeTrue();
    }
    
    [Fact]
    public async Task ParallelStates_Should_CheckMultipleActiveStates()
    {
        // Arrange - Machine with parallel states
        var json = @"{
            ""id"": ""parallelMachine"",
            ""type"": ""parallel"",
            ""states"": {
                ""lights"": {
                    ""initial"": ""off"",
                    ""states"": {
                        ""off"": {
                            ""on"": { ""TURN_ON"": ""on"" }
                        },
                        ""on"": {
                            ""on"": { ""TURN_OFF"": ""off"" }
                        }
                    }
                },
                ""heating"": {
                    ""initial"": ""idle"",
                    ""states"": {
                        ""idle"": {
                            ""on"": { ""HEAT"": ""heating"" }
                        },
                        ""heating"": {
                            ""on"": { ""COOL"": ""idle"" }
                        }
                    }
                }
            }
        }";
        
        var machine = StateMachine.CreateFromScript(json);
        machine.Start();
        await Task.Delay(50);
        
        // Assert - Both parallel regions should be active
        var activeStates = machine.GetActiveStateString(leafOnly: false);
        activeStates.Should().Contain("lights");
        activeStates.Should().Contain("heating");
        activeStates.Should().Contain("off");
        activeStates.Should().Contain("idle");
        
        // Act - Turn on lights
        machine.Send("TURN_ON");
        await Task.Delay(50);
        
        // Assert - Lights should be on, heating still idle
        machine.IsInState(machine, "#parallelMachine.lights.on").Should().BeTrue();
        machine.IsInState(machine, "#parallelMachine.heating.idle").Should().BeTrue();
        
        // Act - Start heating
        machine.Send("HEAT");
        await Task.Delay(50);
        
        // Assert - Both should be in their active states
        machine.IsInState(machine, "#parallelMachine.lights.on").Should().BeTrue();
        machine.IsInState(machine, "#parallelMachine.heating.heating").Should().BeTrue();
    }
}