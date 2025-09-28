using System;
using Xunit;
using Xunit.Abstractions;
using XStateNet;

namespace Test
{
    /// <summary>
    /// Tests for StateMachineFactory helper methods
    /// </summary>
    [Collection("Sequential")]
    public class StateMachineFactoryTests
    {
        private readonly ITestOutputHelper _output;

        public StateMachineFactoryTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ReplaceMachineId_ReplacesDefinitionOnly()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""originalMachine"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""active""
                        }
                    },
                    ""active"": {}
                }
            }";

            // Act
            var result = StateMachineFactory.ReplaceMachineId(jsonScript, "newMachine", preserveReferences: true);

            // Assert
            Assert.Contains(@"""id"": ""newMachine""", result);
            Assert.DoesNotContain(@"""id"": ""originalMachine""", result);
            _output.WriteLine("Successfully replaced machine ID definition");
        }

        [Fact]
        public void ReplaceMachineId_PreservesReferences()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""machine1"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""CHECK"": [
                                {
                                    ""target"": ""checking"",
                                    ""in"": ""#otherMachine.ready""
                                }
                            ]
                        }
                    },
                    ""checking"": {
                        ""invoke"": {
                            ""id"": ""#referencedService""
                        }
                    }
                }
            }";

            // Act
            var result = StateMachineFactory.ReplaceMachineId(jsonScript, "newMachine", preserveReferences: true);

            // Assert
            Assert.Contains(@"""id"": ""newMachine""", result);  // Main ID replaced
            Assert.Contains(@"""id"": ""#referencedService""", result);  // Reference preserved
            Assert.DoesNotContain(@"""id"": ""machine1""", result);
            _output.WriteLine("Successfully replaced definition while preserving references");
        }

        [Fact]
        public void ReplaceMachineId_ReplaceAllWhenNotPreserving()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""machine1"",
                ""states"": {
                    ""idle"": {
                        ""invoke"": {
                            ""id"": ""#referencedService""
                        }
                    }
                }
            }";

            // Act
            var result = StateMachineFactory.ReplaceMachineId(jsonScript, "newMachine", preserveReferences: false);

            // Assert
            Assert.Contains(@"""id"": ""newMachine""", result);  // Main ID replaced
            Assert.Contains(@"""id"": ""#newMachine""", result);  // Reference also replaced
            Assert.DoesNotContain(@"""id"": ""#referencedService""", result);
            _output.WriteLine("Successfully replaced all IDs including references");
        }

        [Fact]
        public void CreateFromScript_WithCustomMachineId()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""customMachine"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": ""active""
                        }
                    },
                    ""active"": {}
                }
            }";

            // Act
            var machine = new StateMachine();
            StateMachineFactory.CreateFromScript(machine, jsonScript, threadSafe: false);
            machine.Start();

            // Assert
            Assert.Equal("#customMachine.idle", machine.GetActiveStateNames());
            _output.WriteLine($"Created machine with custom ID: {machine.machineId}");
        }

        [Fact]
        public void CreateMultipleMachinesFromTemplate()
        {
            // Arrange
            var templateScript = @"{
                ""id"": ""pingPongMachine"",
                ""initial"": ""ready"",
                ""states"": {
                    ""ready"": {
                        ""on"": {
                            ""PING"": ""ponged"",
                            ""PONG"": ""pinged""
                        }
                    },
                    ""pinged"": {},
                    ""ponged"": {}
                }
            }";

            // Act - Create multiple machines from same template
            var machine1 = new StateMachine();
            var machine2 = new StateMachine();
            var machine3 = new StateMachine();

            StateMachineFactory.CreateFromScript(machine1, templateScript, threadSafe: false, guidIsolate: true);
            StateMachineFactory.CreateFromScript(machine2, templateScript, threadSafe: false, guidIsolate: true);
            StateMachineFactory.CreateFromScript(machine3, templateScript, threadSafe: false, guidIsolate: true);

            machine1.Start();
            machine2.Start();
            machine3.Start();

            // Assert

            Assert.Equal($"{machine1.machineId}.ready", machine1.GetActiveStateNames());
            Assert.Equal($"{machine2.machineId}.ready", machine2.GetActiveStateNames());
            Assert.Equal($"{machine3.machineId}.ready", machine3.GetActiveStateNames());

            _output.WriteLine("Successfully created multiple machines from template with different IDs");
        }

        [Fact]
        public void ComplexScriptWithNestedReferences()
        {
            // Arrange - Complex script with various reference patterns
            var jsonScript = @"{
                ""id"": ""mainMachine"",
                ""initial"": ""idle"",
                ""context"": {
                    ""relatedMachine"": ""#externalMachine""
                },
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""START"": [
                                {
                                    ""target"": ""active"",
                                    ""in"": ""#monitor.healthy""
                                }
                            ]
                        }
                    },
                    ""active"": {
                        ""invoke"": {
                            ""id"": ""#serviceWorker"",
                            ""src"": ""workerService""
                        }
                    }
                }
            }";

            // Act
            var result = StateMachineFactory.ReplaceMachineId(jsonScript, "myMachine", preserveReferences: true);

            // Assert
            Assert.Contains(@"""id"": ""myMachine""", result);  // Main ID replaced
            Assert.Contains(@"""id"": ""#serviceWorker""", result);  // Service reference preserved
            Assert.Contains(@"""#monitor.healthy""", result);  // In-condition reference preserved
            Assert.Contains(@"""#externalMachine""", result);  // Context reference preserved
            Assert.DoesNotContain(@"""id"": ""mainMachine""", result);

            _output.WriteLine("Successfully handled complex script with nested references");
        }

        [Fact]
        public void MachineIdIsolatedScript_IsolatesSimpleMachineId()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""myMachine"",
                ""initial"": ""idle"",
                ""states"": {
                    ""idle"": {},
                    ""active"": {}
                }
            }";

            // Act
            var result = StateMachineFactory.MachineIdIsolatedScript(jsonScript);

            // Assert
            Assert.DoesNotContain(@"""id"": ""myMachine""", result);
            Assert.Contains(@"""id"": ""myMachine_", result);  // Should have GUID suffix
            _output.WriteLine($"Isolated script: {result}");
        }

        [Fact]
        public void MachineIdIsolatedScript_ReplacesReferences()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""parentMachine"",
                ""initial"": ""waiting"",
                ""context"": {
                    ""relatedMachine"": ""#parentMachine""
                },
                ""states"": {
                    ""waiting"": {
                        ""on"": {
                            ""CHECK"": [
                                {
                                    ""target"": ""checking"",
                                    ""in"": ""#parentMachine.ready""
                                }
                            ]
                        }
                    },
                    ""checking"": {
                        ""invoke"": {
                            ""id"": ""#parentMachine.worker""
                        }
                    }
                }
            }";

            // Act
            var result = StateMachineFactory.MachineIdIsolatedScript(jsonScript);

            // Assert
            Assert.DoesNotContain(@"""id"": ""parentMachine""", result);
            Assert.DoesNotContain(@"""#parentMachine""", result);  // Old references should be gone
            Assert.DoesNotContain(@"""#parentMachine.ready""", result);
            Assert.DoesNotContain(@"""#parentMachine.worker""", result);

            // Should contain new isolated ID with GUID
            Assert.Contains(@"""id"": ""parentMachine_", result);
            Assert.Contains(@"""#parentMachine_", result);  // References should be updated

            _output.WriteLine($"Successfully replaced all references with isolated ID");
        }

        [Fact]
        public void MachineIdIsolatedScript_PreservesOtherReferences()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""myMachine"",
                ""states"": {
                    ""idle"": {
                        ""on"": {
                            ""CHECK"": [
                                {
                                    ""target"": ""active"",
                                    ""in"": ""#otherMachine.ready""
                                }
                            ]
                        }
                    },
                    ""active"": {
                        ""invoke"": {
                            ""id"": ""#externalService""
                        }
                    }
                }
            }";

            // Act
            var result = StateMachineFactory.MachineIdIsolatedScript(jsonScript);

            // Assert
            Assert.DoesNotContain(@"""id"": ""myMachine""", result);
            Assert.Contains(@"""id"": ""myMachine_", result);

            // Other references should be preserved
            Assert.Contains(@"""#otherMachine.ready""", result);
            Assert.Contains(@"""#externalService""", result);

            _output.WriteLine("Successfully preserved unrelated references");
        }

        [Fact]
        public void MachineIdIsolatedScript_HandlesNoMachineId()
        {
            // Arrange - Script with reference-only ID
            var jsonScript = @"{
                ""id"": ""#referencedMachine"",
                ""states"": {
                    ""idle"": {}
                }
            }";

            // Act
            var result = StateMachineFactory.MachineIdIsolatedScript(jsonScript);

            // Assert
            Assert.Equal(jsonScript, result); // Should return unchanged
            _output.WriteLine("Correctly handled script with no machine ID definition");
        }

        [Fact]
        public void MachineIdIsolatedScript_GeneratesUniqueIds()
        {
            // Arrange
            var jsonScript = @"{
                ""id"": ""testMachine"",
                ""initial"": ""idle""
            }";

            // Act - Call twice to get two different results
            var result1 = StateMachineFactory.MachineIdIsolatedScript(jsonScript);
            var result2 = StateMachineFactory.MachineIdIsolatedScript(jsonScript);

            // Assert - Both should have isolated IDs but different GUIDs
            Assert.NotEqual(result1, result2);
            Assert.DoesNotContain(@"""id"": ""testMachine""", result1);
            Assert.DoesNotContain(@"""id"": ""testMachine""", result2);
            Assert.Contains(@"""id"": ""testMachine_", result1);
            Assert.Contains(@"""id"": ""testMachine_", result2);

            _output.WriteLine("Successfully generated unique isolated IDs");
        }
    }
}