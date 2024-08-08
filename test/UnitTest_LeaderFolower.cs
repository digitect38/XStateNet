using NUnit.Framework;
using XStateNet;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedFeatures
{
    [TestFixture]
    public class LeaderFollowerStateMachinesTests
    {
        private StateMachine _followerStateMachine;
        private StateMachine _leaderStateMachine;

        private ConcurrentDictionary<string, List<NamedAction>> _followerActions;
        private ConcurrentDictionary<string, NamedGuard> _followerGuards;

        private ConcurrentDictionary<string, List<NamedAction>> _leaderActions;
        private ConcurrentDictionary<string, NamedGuard> _leaderGuards;

        [SetUp]
        public void Setup()
        {
            // Follower actions
            _followerActions = new ConcurrentDictionary<string, List<NamedAction>>();
            _followerGuards = new ConcurrentDictionary<string, NamedGuard>();

            // Leader actions
            _leaderActions = new ConcurrentDictionary<string, List<NamedAction>>
            {
                ["sendToFollowerToB"] = new List<NamedAction> { new NamedAction("sendToFollowerToB", (sm) => _followerStateMachine.Send("to_b")) },
                ["sendToFollowerToA"] = new List<NamedAction> { new NamedAction("sendToFollowerToA", (sm) => _followerStateMachine.Send("to_a")) }
            };
            _leaderGuards = new ConcurrentDictionary<string, NamedGuard>();

            // Load state machines from JSON
            var followerJson = LeaderFollowerStateMachine.FollowerStateMachineScript;
            var leaderJson = LeaderFollowerStateMachine.LeaderStateMachineScript;

            _followerStateMachine = StateMachine.CreateFromScript(followerJson, _followerActions, _followerGuards).Start();
            _leaderStateMachine = StateMachine.CreateFromScript(leaderJson, _leaderActions, _leaderGuards).Start();
        }

        [Test]
        public async Task TestLeaderFollowerStateMachines()
        {
            // Initially, both state machines should be in state 'a'
            Assert.AreEqual(_followerStateMachine.GetCurrentState(), "#follower.a");
            Assert.AreEqual(_leaderStateMachine.GetCurrentState(), "#leader.a");

            // Wait for the leader to send the 'to_b' event to the follower
            await Task.Delay(1100);
            Assert.AreEqual(_followerStateMachine.GetCurrentState(), "#follower.b");
            Assert.AreEqual(_leaderStateMachine.GetCurrentState(), "#leader.b");

            // Wait for the leader to send the 'to_a' event to the follower
            await Task.Delay(1100);
            Assert.AreEqual(_followerStateMachine.GetCurrentState(), "#follower.a");
            Assert.AreEqual(_leaderStateMachine.GetCurrentState(), "#leader.a");
        }
        public static class LeaderFollowerStateMachine
        {
            public static string FollowerStateMachineScript => @"
            {
                'id': 'follower',
                'initial': 'a',
                'states': {
                    'a': {
                        'on': {
                            'to_b': {
                                'target': 'b'
                            }
                        }
                    },
                    'b': {
                        'on': {
                            'to_a': {
                                'target': 'a'
                            }
                        }
                    }
                }
            }";

            public static string LeaderStateMachineScript => @"
            {
                'id': 'leader',
                'initial': 'a',
                'states': {
                    'a': {
                        'after': {
                            '1000': {
                                'target': 'b',
                                'actions': ['sendToFollowerToB']
                            }
                        }
                    },
                    'b': {
                        'after': {
                            '1000': {
                                'target': 'a',
                                'actions': ['sendToFollowerToA']
                            }
                        }
                    }
                }
            }";
        }
    }
}
