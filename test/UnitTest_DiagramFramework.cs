using NUnit.Framework;
using XStateNet;
using XStateNet.UnitTest;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace AdvancedFeatures;
public class DiagrammingFrameworkTests
{
    private StateMachine _stateMachine;
    private ContextMap _context = new ();

    ActionMap actionMap = new();
    GuardMap guardMap = new();

    [SetUp]
    public void Setup()
    {

        // 액션 추가
        actionMap["setLButtonDown"]     = [new ("setLButtonDown",   SetLButtonDown  )];
        actionMap["startSelection"]     = [new ("startSelection",   StartSelection  )];
        actionMap["updateSelection"]    = [new ("updateSelection",  UpdateSelection )];
        actionMap["endSelection"]       = [ new ("endSelection",    EndSelection    )];

        actionMap["startMoving"]        = [new ("startMoving",      StartMoving     )];
        actionMap["endMoving"]          = [new ("endMoving",        EndMoving       )];
        actionMap["updateMoving"]       = [new ("updateMoving",     UpdateMoving    )];

        actionMap["startConnecting"]    = [new ("startConnecting",  StartConnecting )];
        actionMap["endConnecting"]      = [new ("endConnecting",    EndConnecting   )];

        actionMap["startResizing"]      = [new("startResizing",     StartResizing   )];
        actionMap["endResizing"]        = [new("endResizing",       EndResizing     )];

        // 가드 추가
        guardMap["onShapeBody"]         = new NamedGuard("onShapeBody", OnShapeBody);
        guardMap["onCanvas"]            = new NamedGuard("onCanvas", OnCanvas);

        guardMap["onResizePadWithoutButton"] = new NamedGuard("onResizePadWithoutButton", onResizePadWithoutButton);
        guardMap["onConnectionPinWithoutButton"] = new NamedGuard("onConnectionPinWithoutButton", onConnectionPinWithoutButton);
        guardMap["onShapeBodyWithoutButton"] = new NamedGuard("onShapeBodyWithoutButton", onShapeBodyWithoutButton);

        guardMap["noShapeSelected"] = new NamedGuard("noShapeSelected", NoShapeSelected);

        // default context values
        _context["onResizePad"] = false;
        _context["onShapeBody"] = false;
        _context["onConnectionPin"] = false;
        _context["onTheOtherPin"] = false;
        _context["mouse_move"] = false;
        _context["l_button_down"] = false;
        _context["r_button_down"] = false;
        _context["selectionCount"] = 0;

        // 상태 머신 초기화
        _stateMachine = StateMachine.CreateFromScript(script, actionMap, guardMap).Start();
    }


    // 유닛 테스트 메서드
    [Test]
    public void TestInitialState()
    {
        var states = _stateMachine.GetActiveStateString();
        states.AssertEquivalence("#stateMachine.idle");
    }

    [Test]
    public void TestLButtonDownOnShapeBody()
    {
        _context["onShapeBody"] = true;
        _context["onCanvas"] = false;
        _stateMachine.Send("L_BUTTON_DOWN");
        var states = _stateMachine.GetActiveStateString();
        states.AssertEquivalence("#stateMachine.selected.moving.idle");
    }

    [Test]
    public void TestLButtonDownOnCanvas()
    {
        _context["onShapeBody"] = false;
        _context["onCanvas"] = true;
        _stateMachine.Send("L_BUTTON_DOWN");
        var states = _stateMachine.GetActiveStateString();
        states.AssertEquivalence("#stateMachine.selecting");
    }

    [Test]
    public void TestSelectionToIdle()
    {
        _context["onShapeBody"] = false;
        _context["onCanvas"] = true;
        _stateMachine.Send("L_BUTTON_DOWN");
        _stateMachine.Send("MOUSE_MOVE");
        _context["selectionCount"] = 0;
        _stateMachine.Send("L_BUTTON_UP");
        var states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.idle");
    }

    [Test]
    public void TestSelectionToSelected()
    {
        _context["onShapeBody"] = false;
        _context["onCanvas"] = true;
        _stateMachine.Send("L_BUTTON_DOWN");
        _stateMachine.Send("MOUSE_MOVE");
        _context["selectionCount"] = 1;
        _stateMachine.Send("L_BUTTON_UP");
        var states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.selecting");
    }

    [Test]
    public void TestMovingState()
    {
        _context["onShapeBody"] = true;
        _context["onCanvas"] = false;
        _stateMachine.Send("L_BUTTON_DOWN");

        var states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.selected.moving.idle");

        _context["onResize"] = false;

        _stateMachine.Send("MOUSE_MOVE");
        states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.selected.moving.idle");

        _stateMachine.Send("L_BUTTON_UP");
        states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.selected.moving.idle");
    }

    [Test]
    public void TestResizingState()
    {
        _context["onShapeBody"] = true;
        _context["onCanvas"] = false;
        _stateMachine.Send("L_BUTTON_DOWN");

        var states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.selected.moving.idle");


        _context["onShapeBody"] = false;
        _context["onResizePad"] = true;
        _context["l_button_down"] = false;

        _stateMachine.Send("MOUSE_MOVE");

        states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.selected.resizing.idle");

        _stateMachine.Send("L_BUTTON_UP");
        states = _stateMachine.GetActiveStateString();
        states.Equals("#stateMachine.selected.resizing.idle");
    }


    // 액션 메서드
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
        // selectionCount 업데이트 로직 추가
    }

    private void EndSelection(StateMachine sm)
    {
        // selectionCount 종료 로직 추가
    }

    private void StartMoving(StateMachine sm)
    {
        // 이동 시작 로직 추가
    }

    private void EndMoving(StateMachine sm)
    {
        // 이동 종료 로직 추가
    }

    private void UpdateMoving(StateMachine sm)
    {
        // 이동 업데이트 로직 추가
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

    // 가드 메서드
    private bool OnShapeBody(StateMachine sm)
    {
        bool val = (bool)_context["onShapeBody"];
        return val;
    }

    private bool OnCanvas(StateMachine sm)
    {
        bool val = (bool)_context["onCanvas"];
        return val;
    }

    private bool NoShapeSelected(StateMachine sm)
    {
        var val = (int)_context["selectionCount"];
        return (int)val == 0;
    }

    private bool onResizePadWithoutButton(StateMachine sm)
    {
        var val = (bool)_context["onResizePad"] && !(bool)_context["l_button_down"];
        return val;
    }

    private bool onConnectionPinWithoutButton(StateMachine sm)
    {
        var val = (bool)_context["onConnectionPin"] && !(bool)_context["l_button_down"] && !(bool)_context["r_button_down"];
        return val;
    }

    private bool onShapeBodyWithoutButton(StateMachine sm)
    {
        var val = (bool)_context["onShapeBody"] && !(bool)_context["l_button_down"] && !(bool)_context["r_button_down"];
        return val;
    }



    private static string script =
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
    'id': 'stateMachine',
    'initial': 'idle',
    'on': {
        'RESET': {
            'target': 'idle',
            'actions': ['resetContext', 'logStateAfterUpdate']
        },
        'UPDATE_CONTEXT_FROM_REQUEST': {
            'actions': ['updateContextFromRequest']
        }
    },
    'states': {
        'idle': {
            'on': {
                'L_BUTTON_DOWN': [
                    {
                        'target': '#stateMachine.selected.moving',
                        'cond': 'onShapeBody',
                        'actions': ['setLButtonDown']
                    },
                    {
                        'target': 'selecting',
                        'cond': 'onCanvas',
                        'actions': ['setLButtonDown', 'startSelection']
                    },
                    {
                        'actions': ['setLButtonDown']
                    }
                ]
                    
            }
        },
        'selecting': {
            'on': {
                'MOUSE_MOVE': {
                    'actions': ['updateSelection']
                },
                'L_BUTTON_UP':
                    {
                        'target': 'idle',
                        'cond': 'noShapeSelected',
                        'actions': ['setLButtonUp']
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
                            'entry': ['startMoving'],
                            'exit': ['endMoving'],
                            'on': {
                                'L_BUTTON_UP': {
                                    'target': 'idle',
                                    'actions': ['setLButtonUp']
                                },
                                'MOUSE_MOVE': {
                                    'actions': ['updateMoving']
                                }
                            }
                        }
                    }
                },
                'connecting': {
                    'initial': 'idle',
                    'entry': ['startConnecting'],
                    'exit': ['endConnecting'],
                    'states': {
                        'idle': {
                            'on': {
                                'L_BUTTON_DOWN': {
                                    'target': 'startConnecting',
                                    'actions': ['setLButtonDown']
                                }
                            }
                        },
                        'startConnecting': {
                            'on': {
                                'L_BUTTON_UP': {
                                    'target': 'findingEndPin',
                                    'actions': ['setLButtonUp']
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
                                    'actions': ['setLButtonDown']
                                }
                            }
                        }
                    }
                },
                'resizing': {
                    'initial': 'idle',
                    'entry': ['startResizing'],
                    'exit': ['endResizing'],
                    'states': {
                        'idle': {
                            'on': {
                                'L_BUTTON_DOWN': {
                                    'target': 'resizing',
                                    'actions': ['setLButtonDown']
                                }
                            }
                        },
                        'resizing': {
                            'on': {
                                'MOUSE_MOVE': {
                                    'actions': ['updateResizing']
                                },
                                'L_BUTTON_UP': {
                                    'target': 'idle',
                                    'actions': ['setLButtonUp']
                                }
                            }
                        }
                    }
                }
            },            
        }
		  }    
	}


        ";

}
