using System;
using System.Windows;
using System.Threading.Tasks;
using XStateNet;
using XStateNet.Distributed.EventBus;
using TimelineWPF.ViewModels;

namespace TimelineWPF
{
    public partial class DemoWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly RealTimeStateMachineAdapter _adapter;
        private readonly System.Collections.Generic.List<StateMachine> _machines = new();

        public DemoWindow()
        {
            InitializeComponent();

            // Use the TimelineComponent's existing DataContext
            _viewModel = (MainViewModel)TimelineControl.DataContext;

            // Use simpler RealTimeStateMachineAdapter for demo
            _adapter = new RealTimeStateMachineAdapter(_viewModel);

            Loaded += async (s, e) => await InitializeDemo();
            Closed += (s, e) => Cleanup();
        }

        private void LaunchCircuitBreakerSimulator_Click(object sender, RoutedEventArgs e)
        {
            var simulatorWindow = new CircuitBreakerSimulatorWindow();
            simulatorWindow.Show();
        }

        private async Task InitializeDemo()
        {
            // No initialization needed for RealTimeStateMachineAdapter

            // Create demo state machines
            var machine1 = CreateDemoMachine("demo-machine-1", "Traffic Light");
            var machine2 = CreateDemoMachine("demo-machine-2", "Door Controller");
            var machine3 = CreateDemoMachine("demo-machine-3", "Elevator");

            // Register machines with RealTimeStateMachineAdapter
            _adapter.RegisterStateMachine(machine1, "Traffic Light");
            _adapter.RegisterStateMachine(machine2, "Door Controller");
            _adapter.RegisterStateMachine(machine3, "Elevator");

            // Debug: Check if machines are registered
            var stateMachines = _viewModel.GetStateMachines().ToList();
            Console.WriteLine($"[DEBUG] Registered {stateMachines.Count} state machines in Timeline:");
            foreach (var sm in stateMachines)
            {
                Console.WriteLine($"  - {sm.Name}");
            }

            // Start simulation
            _viewModel.StartCommand.Execute(null);

            // Simulate events
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                machine1.Send("CHANGE");

                await Task.Delay(500);
                machine2.Send("OPEN");

                await Task.Delay(800);
                machine3.Send("CALL");

                await Task.Delay(1000);
                machine1.Send("CHANGE");

                await Task.Delay(500);
                machine2.Send("CLOSE");

                await Task.Delay(1000);
                machine3.Send("ARRIVE");

                await Task.Delay(500);
                machine1.Send("CHANGE");

                await Task.Delay(1000);
                machine3.Send("OPEN_DOOR");

                await Task.Delay(2000);
                machine3.Send("CLOSE_DOOR");
            });
        }

        private StateMachine CreateDemoMachine(string id, string type)
        {
            string json = type switch
            {
                "Traffic Light" => @"{
                    'id': " + id + @",
                    'initial': 'red',
                    'states': {
                        'red': {
                            'on': {
                                'CHANGE': {
                                    'target': 'green',
                                    'actions': ['logChange']
                                }
                            }
                        },
                        'green': {
                            'on': {
                                'CHANGE': {
                                    'target': 'yellow',
                                    'actions': ['logChange']
                                }
                            }
                        },
                        'yellow': {
                            'on': {
                                'CHANGE': {
                                    'target': 'red',
                                    'actions': ['logChange']
                                }
                            }
                        }
                    }
                }",

                "Door Controller" => @"{
                    'id': " + id + @",
                    'initial': 'closed',
                    'states': {
                        'closed': {
                            'on': {
                                'OPEN': {
                                    'target': 'opening',
                                    'actions': ['startMotor']
                                }
                            }
                        },
                        'opening': {
                            'after': {
                                '500': 'open'
                            }
                        },
                        'open': {
                            'on': {
                                'CLOSE': {
                                    'target': 'closing',
                                    'actions': ['startMotor']
                                }
                            }
                        },
                        'closing': {
                            'after': {
                                '500': 'closed'
                            }
                        }
                    }
                }",

                _ => @"{
                    'id': " + id + @",
                    'initial': 'idle',
                    'states': {
                        'idle': {
                            'on': {
                                'CALL': {
                                    'target': 'moving',
                                    'actions': ['startMoving']
                                }
                            }
                        },
                        'moving': {
                            'on': {
                                'ARRIVE': {
                                    'target': 'arrived',
                                    'actions': ['stopMoving']
                                }
                            }
                        },
                        'arrived': {
                            'on': {
                                'OPEN_DOOR': {
                                    'target': 'doorOpen',
                                    'actions': ['openDoor']
                                }
                            }
                        },
                        'doorOpen': {
                            'on': {
                                'CLOSE_DOOR': {
                                    'target': 'idle',
                                    'actions': ['closeDoor']
                                }
                            }
                        }
                    }
                }"
            };

            var actionMap = new ActionMap
            {
                ["logChange"] = new System.Collections.Generic.List<NamedAction> {
                    new NamedAction("logChange", _ => Console.WriteLine($"{type}: Changed state"))
                },
                ["startMotor"] = new System.Collections.Generic.List<NamedAction> {
                    new NamedAction("startMotor", _ => Console.WriteLine($"{type}: Motor started"))
                },
                ["startMoving"] = new System.Collections.Generic.List<NamedAction> {
                    new NamedAction("startMoving", _ => Console.WriteLine($"{type}: Moving"))
                },
                ["stopMoving"] = new System.Collections.Generic.List<NamedAction> {
                    new NamedAction("stopMoving", _ => Console.WriteLine($"{type}: Stopped"))
                },
                ["openDoor"] = new System.Collections.Generic.List<NamedAction> {
                    new NamedAction("openDoor", _ => Console.WriteLine($"{type}: Door opened"))
                },
                ["closeDoor"] = new System.Collections.Generic.List<NamedAction> {
                    new NamedAction("closeDoor", _ => Console.WriteLine($"{type}: Door closed"))
                }
            };

            var machine = StateMachineFactory.CreateFromScript(json, threadSafe: false, true, actionMap);
            machine.Start();
            _machines.Add(machine);
            return machine;
        }

        private void Cleanup()
        {
            foreach (var machine in _machines)
            {
                machine.Stop();
            }
            _adapter?.Dispose();
        }
    }
}