using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using XStateNet;

// main
class Program
{
    static void Main()
    {
        var stateMachine = StateMachine.CreateFromScript(jsonScript, _actions).Start();
        Log("==========================================================================");

        var path1 = stateMachine.GetFullTransitionSinglePath("#S.S1", "#S.S2");
        var exitPathCsv1 = path1.exitSinglePath.ToCsvString(stateMachine);
        var entryPathCsv1 = path1.entrySinglePath.ToCsvString(stateMachine);

        var path2 = stateMachine.GetFullTransitionSinglePath("#S.S1.S11.S111.S1111", "#S.S2.S21.S211.S2111");
        var exitPathCsv2 = path2.exitSinglePath.ToCsvString(stateMachine);
        var entryPathCsv2 = path2.entrySinglePath.ToCsvString(stateMachine);

        Debug.Assert(exitPathCsv1 == exitPathCsv2);
        Debug.Assert(entryPathCsv1 == entryPathCsv2);

        Log($"entryPath: {exitPathCsv1}");
        Log($"exitPath: {entryPathCsv1}");

        Log("==========================================================================");

        string? firstExit = path1.exitSinglePath.First();

        if (stateMachine != null)
        {
            stateMachine.TransitUp(firstExit?.ToState(stateMachine) as CompoundState);

            var firstEntry = path1.entrySinglePath.First();

            stateMachine.TransitDown(firstEntry.ToState(stateMachine) as CompoundState, null);
        }

    }

    static void Log(string message) => Console.WriteLine(message);

    static ActionMap? _actions = new()
    {
        ["EnterS1"] = new List<NamedAction> { new NamedAction("EnterS1", (sm) => Log("ENT S1")) },
        ["ExitS1"] = new List<NamedAction> { new NamedAction("ExitS1", (sm) => Log("EXT S1")) },

            ["EnterS11"] = new List<NamedAction> { new NamedAction("EnterS11", (sm) => Log("ENT S11")) },
            ["ExitS11"] = new List<NamedAction> { new NamedAction("ExitS11", (sm) => Log("EXT S11")) },

                ["EnterS111"] = new List<NamedAction> { new NamedAction("EnterS111", (sm) => Log("ENT S111")) },
                ["ExitS111"] = new List<NamedAction> { new NamedAction("ExitS111", (sm) => Log("EXT S111")) },

                    ["EnterS1111"] = new List<NamedAction> { new NamedAction("EnterS1111", (sm) => Log("ENT S1111")) },
                    ["ExitS1111"] = new List<NamedAction> { new NamedAction("ExitS1111", (sm) => Log("EXT S1111")) },

                        ["EnterS11111"] = new List<NamedAction> { new NamedAction("EnterS11111", (sm) => Log("ENT S11111")) },
                        ["ExitS11111"] = new List<NamedAction> { new NamedAction("ExitS11111", (sm) => Log("EXT S11111")) },

                        ["EnterS11112"] = new List<NamedAction> { new NamedAction("EnterS11112", (sm) => Log("ENT S11112")) },
                        ["ExitS11112"] = new List<NamedAction> { new NamedAction("ExitS11112", (sm) => Log("EXT S11112")) },

                    ["EnterS1112"] = new List<NamedAction> { new NamedAction("EnterS1112", (sm) => Log("ENT S1112")) },
                    ["ExitS1112"] = new List<NamedAction> { new NamedAction("ExitS1112", (sm) => Log("EXT S1112")) },

                        ["EnterS11121"] = new List<NamedAction> { new NamedAction("EnterS11121", (sm) => Log("ENT S11121")) },
                        ["ExitS11121"] = new List<NamedAction> { new NamedAction("ExitS11121", (sm) => Log("EXT S11121")) },

                        ["EnterS11122"] = new List<NamedAction> { new NamedAction("EnterS11122", (sm) => Log("ENT S11122")) },
                        ["ExitS11122"] = new List<NamedAction> { new NamedAction("ExitS11122", (sm) => Log("EXT S11122")) },

                ["EnterS112"] = new List<NamedAction> { new NamedAction("EnterS112", (sm) => Log("ENT S112")) },
                ["ExitS112"] = new List<NamedAction> { new NamedAction("ExitS112", (sm) => Log("EXT S112")) },

                    ["EnterS1121"] = new List<NamedAction> { new NamedAction("EnterS1121", (sm) => Log("ENT S1121")) },
                    ["ExitS1121"] = new List<NamedAction> { new NamedAction("ExitS1121", (sm) => Log("EXT S1121")) },

                        ["EnterS11211"] = new List<NamedAction> { new NamedAction("EnterS11211", (sm) => Log("ENT S11211")) },
                        ["ExitS11211"] = new List<NamedAction> { new NamedAction("ExitS11211", (sm) => Log("EXT S11211")) },

                        ["EnterS11212"] = new List<NamedAction> { new NamedAction("EnterS11212", (sm) => Log("ENT S11212")) },
                        ["ExitS11212"] = new List<NamedAction> { new NamedAction("ExitS11212", (sm) => Log("EXT S11212")) },

                    ["EnterS1122"] = new List<NamedAction> { new NamedAction("EnterS1122", (sm) => Log("ENT S1122")) },
                    ["ExitS1122"] = new List<NamedAction> { new NamedAction("ExitS1122", (sm) => Log("EXT S1122")) },

                        ["EnterS11221"] = new List<NamedAction> { new NamedAction("EnterS11221", (sm) => Log("ENT S11221")) },
                        ["ExitS11221"] = new List<NamedAction> { new NamedAction("ExitS11221", (sm) => Log("EXT S11221")) },

                        ["EnterS11222"] = new List<NamedAction> { new NamedAction("EnterS11222", (sm) => Log("ENT S11222")) },
                        ["ExitS11222"] = new List<NamedAction> { new NamedAction("ExitS11222", (sm) => Log("EXT S11222")) },

            ["EnterS12"] = new List<NamedAction> { new NamedAction("EnterS12", (sm) => Log("ENT S12")) },
            ["ExitS12"] = new List<NamedAction> { new NamedAction("ExitS12", (sm) => Log("EXT S12")) },

                ["EnterS121"] = new List<NamedAction> { new NamedAction("EnterS121", (sm) => Log("ENT S121")) },
                ["ExitS121"] = new List<NamedAction> { new NamedAction("ExitS121", (sm) => Log("EXT S121")) },

                    ["EnterS1211"] = new List<NamedAction> { new NamedAction("EnterS1211", (sm) => Log("ENT S1211")) },
                    ["ExitS1211"] = new List<NamedAction> { new NamedAction("ExitS1211", (sm) => Log("EXT S1211")) },

                        ["EnterS12111"] = new List<NamedAction> { new NamedAction("EnterS12111", (sm) => Log("ENT S12111")) },
                        ["ExitS12111"] = new List<NamedAction> { new NamedAction("ExitS12111", (sm) => Log("EXT S12111")) },

                        ["EnterS12112"] = new List<NamedAction> { new NamedAction("EnterS12112", (sm) => Log("ENT S12112")) },
                        ["ExitS12112"] = new List<NamedAction> { new NamedAction("ExitS12112", (sm) => Log("EXT S12112")) },

                    ["EnterS1212"] = new List<NamedAction> { new NamedAction("EnterS1212", (sm) => Log("ENT S1212")) },
                    ["ExitS1212"] = new List<NamedAction> { new NamedAction("ExitS1212", (sm) => Log("EXT S1212")) },

                        ["EnterS12121"] = new List<NamedAction> { new NamedAction("EnterS12121", (sm) => Log("ENT S12121")) },
                        ["ExitS12121"] = new List<NamedAction> { new NamedAction("ExitS12121", (sm) => Log("EXT S12121")) },

                        ["EnterS12122"] = new List<NamedAction> { new NamedAction("EnterS12122", (sm) => Log("ENT S12122")) },
                        ["ExitS12122"] = new List<NamedAction> { new NamedAction("ExitS12122", (sm) => Log("EXT S12122")) },

                ["EnterS122"] = new List<NamedAction> { new NamedAction("EnterS122", (sm) => Log("ENT S122")) },
                ["ExitS122"] = new List<NamedAction> { new NamedAction("ExitS122", (sm) => Log("EXT S122")) },

                    ["EnterS1221"] = new List<NamedAction> { new NamedAction("EnterS1221", (sm) => Log("ENT S1221")) },
                    ["ExitS1221"] = new List<NamedAction> { new NamedAction("ExitS1221", (sm) => Log("EXT S1221")) },

                        ["EnterS12211"] = new List<NamedAction> { new NamedAction("EnterS12211", (sm) => Log("ENT S12211")) },
                        ["ExitS12211"] = new List<NamedAction> { new NamedAction("ExitS12211", (sm) => Log("EXT S12211")) },

                        ["EnterS12212"] = new List<NamedAction> { new NamedAction("EnterS12212", (sm) => Log("ENT S12212")) },
                        ["ExitS12212"] = new List<NamedAction> { new NamedAction("ExitS12212", (sm) => Log("EXT S12212")) },

                    ["EnterS1222"] = new List<NamedAction> { new NamedAction("EnterS1222", (sm) => Log("ENT S1222")) },
                    ["ExitS1222"] = new List<NamedAction> { new NamedAction("ExitS1222", (sm) => Log("EXT S1222")) },

                        ["EnterS12221"] = new List<NamedAction> { new NamedAction("EnterS12221", (sm) => Log("ENT S12221")) },
                        ["ExitS12221"] = new List<NamedAction> { new NamedAction("ExitS12221", (sm) => Log("EXT S12221")) },

                        ["EnterS12222"] = new List<NamedAction> { new NamedAction("EnterS12222", (sm) => Log("ENT S12222")) },
                        ["ExitS12222"] = new List<NamedAction> { new NamedAction("ExitS12222", (sm) => Log("EXT S12222")) },

        ["EnterS2"] = new List<NamedAction> { new NamedAction("EnterS2", (sm) => Log("ENT S2")) },
        ["ExitS2"] = new List<NamedAction> { new NamedAction("ExitS2", (sm) => Log("EXT S2")) },

            ["EnterS21"] = new List<NamedAction> { new NamedAction("EnterS21", (sm) => Log("ENT S21")) },
            ["ExitS21"] = new List<NamedAction> { new NamedAction("ExitS21", (sm) => Log("EXT S21")) },

                ["EnterS211"] = new List<NamedAction> { new NamedAction("EnterS211", (sm) => Log("ENT S211")) },
                ["ExitS211"] = new List<NamedAction> { new NamedAction("ExitS211", (sm) => Log("EXT S211")) },

                    ["EnterS2111"] = new List<NamedAction> { new NamedAction("EnterS2111", (sm) => Log("ENT S2111")) },
                    ["ExitS2111"] = new List<NamedAction> { new NamedAction("ExitS2111", (sm) => Log("EXT S2111")) },

                        ["EnterS21111"] = new List<NamedAction> { new NamedAction("EnterS21111", (sm) => Log("ENT S21111")) },
                        ["ExitS21111"] = new List<NamedAction> { new NamedAction("ExitS21111", (sm) => Log("EXT S21111")) },

                        ["EnterS21112"] = new List<NamedAction> { new NamedAction("EnterS21112", (sm) => Log("ENT S21112")) },
                        ["ExitS21112"] = new List<NamedAction> { new NamedAction("ExitS21112", (sm) => Log("EXT S21112")) },

                    ["EnterS2112"] = new List<NamedAction> { new NamedAction("EnterS2112", (sm) => Log("ENT S2112")) },
                    ["ExitS2112"] = new List<NamedAction> { new NamedAction("ExitS2112", (sm) => Log("EXT S2112")) },

                        ["EnterS21121"] = new List<NamedAction> { new NamedAction("EnterS21121", (sm) => Log("ENT S21121")) },
                        ["ExitS21121"] = new List<NamedAction> { new NamedAction("ExitS21121", (sm) => Log("EXT S21121")) },

                        ["EnterS21122"] = new List<NamedAction> { new NamedAction("EnterS21122", (sm) => Log("ENT S21122")) },
                        ["ExitS21122"] = new List<NamedAction> { new NamedAction("ExitS21122", (sm) => Log("EXT S21122")) },

                ["EnterS212"] = new List<NamedAction> { new NamedAction("EnterS212", (sm) => Log("ENT S212")) },
                ["ExitS212"] = new List<NamedAction> { new NamedAction("ExitS212", (sm) => Log("EXT S212")) },

                    ["EnterS2121"] = new List<NamedAction> { new NamedAction("EnterS2121", (sm) => Log("ENT S2121")) },
                    ["ExitS2121"] = new List<NamedAction> { new NamedAction("ExitS2121", (sm) => Log("EXT S2121")) },

                        ["EnterS21211"] = new List<NamedAction> { new NamedAction("EnterS21211", (sm) => Log("ENT S21211")) },
                        ["ExitS21211"] = new List<NamedAction> { new NamedAction("ExitS21211", (sm) => Log("EXT S21211")) },

                        ["EnterS21212"] = new List<NamedAction> { new NamedAction("EnterS21212", (sm) => Log("ENT S21212")) },
                        ["ExitS21212"] = new List<NamedAction> { new NamedAction("ExitS21212", (sm) => Log("EXT S21212")) },

                    ["EnterS2122"] = new List<NamedAction> { new NamedAction("EnterS2122", (sm) => Log("ENT S2122")) },
                    ["ExitS2122"] = new List<NamedAction> { new NamedAction("ExitS2122", (sm) => Log("EXT S2122")) },

                        ["EnterS21221"] = new List<NamedAction> { new NamedAction("EnterS21221", (sm) => Log("ENT S21221")) },
                        ["ExitS21221"] = new List<NamedAction> { new NamedAction("ExitS21221", (sm) => Log("EXT S21221")) },

                        ["EnterS21222"] = new List<NamedAction> { new NamedAction("EnterS21222", (sm) => Log("ENT S21222")) },
                        ["ExitS21222"] = new List<NamedAction> { new NamedAction("ExitS21222", (sm) => Log("EXT S21222")) },

            ["EnterS22"] = new List<NamedAction> { new NamedAction("EnterS22", (sm) => Log("ENT S22")) },
            ["ExitS22"] = new List<NamedAction> { new NamedAction("ExitS22", (sm) => Log("EXT S22")) },

                ["EnterS221"] = new List<NamedAction> { new NamedAction("EnterS221", (sm) => Log("ENT S221")) },
                ["ExitS221"] = new List<NamedAction> { new NamedAction("ExitS221", (sm) => Log("EXT S221")) },

                    ["EnterS2211"] = new List<NamedAction> { new NamedAction("EnterS2211", (sm) => Log("ENT S2211")) },
                    ["ExitS2211"] = new List<NamedAction> { new NamedAction("ExitS2211", (sm) => Log("EXT S2211")) },

                        ["EnterS22111"] = new List<NamedAction> { new NamedAction("EnterS22111", (sm) => Log("ENT S22111")) },
                        ["ExitS22111"] = new List<NamedAction> { new NamedAction("ExitS22111", (sm) => Log("EXT S22111")) },

                        ["EnterS22112"] = new List<NamedAction> { new NamedAction("EnterS22112", (sm) => Log("ENT S22112")) },
                        ["ExitS22112"] = new List<NamedAction> { new NamedAction("ExitS22112", (sm) => Log("EXT S22112")) },

                    ["EnterS2212"] = new List<NamedAction> { new NamedAction("EnterS2212", (sm) => Log("ENT S2212")) },
                    ["ExitS2212"] = new List<NamedAction> { new NamedAction("ExitS2212", (sm) => Log("EXT S2212")) },

                        ["EnterS22121"] = new List<NamedAction> { new NamedAction("EnterS22121", (sm) => Log("ENT S22121")) },
                        ["ExitS22121"] = new List<NamedAction> { new NamedAction("ExitS22121", (sm) => Log("EXT S22121")) },

                        ["EnterS22122"] = new List<NamedAction> { new NamedAction("EnterS22122", (sm) => Log("ENT S22122")) },
                        ["ExitS22122"] = new List<NamedAction> { new NamedAction("ExitS22122", (sm) => Log("EXT S22122")) },

                ["EnterS222"] = new List<NamedAction> { new NamedAction("EnterS222", (sm) => Log("ENT S222")) },
                ["ExitS222"] = new List<NamedAction> { new NamedAction("ExitS222", (sm) => Log("EXT S222")) },

                    ["EnterS2221"] = new List<NamedAction> { new NamedAction("EnterS2221", (sm) => Log("ENT S2221")) },
                    ["ExitS2221"] = new List<NamedAction> { new NamedAction("ExitS2221", (sm) => Log("EXT S2221")) },

                        ["EnterS22211"] = new List<NamedAction> { new NamedAction("EnterS22211", (sm) => Log("ENT S22211")) },
                        ["ExitS22211"] = new List<NamedAction> { new NamedAction("ExitS22211", (sm) => Log("EXT S22211")) },

                        ["EnterS22212"] = new List<NamedAction> { new NamedAction("EnterS22212", (sm) => Log("ENT S22212")) },
                        ["ExitS22212"] = new List<NamedAction> { new NamedAction("ExitS22212", (sm) => Log("EXT S22212")) },

                    ["EnterS2222"] = new List<NamedAction> { new NamedAction("EnterS2222", (sm) => Log("ENT S2222")) },
                    ["ExitS2222"] = new List<NamedAction> { new NamedAction("ExitS2222", (sm) => Log("EXT S2222")) },

                        ["EnterS22221"] = new List<NamedAction> { new NamedAction("EnterS22221", (sm) => Log("ENT S22221")) },
                        ["ExitS22221"] = new List<NamedAction> { new NamedAction("ExitS22221", (sm) => Log("EXT S22221")) },

                        ["EnterS22222"] = new List<NamedAction> { new NamedAction("EnterS22222", (sm) => Log("ENT S22222")) },
                        ["ExitS22222"] = new List<NamedAction> { new NamedAction("ExitS22222", (sm) => Log("EXT S22222")) },
    };

    static string jsonScript =
        @"{
    id: 'S',
    initial: 'S1',
    states: {        
        S1: {
            type: 'parallel',            
            on: {'S2': 'S2'},
            entry: ['EnterS1'],
            exit: ['ExitS1'],
            states: {
                S11: {
                    initial: 'S111',
                    entry: ['EnterS11'],
                    exit: ['ExitS11'],
                    states: {
                        hist : { type: 'history', history: 'deep' },
                        S111: {
                            type: 'parallel',            
                            on: { 'S112': 'S112'},
                            entry: ['EnterS111'],
                            exit: ['ExitS111'],
                            states: {
                                S1111: {
                                    initial: 'S11111',
                                    entry: ['EnterS1111'],
                                    exit: ['ExitS1111'],
                                    states: {
                                        S11111: {
                                            entry: ['EnterS11111'],
                                            exit: ['ExitS11111'],
                                            on: { 'S11112': 'S11112'}
                                        },
                                        S11112: {
                                            entry: ['EnterS11112'],
                                            exit: ['ExitS11112'],
                                            on: { 'S11111': 'S11111'}
                                        },        
                                    }
                                },
                                S1112: {
                                    initial: 'S11121',
                                    entry: ['EnterS1112'],
                                    exit: ['ExitS1112'],
                                    states: {
                                        S11121: {
                                            entry: ['EnterS11121'],
                                            exit: ['ExitS11121'],
                                            on: { 'S11122': 'S11122'}
                                        },
                                        S11122: {
                                            entry: ['EnterS11122'],
                                            exit: ['ExitS11122'],
                                            on: { 'S11121': 'S11121'}
                                        },        
                                    }
                                },
                            }
                        },
                        S112: {
                            type: 'parallel',            
                            on: { 'S111': 'S111'},
                            entry: ['EnterS112'],
                            exit: ['ExitS112'],
                            states: {
                                S1121: {
                                    initial: 'S11211',
                                    entry: ['EnterS1121'],
                                    exit: ['ExitS1121'],
                                    states: {
                                        S11211: {
                                            entry: ['EnterS11211'],
                                            exit: ['ExitS11211'],
                                            on: { 'S11212': 'S11212'}
                                        },
                                        S11212: {
                                            entry: ['EnterS11212'],
                                            exit: ['ExitS11212'],
                                            on: { 'S11211': 'S11211'}
                                        },        
                                    }
                                },
                                S1122: {
                                    initial: 'S11221',
                                    entry: ['EnterS1122'],
                                    exit: ['ExitS1122'],
                                    states: {
                                        S11221: {
                                            entry: ['EnterS11221'],
                                            exit: ['ExitS11221'],
                                            on: { 'S11222': 'S11222'}
                                        },
                                        S11222: {
                                            entry: ['EnterS11222'],
                                            exit: ['ExitS11222'],
                                            on: { 'S11221': 'S11221'}
                                        },        
                                    }
                                },
                            }
                        },
                    }
                },
                S12: {
                    initial: 'S121',
                    entry: ['EnterS12'],
                    exit: ['ExitS12'],
                    states: {
                        hist : { type: 'history', history: 'deep' },
                        S121: {
                            type: 'parallel',            
                            on: { 'S122': 'S122'},
                            entry: ['EnterS121'],
                            exit: ['ExitS121'],
                            states: {
                                S1211: {
                                    initial: 'S12111',
                                    entry: ['EnterS1211'],
                                    exit: ['ExitS1211'],
                                    states: {
                                        S12111: {
                                            entry: ['EnterS12111'],
                                            exit: ['ExitS12111'],
                                            on: { 'S12112': 'S12112'}
                                        },
                                        S12112: {
                                            entry: ['EnterS12112'],
                                            exit: ['ExitS12112'],
                                            on: { 'S12111': 'S12111'}
                                        },        
                                    }
                                },
                                S1212: {
                                    initial: 'S12121',
                                    entry: ['EnterS1212'],
                                    exit: ['ExitS1212'],
                                    states: {
                                        S12121: {
                                            entry: ['EnterS12121'],
                                            exit: ['ExitS12121'],
                                            on: { 'S12122': 'S12122'}
                                        },
                                        S12122: {
                                            entry: ['EnterS12122'],
                                            exit: ['ExitS12122'],
                                            on: { 'S12121': 'S12121'}
                                        },        
                                    }
                                },
                            }
                        },
                        S122: {
                            type: 'parallel',            
                            on: { 'S121': 'S121'},
                            entry: ['EnterS122'],
                            exit: ['ExitS122'],
                            states: {
                                S1221: {
                                    initial: 'S12211',
                                    entry: ['EnterS1221'],
                                    exit: ['ExitS1221'],
                                    states: {
                                        S12211: {
                                            entry: ['EnterS12211'],
                                            exit: ['ExitS12211'],
                                            on: { 'S12212': 'S12212'}
                                        },
                                        S12212: {
                                            entry: ['EnterS12212'],
                                            exit: ['ExitS12212'],
                                            on: { 'S12211': 'S12211'}
                                        },        
                                    }
                                },
                                S1222: {
                                    initial: 'S12221',
                                    entry: ['EnterS1222'],
                                    exit: ['ExitS1222'],
                                    states: {
                                        S12221: {
                                            entry: ['EnterS12221'],
                                            exit: ['ExitS12221'],
                                            on: { 'S12222': 'S12222'}
                                        },
                                        S12222: {
                                            entry: ['EnterS12222'],
                                            exit: ['ExitS12222'],
                                            on: { 'S12221': 'S12221'}
                                        },        
                                    }
                                },
                            }
                        },
                    }
                },
            }
        },
        
        S2: {
            type: 'parallel',
            on: {'S1': 'S1'},
            entry: ['EnterS2'],
            exit: ['ExitS2'],
            states: {
                S21: {
                    initial: 'S211',
                    entry: ['EnterS21'],
                    exit: ['ExitS21'],
                    states: {
                        hist : { type: 'history', history: 'deep' },
                        S211: {
                            type: 'parallel',
                            on: { 'S212': 'S212'},
                            entry: ['EnterS211'],
                            exit: ['ExitS211'],
                            states: {
                                S2111: {
                                    initial: 'S21111',
                                    entry: ['EnterS2111'],
                                    exit: ['ExitS2111'],
                                    states: {
                                        S21111: {
                                            entry: ['EnterS21111'],
                                            exit: ['ExitS21111'],
                                            on: { 'S21112': 'S21112'}
                                        },
                                        S21112: {
                                            entry: ['EnterS21112'],
                                            exit: ['ExitS21112'],
                                            on: { 'S21111': 'S21111'}
                                        },        
                                    }
                                },
                                S2112: {
                                    initial: 'S21121',
                                    entry: ['EnterS2112'],
                                    exit: ['ExitS2112'],
                                    states: {
                                        S21121: {
                                            entry: ['EnterS21121'],
                                            exit: ['ExitS21121'],
                                            on: { 'S21122': 'S21122'}
                                        },
                                        S21122: {
                                            entry: ['EnterS21122'],
                                            exit: ['ExitS21122'],
                                            on: { 'S21121': 'S21121'}
                                        },        
                                    }
                                },
                            }
                        },
                        S212: {
                            type: 'parallel',
                            on: { 'S211': 'S211'},
                            entry: ['EnterS212'],
                            exit: ['ExitS212'],
                            states: {
                                S2121: {
                                    initial: 'S21211',
                                    entry: ['EnterS2121'],
                                    exit: ['ExitS2121'],
                                    states: {
                                        S21211: {
                                            entry: ['EnterS21211'],
                                            exit: ['ExitS21211'],
                                            on: { 'S21212': 'S21212'}
                                        },
                                        S21212: {
                                            entry: ['EnterS21212'],
                                            exit: ['ExitS21212'],
                                            on: { 'S21211': 'S21211'}
                                        },        
                                    }
                                },
                                S2122: {
                                    initial: 'S21221',
                                    entry: ['EnterS2122'],
                                    exit: ['ExitS2122'],
                                    states: {
                                        S21221: {
                                            entry: ['EnterS21221'],
                                            exit: ['ExitS21221'],
                                            on: { 'S21222': 'S21222'}
                                        },
                                        S21222: {
                                            entry: ['EnterS21222'],
                                            exit: ['ExitS21222'],
                                            on: { 'S21221': 'S21221'}
                                        },        
                                    }
                                },
                            }
                        },
                    }
                },
                S22: {
                    initial: 'S221',
                    entry: ['EnterS22'],
                    exit: ['ExitS22'],
                    states: {
                        hist : { type: 'history', history: 'deep' },
                        S221: {
                            type: 'parallel',
                            on: { 'S222': 'S222'},
                            entry: ['EnterS221'],
                            exit: ['ExitS221'],
                            states: {
                                S2211: {
                                    initial: 'S22111',
                                    entry: ['EnterS2211'],
                                    exit: ['ExitS2211'],
                                    states: {
                                        S22111: {
                                            entry: ['EnterS22111'],
                                            exit: ['ExitS22111'],
                                            on: { 'S22112': 'S22112'}
                                        },
                                        S22112: {
                                            entry: ['EnterS22112'],
                                            exit: ['ExitS22112'],
                                            on: { 'S22111': 'S22111'}
                                        },        
                                    }
                                },
                                S2212: {
                                    initial: 'S22121',
                                    entry: ['EnterS2221'],
                                    exit: ['ExitS2221'],
                                    states: {
                                        S22121: {
                                            entry: ['EnterS22121'],
                                            exit: ['ExitS22121'],
                                            on: { 'S22122': 'S22122'}
                                        },
                                        S22122: {
                                            entry: ['EnterS22122'],
                                            exit: ['ExitS22122'],
                                            on: { 'S22121': 'S22121'}
                                        },        
                                    }
                                },
                            }
                        },
                        S222: {
                            type: 'parallel',
                            on: { 'S221': 'S221'},
                            entry: ['EnterS222'],
                            exit: ['ExitS222'],
                            states: {
                                S2221: {
                                    initial: 'S22211',
                                    entry: ['EnterS2221'],
                                    exit: ['ExitS2221'],
                                    states: {
                                        S22211: {
                                            entry: ['EnterS22211'],
                                            exit: ['ExitS22211'],
                                            on: { 'S22212': 'S22212'}
                                        },
                                        S22212: {
                                            entry: ['EnterS22212'],
                                            exit: ['ExitS22212'],
                                            on: { 'S22211': 'S22211'}
                                        },        
                                    }
                                },
                                S2222: {
                                    initial: 'S22221',
                                    entry: ['EnterS2222'],
                                    exit: ['ExitS2222'],
                                    states: {
                                        S22221: {
                                            entry: ['EnterS22221'],
                                            exit: ['ExitS22221'],
                                            on: { 'S22222': 'S22222'}
                                        },
                                        S22222: {
                                            entry: ['EnterS22222'],
                                            exit: ['ExitS22222'],
                                            on: { 'S22221': 'S22221'}
                                        },        
                                    }
                                },
                            }
                        },
                    }
                },
            }
        },
    }
}";
}
