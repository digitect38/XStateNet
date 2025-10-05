using System.Threading.Tasks;
using XStateNet;
using XStateNet.UnitTest;
using Xunit;

// Suppress obsolete warning - standalone diagram framework test with no inter-machine communication
#pragma warning disable CS0618

namespace AdvancedFeatures;
public class DiagrammingFrameworkTests : IDisposable
{
    private ContextMap _context = new();

    private IStateMachine CreateStateMachine(string uniqueId)
    {
        var actionMap = new ActionMap();
        var guardMap = new GuardMap();

        // Add actions
        actionMap["setLButtonDown"] = [new("setLButtonDown", SetLButtonDown)];
        actionMap["startSelection"] = [new("startSelection", StartSelection)];
        actionMap["updateSelection"] = [new("updateSelection", UpdateSelection)];
        actionMap["endSelection"] = [new("endSelection", EndSelection)];

        actionMap["startMoving"] = [new("startMoving", StartMoving)];
        actionMap["endMoving"] = [new("endMoving", EndMoving)];
        actionMap["updateMoving"] = [new("updateMoving", UpdateMoving)];

        actionMap["startConnecting"] = [new("startConnecting", StartConnecting)];
        actionMap["endConnecting"] = [new("endConnecting", EndConnecting)];

        actionMap["startResizing"] = [new("startResizing", StartResizing)];
        actionMap["endResizing"] = [new("endResizing", EndResizing)];

        // Add guards
        guardMap["onShapeBody"] = new NamedGuard(OnShapeBody, "onShapeBody");
        guardMap["onCanvas"] = new NamedGuard(OnCanvas, "onCanvas");

        guardMap["onResizePadWithoutButton"] = new NamedGuard(onResizePadWithoutButton, "onResizePadWithoutButton");
        guardMap["onConnectionPinWithoutButton"] = new NamedGuard(onConnectionPinWithoutButton, "onConnectionPinWithoutButton");
        guardMap["onShapeBodyWithoutButton"] = new NamedGuard(onShapeBodyWithoutButton, "onShapeBodyWithoutButton");

        guardMap["noShapeSelected"] = new NamedGuard(NoShapeSelected, "noShapeSelected");

        // default context values
        _context["onResizePad"] = false;
        _context["onShapeBody"] = false;
        _context["onConnectionPin"] = false;
        _context["onTheOtherPin"] = false;
        _context["mouse_move"] = false;
        _context["l_button_down"] = false;
        _context["r_button_down"] = false;
        _context["selectionCount"] = 0;

        var script = GetScript(uniqueId);

        // Initialize state machine
        var s = StateMachineFactory.CreateFromScript(script, threadSafe: false, false, actionMap, guardMap);
        s.StartAsync().GetAwaiter().GetResult();
        return s;
    }


    // Test methods
    [Fact]
    public void TestInitialState()
    {
        var uniqueId = "TestInitialState_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        var states = stateMachine!.GetActiveStateNames();
        states.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public async Task TestLButtonDownOnShapeBody()
    {
        var uniqueId = "TestLButtonDownOnShapeBody_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        _context["onShapeBody"] = true;
        _context["onCanvas"] = false;
        await stateMachine!.SendAsync("L_BUTTON_DOWN");
        var states = stateMachine!.GetActiveStateNames();
        states.AssertEquivalence($"#{uniqueId}.selected.moving.idle");
    }

    [Fact]
    public async Task TestLButtonDownOnCanvas()
    {
        var uniqueId = "TestLButtonDownOnCanvas_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        _context["onShapeBody"] = false;
        _context["onCanvas"] = true;
        await stateMachine!.SendAsync("L_BUTTON_DOWN");
        var states = stateMachine!.GetActiveStateNames();
        states.AssertEquivalence($"#{uniqueId}.selecting");
    }

    [Fact]
    public async Task TestSelectionToIdle()
    {
        var uniqueId = "TestSelectionToIdle_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        _context["onShapeBody"] = false;
        _context["onCanvas"] = true;
        await stateMachine.SendAsync("L_BUTTON_DOWN");
        await stateMachine.SendAsync("MOUSE_MOVE");
        _context["selectionCount"] = 0;
        var states = await stateMachine.SendAsync("L_BUTTON_UP");
        states.AssertEquivalence($"#{uniqueId}.idle");
    }

    [Fact]
    public async Task TestSelectionToSelected()
    {
        var uniqueId = "TestSelectionToSelected_" + Guid.NewGuid().ToString("N");
        var stateMachine = CreateStateMachine(uniqueId);

        _context["onShapeBody"] = false;
        _context["onCanvas"] = true;
        await stateMachine.SendAsync("L_BUTTON_DOWN");
        await stateMachine.SendAsync("MOUSE_MOVE");
        _context["selectionCount"] = 1;
        var states = await stateMachine.SendAsync("L_BUTTON_UP");
        states.AssertEquivalence($"#{uniqueId}.selecting");
    }

    [Fact]
    public async Task TestMovingState()
    {
        var uniqueId = "stateMachine_" + Guid.NewGuid().ToString("N");
        var stateMachine =  CreateStateMachine(uniqueId);

        _context["onShapeBody"] = true;
        _context["onCanvas"] = false;
        var states = await stateMachine.SendAsync("L_BUTTON_DOWN");
        states.AssertEquivalence($"#{uniqueId}.selected.moving.idle");

        _context["onResize"] = false;

        states = await stateMachine.SendAsync("MOUSE_MOVE");
        states.AssertEquivalence($"#{uniqueId}.selected.moving.idle");

        states = await stateMachine.SendAsync("L_BUTTON_UP");
        states.AssertEquivalence($"#{uniqueId}.selected.moving.idle");
    }

    [Fact]
    public async Task TestResizingState()
    {
        var uniqueId = "TestResizingState_" + Guid.NewGuid().ToString("N");
        var stateMachine =   CreateStateMachine(uniqueId);

        _context["onShapeBody"] = true;
        _context["onCanvas"] = false;
        var states = await stateMachine.SendAsync("L_BUTTON_DOWN");

        //var states = stateMachine.GetActiveStateNames();
        states.AssertEquivalence($"#{uniqueId}.selected.moving.idle");


        _context["onShapeBody"] = false;
        _context["onResizePad"] = true;
        _context["l_button_down"] = false;

        await stateMachine.SendAsync("MOUSE_MOVE");
        states = await stateMachine.SendAsync("MOUSE_MOVE");

        //states = stateMachine.GetActiveStateNames();
        states.AssertEquivalence($"#{uniqueId}.selected.resizing.idle");

        states = await stateMachine.SendAsync("L_BUTTON_DOWN");
        states.AssertEquivalence($"#{uniqueId}.selected.resizing.resizing");
    }


    // Action methods
    private void SetLButtonDown(StateMachine sm)
    {
        _context["l_button_down"] = true;
    }

    private void StartSelection(StateMachine sm)
    {
        _context["selectionCount"] = 0;
    }

    private void UpdateSelection(StateMachine sm)
    {
        // Add selectionCount update logic
    }

    private void EndSelection(StateMachine sm)
    {
        // Add selectionCount decrease logic
    }

    private void StartMoving(StateMachine sm)
    {
        // Add move start logic
    }

    private void EndMoving(StateMachine sm)
    {
        // Add move end logic
    }

    private void UpdateMoving(StateMachine sm)
    {
        // Add move update logic
    }

    private void StartConnecting(StateMachine sm)
    {

    }

    private void EndConnecting(StateMachine sm)
    {

    }

    private void StartResizing(StateMachine sm)
    {

    }

    private void EndResizing(StateMachine sm)
    {

    }

    // Guard methods
    private bool OnShapeBody(StateMachine sm)
    {
        bool val = (bool)(_context["onShapeBody"] ?? false);
        return val;
    }

    private bool OnCanvas(StateMachine sm)
    {
        bool val = (bool)(_context["onCanvas"] ?? false);
        return val;
    }

    private bool NoShapeSelected(StateMachine sm)
    {
        var val = (int)(_context["selectionCount"] ?? 0);
        return (int)val == 0;
    }

    private bool onResizePadWithoutButton(StateMachine sm)
    {
        var val = (bool)(_context["onResizePad"] ?? false) && !(bool)(_context["l_button_down"] ?? false);
        return val;
    }

    private bool onConnectionPinWithoutButton(StateMachine sm)
    {
        var val = (bool)(_context["onConnectionPin"] ?? false) && !(bool)(_context["l_button_down"] ?? false) && !(bool)(_context["r_button_down"] ?? false);
        return val;
    }

    private bool onShapeBodyWithoutButton(StateMachine sm)
    {
        var val = (bool)(_context["onShapeBody"] ?? false) && !(bool)(_context["l_button_down"] ?? false) && !(bool)(_context["r_button_down"] ?? false);
        return val;
    }

    private static string GetScript(string uniqueId) =>
        @"   {
        'context': {
            'onCanvas': false,
            'onResizePad': false,
            'onShapeBody': false,
            'onConnectionPin': false,
            'onTheOtherPin': false,
            'mouse_move': false,
            'l_button_down': false,
            'r_button_down': false,
            'selectionCount': 0
        },
        'id': '" + uniqueId + @"',
        'initial': 'idle',
        'on': {
            'RESET': {
                'target': 'idle',
                'actions': ['resetContext', 'logStateAfterUpdate']
            },
            'UPDATE_CONTEXT_FROM_REQUEST': {
                'actions': 'updateContextFromRequest'
            }
        },
        'states': {
            'idle': {
                'on': {
                    'L_BUTTON_DOWN': [
                        {
                            'target': '#" + uniqueId + @".selected.moving',
                            'cond': 'onShapeBody',
                            'actions': 'setLButtonDown'
                        },
                        {
                            'target': 'selecting',
                            'cond': 'onCanvas',
                            'actions': ['setLButtonDown', 'startSelection']
                        },
                        {
                            'actions': 'setLButtonDown'
                        }
                    ]
                    
                }
            },
            'selecting': {
                'on': {
                    'MOUSE_MOVE': {
                        'actions': 'updateSelection'
                    },
                    'L_BUTTON_UP':
                        {
                            'target': 'idle',
                            'cond': 'noShapeSelected',
                            'actions': 'setLButtonUp'
                        },
                }
            },
            'selected': {
                'initial': 'moving',

                // original position
            
                'on': {
                    'MOUSE_MOVE': 
                        {
                            'target': '.resizing',
                            'cond': 'onResizePadWithoutButton'
                        },
                    
                }, 
                // original end
            
                'states': {
                    'moving': {
                        'initial': 'idle',
                    
                        // debug tricky trial start
    /*
					    on : {
						    'MOUSE_MOVE': 
								    {
										    'target': 'resizing',
										    'cond': 'onResizePadWithoutButton'
								    },
											
					    },
    */
                        // trial end

                        'states': {
                            'idle': {
                                'on': {
                                    'L_BUTTON_DOWN': {
                                        'target': 'moving',
                                        'actions': ['setLButtonDown', 'activateResizePad']
                                    },                                
														    }
                            },
                            'moving': {
                                'entry': 'startMoving',
                                'exit': 'endMoving',
                                'on': {
                                    'L_BUTTON_UP': {
                                        'target': 'idle',
                                        'actions': 'setLButtonUp'
                                    },
                                    'MOUSE_MOVE': {
                                        'actions': 'updateMoving'
                                    }
                                }
                            }
                        }
                    },
                    'connecting': {
                        'initial': 'idle',
                        'entry': 'startConnecting',
                        'exit': 'endConnecting',
                        'states': {
                            'idle': {
                                'on': {
                                    'L_BUTTON_DOWN': {
                                        'target': 'startConnecting',
                                        'actions': 'setLButtonDown'
                                    }
                                }
                            },
                            'startConnecting': {
                                'on': {
                                    'L_BUTTON_UP': {
                                        'target': 'findingEndPin',
                                        'actions': 'setLButtonUp'
                                    }
                                }
                            },
                            'findingEndPin': {
                                'on': {
                                    'MOUSE_MOVE':
                                        {
                                            'target': 'endPinFound',
                                            'cond': 'onTheOtherPin'
                                        },
                                }
                            },
                            'endPinFound': {
                                'on': {
                                    'L_BUTTON_DOWN': {
                                        'target': 'idle',
                                        'actions': 'setLButtonDown'
                                    }
                                }
                            }
                        }
                    },
                    'resizing': {
                        'initial': 'idle',
                        'entry': 'startResizing',
                        'exit': 'endResizing',
                        'states': {
                            'idle': {
                                'on': {
                                    'L_BUTTON_DOWN': {
                                        'target': 'resizing',
                                        'actions': 'setLButtonDown'
                                    }
                                }
                            },
                            'resizing': {
                                'on': {
                                    'MOUSE_MOVE': {
                                        'actions': 'updateResizing'
                                    },
                                    'L_BUTTON_UP': {
                                        'target': 'idle',
                                        'actions': 'setLButtonUp'
                                    }
                                }
                            }
                        }
                    }
                }            
            }
        }
    }";


    public void Dispose()
    {
        // Cleanup if needed
    }
}



