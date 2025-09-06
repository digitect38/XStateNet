using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace AdvancedFeatures;

[TestFixture]
public class MutualExclusionTests
{
    private StateMachine _stateMachine;

    [SetUp]
    public void Setup()
    {
        var actionCallbacks = new ActionMap();
        var guardCallbacks = new GuardMap();

        string jsonScript = @"
        {
            ""id"": ""mutualExclusion"",
            ""type"": ""parallel"",
            ""states"": {
                ""shooter"": {
                    ""initial"": ""wait"",
                    ""states"": {
                        ""wait"": {
                            ""on"": {
                                ""SHOOT"": {
                                    ""target"": ""shoot"",
                                    ""in"": ""#mutualExclusion.trashCan.open""
                                }
                            }
                        },
                        ""shoot"": {
                            ""on"": {
                                ""DONE"": ""wait""
                            }
                        }
                    }
                },
                ""trashCan"": {
                    ""initial"": ""closed"",
                    ""states"": {
                        ""open"": {
                            ""on"": {
                                ""CLOSE"": {
                                    ""target"": ""closed"",
                                    ""in"": ""#mutualExclusion.shooter.wait""
                                }
                            }
                        },
                        ""closed"": {
                            ""on"": {
                                ""OPEN"": ""open""
                            }
                        }
                    }
                }
            }
        }";

        _stateMachine = StateMachine.CreateFromScript(jsonScript, actionCallbacks, guardCallbacks);
        _stateMachine!.Start();
    }

    [Test]
    public void TestInitialState()
    {
        _stateMachine!.GetActiveStateString().AssertEquivalence("#mutualExclusion.shooter.wait;#mutualExclusion.trashCan.closed");
    }

    [Test]
    public void TestTransitionShoot()
    {
        _stateMachine!.Send("OPEN");
        _stateMachine!.Send("SHOOT");

        _stateMachine!.GetActiveStateString().AssertEquivalence("#mutualExclusion.shooter.shoot;#mutualExclusion.trashCan.open");
    }

    [Test]
    public void TestTransitionCannotShoot()
    {
        _stateMachine!.Send("SHOOT");

        _stateMachine!.GetActiveStateString().AssertEquivalence("#mutualExclusion.shooter.wait;#mutualExclusion.trashCan.closed");
    }


    [Test]
    public void TestTransitionCannotClose()
    {
        // trashcan should not be closed while shooting!
        _stateMachine!.Send("OPEN");
        _stateMachine!.Send("SHOOT");
        _stateMachine!.Send("CLOSE");

        _stateMachine!.GetActiveStateString().AssertEquivalence("#mutualExclusion.shooter.shoot;#mutualExclusion.trashCan.open");
    }

    [Test]
    public void TestTransitionCanClose()
    {
        // trashcan can be closed after if shooting is done!
        _stateMachine!.Send("OPEN");
        _stateMachine!.Send("SHOOT");
        _stateMachine!.Send("DONE");
        _stateMachine!.Send("CLOSE");

        _stateMachine!.GetActiveStateString().AssertEquivalence("#mutualExclusion.shooter.wait;#mutualExclusion.trashCan.closed");
    }

    [Test]
    public void TestShootAndDoneTransition()
    {
        _stateMachine!.Send("OPEN");
        _stateMachine!.Send("SHOOT");
        _stateMachine!.Send("DONE");

        _stateMachine!.GetActiveStateString().AssertEquivalence("#mutualExclusion.shooter.wait;#mutualExclusion.trashCan.open");
    }

    [Test]
    public void TestOpenAndCloseTransition()
    {
        _stateMachine!.Send("OPEN");
        _stateMachine!.Send("CLOSE");
        var stateString = _stateMachine!.GetActiveStateString();
        _stateMachine!.GetActiveStateString().AssertEquivalence("#mutualExclusion.shooter.wait;#mutualExclusion.trashCan.closed");
    }
}
