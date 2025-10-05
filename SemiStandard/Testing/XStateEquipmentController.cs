using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using XStateNet.Semi.Secs;
using XStateNet.Semi.Transport;

namespace XStateNet.Semi.Testing
{
    /// <summary>
    /// SEMI Equipment Controller that uses XState scripts for state management
    /// Implements E30 GEM, E87 Carrier Management, E94 Control Job, etc.
    /// </summary>
    public class XStateEquipmentController : IDisposable
    {
        private readonly ILogger<XStateEquipmentController>? _logger;
        private readonly IPEndPoint _endpoint;
        private HsmsConnection? _connection;
        private readonly ConcurrentDictionary<string, StateMachine> _stateMachines = new();
        private readonly ConcurrentDictionary<string, ActionMap> _actions = new();
        private readonly ConcurrentDictionary<string, GuardMap> _guards = new();
        private readonly ConcurrentDictionary<uint, object> _statusVariables = new();
        private readonly ConcurrentDictionary<uint, object> _equipmentConstants = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed;

        // State machine names
        private const string GEM_MACHINE = "E30GEM";
        private const string CARRIER_MACHINE = "E87Carrier";
        private const string CONTROL_JOB_MACHINE = "E94ControlJob";
        private const string HSMS_SESSION_MACHINE = "E37HSMSSession";
        private const string EQUIPMENT_MACHINE = "SemiEquipment";

        public string ModelName { get; set; } = "XStateNet SEMI Controller";
        public string SoftwareRevision { get; set; } = "1.0.0";

        // Events
        public event EventHandler<SecsMessage>? MessageReceived;
        public event EventHandler<SecsMessage>? MessageSent;
        public event EventHandler<string>? StateChanged;

        public XStateEquipmentController(IPEndPoint endpoint, ILogger<XStateEquipmentController>? logger = null)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _logger = logger;
            InitializeStateMachines();
            InitializeStatusVariables();
        }

        /// <summary>
        /// Initialize all XState machines from JSON scripts
        /// </summary>
        private void InitializeStateMachines()
        {
            var scriptsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XStateScripts");

            // Load E30 GEM State Machine
            LoadStateMachine(GEM_MACHINE, Path.Combine(scriptsPath, "E30GemStates.json"));

            // Load E87 Carrier Management State Machine
            LoadStateMachine(CARRIER_MACHINE, Path.Combine(scriptsPath, "E87CarrierStates.json"));

            // Load E94 Control Job State Machine
            LoadStateMachine(CONTROL_JOB_MACHINE, Path.Combine(scriptsPath, "E94ControlJobStates.json"));

            // Load E37 HSMS Session State Machine
            LoadStateMachine(HSMS_SESSION_MACHINE, Path.Combine(scriptsPath, "E37HSMSSession.json"));

            // Load Overall Equipment State Machine
            LoadStateMachine(EQUIPMENT_MACHINE, Path.Combine(scriptsPath, "SemiEquipmentStates.json"));

            _logger?.LogInformation("Initialized {Count} state machines", _stateMachines.Count);
        }

        /// <summary>
        /// Load a single state machine from JSON
        /// </summary>
        private void LoadStateMachine(string name, string jsonPath)
        {
            try
            {
                if (!File.Exists(jsonPath))
                {
                    _logger?.LogWarning("State machine script not found: {Path}", jsonPath);
                    return;
                }

                var jsonScript = File.ReadAllText(jsonPath);
                var actions = new ActionMap();
                var guards = new GuardMap();

                // Set up actions for this state machine
                SetupActionsForMachine(name, actions);
                SetupGuardsForMachine(name, guards);

                // Add generic handlers for any actions not explicitly defined
                AddGenericActionsFromJson(jsonScript, actions);

                // Suppress obsolete warning - this is a testing/simulation controller
                // For production code, use ExtendedPureStateMachineFactory with EventBusOrchestrator
#pragma warning disable CS0618
                var machine = StateMachineFactory.CreateFromScript(jsonScript, threadSafe: false, true, actions, guards);
#pragma warning restore CS0618
                // Don't set machineId to the name - it will use the ID from the JSON

                // Subscribe to state changes
                machine.OnTransition += (from, to, eventName) =>
                {
                    var currentState = machine.GetActiveStateNames();
                    _logger?.LogInformation("[{Machine}] Transition: {From} -> {To} via {Event}", name,
                        from?.Name ?? "null", to?.Name ?? "null", eventName);
                    _logger?.LogInformation("[{Machine}] Current state string: '{State}'", name, currentState);
                    StateChanged?.Invoke(this, $"{name}: {currentState}");

                    // Handle specific state transitions
                    if (name == GEM_MACHINE && to != null)
                    {
                        _logger?.LogInformation("GEM state transition to: {ToState}", to.Name);
                        if (to.Name.Contains("commFail"))
                        {
                            _logger?.LogInformation("GEM entered commFail state, scheduling retry...");
                            // Retry communication establishment after a delay
                            Task.Run(async () =>
                            {
                                await Task.Delay(2000);
                                _logger?.LogInformation("Retrying GEM communication establishment...");
                                await machine.SendAsync("ENABLE");
                            });
                        }
                    }
                };

                _stateMachines[name] = machine;
                _actions[name] = actions;
                _guards[name] = guards;

                _logger?.LogInformation("Loaded state machine: {Name} from {Path}", name, jsonPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load state machine {Name} from {Path}", name, jsonPath);
            }
        }

        /// <summary>
        /// Add generic actions found in JSON that aren't explicitly defined
        /// </summary>
        private void AddGenericActionsFromJson(string json, ActionMap actions)
        {
            try
            {
                // Simple regex to find action references in the JSON
                var actionPattern = @"""(?:entry|exit|actions)""\s*:\s*""([^""]+)""";
                var matches = System.Text.RegularExpressions.Regex.Matches(json, actionPattern);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var actionName = match.Groups[1].Value;

                        // If action not already defined, add a generic one
                        if (!actions.ContainsKey(actionName))
                        {
                            actions[actionName] = new List<NamedAction>
                            {
                                new NamedAction(actionName, (machine) =>
                                {
                                    _logger?.LogDebug("Executing action: {Action}", actionName);
                                })
                            };
                        }
                    }
                }

                // Also handle action arrays
                var arrayPattern = @"""(?:entry|exit|actions)""\s*:\s*\[(.*?)\]";
                var arrayMatches = System.Text.RegularExpressions.Regex.Matches(json, arrayPattern, System.Text.RegularExpressions.RegexOptions.Singleline);

                foreach (System.Text.RegularExpressions.Match match in arrayMatches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var actionsArray = match.Groups[1].Value;
                        var actionNames = System.Text.RegularExpressions.Regex.Matches(actionsArray, @"""([^""]+)""");

                        foreach (System.Text.RegularExpressions.Match actionMatch in actionNames)
                        {
                            var actionName = actionMatch.Groups[1].Value;

                            if (!actions.ContainsKey(actionName))
                            {
                                actions[actionName] = new List<NamedAction>
                                {
                                    new NamedAction(actionName, (machine) =>
                                    {
                                        _logger?.LogDebug("Executing action: {Action}", actionName);
                                    })
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error parsing actions from JSON");
            }
        }

        /// <summary>
        /// Set up actions for specific state machines
        /// </summary>
        private void SetupActionsForMachine(string machineName, ActionMap actions)
        {
            switch (machineName)
            {
                case GEM_MACHINE:
                    // E30 GEM Actions
                    actions["establishCommunication"] = new List<NamedAction>
                    {
                        new NamedAction("establishCommunication", (machine) =>
                        {
                            _logger?.LogInformation("Establishing GEM communication");
                            _ = EstablishCommunicationAsync();
                        })
                    };

                    actions["goOnline"] = new List<NamedAction>
                    {
                        new NamedAction("goOnline", (machine) =>
                        {
                            _logger?.LogInformation("Going ONLINE");
                            _ = SendHostMessage(SecsMessageLibrary.S6F11(1001, new List<SecsItem>()));
                        })
                    };

                    actions["goOffline"] = new List<NamedAction>
                    {
                        new NamedAction("goOffline", (machine) =>
                        {
                            _logger?.LogInformation("Going OFFLINE");
                            _ = SendHostMessage(SecsMessageLibrary.S6F11(1002, new List<SecsItem>()));
                        })
                    };
                    break;

                case CARRIER_MACHINE:
                    // E87 Carrier Management Actions
                    actions["startMapping"] = new List<NamedAction>
                    {
                        new NamedAction("startMapping", (machine) =>
                        {
                            _logger?.LogInformation("Starting carrier mapping");
                            _ = SendHostMessage(SecsMessageLibrary.S6F11(2101, new List<SecsItem>()));
                        })
                    };

                    actions["loadCarrier"] = new List<NamedAction>
                    {
                        new NamedAction("loadCarrier", (machine) =>
                        {
                            _logger?.LogInformation("Loading carrier");
                            var carrierId = "CARRIER001";
                            _ = SendHostMessage(SecsMessageLibrary.S6F11(2001, new List<SecsItem>
                            {
                                new SecsAscii(carrierId) // Carrier ID
                            }));
                        })
                    };

                    actions["unloadCarrier"] = new List<NamedAction>
                    {
                        new NamedAction("unloadCarrier", (machine) =>
                        {
                            _logger?.LogInformation("Unloading carrier");
                            _ = SendHostMessage(SecsMessageLibrary.S6F11(2002, new List<SecsItem>()));
                        })
                    };
                    break;

                case HSMS_SESSION_MACHINE:
                    // E37 HSMS Session Actions
                    actions["startT7Timer"] = new List<NamedAction>
                    {
                        new NamedAction("startT7Timer", (machine) =>
                        {
                            _logger?.LogInformation("Starting T7 timer");
                        })
                    };

                    actions["stopT7Timer"] = new List<NamedAction>
                    {
                        new NamedAction("stopT7Timer", (machine) =>
                        {
                            _logger?.LogInformation("Stopping T7 timer");
                        })
                    };

                    actions["startT6Timer"] = new List<NamedAction>
                    {
                        new NamedAction("startT6Timer", (machine) =>
                        {
                            _logger?.LogInformation("Starting T6 timer");
                        })
                    };

                    actions["sendSelectReq"] = new List<NamedAction>
                    {
                        new NamedAction("sendSelectReq", (machine) =>
                        {
                            _logger?.LogInformation("Sending Select.req");
                        })
                    };

                    actions["sendSelectRsp"] = new List<NamedAction>
                    {
                        new NamedAction("sendSelectRsp", (machine) =>
                        {
                            _logger?.LogInformation("Sending Select.rsp");
                        })
                    };

                    actions["attemptConnection"] = new List<NamedAction>
                    {
                        new NamedAction("attemptConnection", (machine) =>
                        {
                            _logger?.LogInformation("Attempting connection");
                        })
                    };

                    actions["disconnect"] = new List<NamedAction>
                    {
                        new NamedAction("disconnect", (machine) =>
                        {
                            _logger?.LogInformation("Disconnecting");
                        })
                    };

                    actions["recordSelectedEntity"] = new List<NamedAction>
                    {
                        new NamedAction("recordSelectedEntity", (machine) =>
                        {
                            _logger?.LogInformation("Recording selected entity");
                        })
                    };

                    actions["sendSeparateRsp"] = new List<NamedAction>
                    {
                        new NamedAction("sendSeparateRsp", (machine) =>
                        {
                            _logger?.LogInformation("Sending Separate.rsp");
                        })
                    };

                    actions["startLinktest"] = new List<NamedAction>
                    {
                        new NamedAction("startLinktest", (machine) =>
                        {
                            _logger?.LogInformation("Starting linktest");
                        })
                    };

                    actions["stopLinktest"] = new List<NamedAction>
                    {
                        new NamedAction("stopLinktest", (machine) =>
                        {
                            _logger?.LogInformation("Stopping linktest");
                        })
                    };

                    // Additional HSMS actions
                    actions["notifySelected"] = new List<NamedAction>
                    {
                        new NamedAction("notifySelected", (machine) =>
                        {
                            _logger?.LogInformation("Notifying selected");
                        })
                    };

                    actions["notifyDeselected"] = new List<NamedAction>
                    {
                        new NamedAction("notifyDeselected", (machine) =>
                        {
                            _logger?.LogInformation("Notifying deselected");
                        })
                    };
                    break;

                case EQUIPMENT_MACHINE:
                    // Equipment State Machine Actions
                    actions["offlineEntry"] = new List<NamedAction>
                    {
                        new NamedAction("offlineEntry", (machine) =>
                        {
                            _logger?.LogInformation("Entering OFFLINE state");
                            _statusVariables[1] = 4; // OFFLINE
                        })
                    };

                    actions["offlineExit"] = new List<NamedAction>
                    {
                        new NamedAction("offlineExit", (machine) =>
                        {
                            _logger?.LogInformation("Exiting OFFLINE state");
                        })
                    };

                    actions["localEntry"] = new List<NamedAction>
                    {
                        new NamedAction("localEntry", (machine) =>
                        {
                            _logger?.LogInformation("Entering LOCAL state");
                            _statusVariables[1] = 1; // ONLINE_LOCAL
                        })
                    };

                    actions["localExit"] = new List<NamedAction>
                    {
                        new NamedAction("localExit", (machine) =>
                        {
                            _logger?.LogInformation("Exiting LOCAL state");
                        })
                    };

                    actions["remoteEntry"] = new List<NamedAction>
                    {
                        new NamedAction("remoteEntry", (machine) =>
                        {
                            _logger?.LogInformation("Entering REMOTE state");
                            _statusVariables[1] = 2; // ONLINE_REMOTE
                        })
                    };

                    actions["remoteExit"] = new List<NamedAction>
                    {
                        new NamedAction("remoteExit", (machine) =>
                        {
                            _logger?.LogInformation("Exiting REMOTE state");
                        })
                    };

                    actions["processingEntry"] = new List<NamedAction>
                    {
                        new NamedAction("processingEntry", (machine) =>
                        {
                            _logger?.LogInformation("Starting processing");
                            _statusVariables[2] = 1; // PROCESSING
                        })
                    };

                    actions["processingExit"] = new List<NamedAction>
                    {
                        new NamedAction("processingExit", (machine) =>
                        {
                            _logger?.LogInformation("Stopping processing");
                            _statusVariables[2] = 0; // IDLE
                        })
                    };
                    break;

                case CONTROL_JOB_MACHINE:
                    // E94 Control Job Actions
                    actions["startJob"] = new List<NamedAction>
                    {
                        new NamedAction("startJob", (machine) =>
                        {
                            _logger?.LogInformation("Starting control job");
                            var jobId = "JOB001";
                            _ = SendHostMessage(SecsMessageLibrary.S6F11(3001, new List<SecsItem>
                            {
                                new SecsAscii(jobId) // Control Job ID
                            }));
                        })
                    };

                    actions["completeJob"] = new List<NamedAction>
                    {
                        new NamedAction("completeJob", (machine) =>
                        {
                            _logger?.LogInformation("Completing control job");
                            _ = SendHostMessage(SecsMessageLibrary.S6F11(3002, new List<SecsItem>()));
                        })
                    };
                    break;
            }
        }

        /// <summary>
        /// Set up guards for specific state machines
        /// </summary>
        private void SetupGuardsForMachine(string machineName, GuardMap guards)
        {
            switch (machineName)
            {
                case GEM_MACHINE:
                    guards["isOnlineLocal"] = new NamedGuard("isOnlineLocal", (machine) =>
                    {
                        var controlState = _statusVariables.GetValueOrDefault((uint)1, 1);
                        return controlState.Equals(1); // ONLINE_LOCAL
                    });

                    guards["isOnlineRemote"] = new NamedGuard("isOnlineRemote", (machine) =>
                    {
                        var controlState = _statusVariables.GetValueOrDefault((uint)1, 1);
                        return controlState.Equals(2); // ONLINE_REMOTE
                    });
                    break;

                case CARRIER_MACHINE:
                    guards["isCarrierPresent"] = new NamedGuard("isCarrierPresent", (machine) =>
                    {
                        // Check if carrier is physically present
                        return true; // Simulated
                    });

                    guards["isCarrierIDValid"] = new NamedGuard("isCarrierIDValid", (machine) =>
                    {
                        // Validate carrier ID format
                        return true; // Simulated
                    });
                    break;

                case HSMS_SESSION_MACHINE:
                    guards["isPassiveMode"] = new NamedGuard("isPassiveMode", (machine) =>
                    {
                        return true; // Equipment is in passive mode
                    });

                    guards["isActiveMode"] = new NamedGuard("isActiveMode", (machine) =>
                    {
                        return false; // Equipment is not in active mode
                    });
                    break;
            }
        }

        /// <summary>
        /// Initialize SEMI status variables and equipment constants
        /// </summary>
        private void InitializeStatusVariables()
        {
            // Common Status Variables (SEMI E5)
            _statusVariables[1] = 1;  // Control State (1=ONLINE_LOCAL)
            _statusVariables[2] = 0;  // Process State (0=IDLE)
            _statusVariables[3] = 0;  // Alarm State (0=NO_ALARMS)

            // Equipment Constants (SEMI E5)
            _equipmentConstants[1] = ModelName;           // Equipment Model
            _equipmentConstants[2] = SoftwareRevision;    // Software Revision
            _equipmentConstants[3] = 300;                 // Max concurrent jobs

            _logger?.LogInformation("Initialized {SVCount} status variables and {ECCount} equipment constants",
                _statusVariables.Count, _equipmentConstants.Count);
        }

        /// <summary>
        /// Start the equipment controller
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(XStateEquipmentController));

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start connection
            _connection = new HsmsConnection(_endpoint, HsmsConnection.HsmsConnectionMode.Passive, null);
            _connection.MessageReceived += OnHsmsMessageReceived;
            _connection.StateChanged += OnConnectionStateChanged;
            _connection.ErrorOccurred += OnConnectionError;

            await _connection.ConnectAsync(_cancellationTokenSource.Token);

            // Start all state machines
            foreach (var (name, machine) in _stateMachines)
            {
                await machine.StartAsync();
                _logger?.LogInformation("Started state machine: {Name}", name);
            }

            // Send initial state to GEM machine
            if (_stateMachines.TryGetValue(GEM_MACHINE, out var gemMachine))
            {
                await gemMachine.SendAsync("ENABLE");
            }

            _logger?.LogInformation("XState Equipment Controller started on {Endpoint}", _endpoint);
        }

        /// <summary>
        /// Handle incoming HSMS messages
        /// </summary>
        private async void OnHsmsMessageReceived(object? sender, HsmsMessage hsmsMessage)
        {
            try
            {
                // Handle control messages
                if (hsmsMessage.MessageType != HsmsMessageType.DataMessage)
                {
                    await HandleControlMessage(hsmsMessage);
                    return;
                }

                // Decode SECS message
                var secsMessage = DecodeSecsMessage(hsmsMessage);
                if (secsMessage == null) return;

                MessageReceived?.Invoke(this, secsMessage);

                // Route message to appropriate state machine
                await RouteMessageToStateMachine(secsMessage);

                // Generate response
                var response = await GenerateResponse(secsMessage);
                if (response != null)
                {
                    await SendHostMessage(response);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling HSMS message");
            }
        }

        /// <summary>
        /// Route SECS message to appropriate state machine
        /// </summary>
        private async Task RouteMessageToStateMachine(SecsMessage message)
        {
            var sxfy = $"S{message.Stream}F{message.Function}";

            switch (sxfy)
            {
                case "S1F13": // Establish Communication Request
                    if (_stateMachines.TryGetValue(GEM_MACHINE, out var gemMachine))
                    {
                        _logger?.LogInformation("Routing S1F13 to GEM state machine");
                        await gemMachine.SendAsync("RECEIVE_S1F13");

                        // Also send confirmation that we should transition
                        Task.Run(async () =>
                        {
                            await Task.Delay(100);
                            await gemMachine.SendAsync("SEND_S1F14");
                        });
                    }
                    break;

                case "S1F17": // Request Online
                    if (_stateMachines.TryGetValue(GEM_MACHINE, out var gem))
                    {
                        await gem.SendAsync("GO_ONLINE");
                    }
                    break;

                case "S2F41": // Host Command
                    await HandleHostCommand(message);
                    break;

                case "S3F17": // Carrier Action Request
                    if (_stateMachines.TryGetValue(CARRIER_MACHINE, out var carrier))
                    {
                        await carrier.SendAsync("CARRIER_ACTION");
                    }
                    break;

                case "S14F1": // Control Job Request
                    if (_stateMachines.TryGetValue(CONTROL_JOB_MACHINE, out var controlJob))
                    {
                        await controlJob.SendAsync("CREATE_JOB");
                    }
                    break;
            }
        }

        /// <summary>
        /// Handle host commands (S2F41)
        /// </summary>
        private async Task HandleHostCommand(SecsMessage message)
        {
            // Extract command from message
            var items = message.Data as SecsList;
            if (items?.Items.Count > 0)
            {
                var command = (items.Items[0] as SecsAscii)?.Value;
                _logger?.LogInformation("Received host command: {Command}", command);

                switch (command?.ToUpper())
                {
                    case "START":
                        if (_stateMachines.TryGetValue(EQUIPMENT_MACHINE, out var equipment))
                        {
                            await equipment.SendAsync("START_PROCESSING");
                        }
                        break;

                    case "STOP":
                        if (_stateMachines.TryGetValue(EQUIPMENT_MACHINE, out var equip))
                        {
                            await equip.SendAsync("STOP_PROCESSING");
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Generate response for SECS message
        /// </summary>
        private async Task<SecsMessage?> GenerateResponse(SecsMessage message)
        {
            var sxfy = $"S{message.Stream}F{message.Function}";

            return sxfy switch
            {
                "S1F1" => SecsMessageLibrary.S1F2(ModelName, SoftwareRevision),
                "S1F3" => SecsMessageLibrary.S1F4(new List<SecsItem>(_statusVariables.Values.Select(v => new SecsU4(Convert.ToUInt32(v))))),
                "S1F13" => SecsMessageLibrary.S1F14(0, ModelName, SoftwareRevision),
                "S1F17" => new SecsMessage(1, 18, false) { SystemBytes = message.SystemBytes, Data = new SecsU1(0) },
                "S2F41" => SecsMessageLibrary.S2F42(0, null),
                _ => null
            };
        }

        /// <summary>
        /// Handle HSMS control messages
        /// </summary>
        private async Task HandleControlMessage(HsmsMessage message)
        {
            HsmsMessage? response = null;

            switch (message.MessageType)
            {
                case HsmsMessageType.SelectReq:
                    response = new HsmsMessage
                    {
                        MessageType = HsmsMessageType.SelectRsp,
                        SystemBytes = message.SystemBytes
                    };

                    // Update HSMS session state machine
                    if (_stateMachines.TryGetValue(HSMS_SESSION_MACHINE, out var hsmsSession))
                    {
                        await hsmsSession.SendAsync("SELECT");
                    }
                    break;

                case HsmsMessageType.DeselectReq:
                    response = new HsmsMessage
                    {
                        MessageType = HsmsMessageType.DeselectRsp,
                        SystemBytes = message.SystemBytes
                    };

                    if (_stateMachines.TryGetValue(HSMS_SESSION_MACHINE, out var hsms))
                    {
                        await hsms.SendAsync("DESELECT");
                    }
                    break;

                case HsmsMessageType.LinktestReq:
                    response = new HsmsMessage
                    {
                        MessageType = HsmsMessageType.LinktestRsp,
                        SystemBytes = message.SystemBytes
                    };
                    break;
            }

            if (response != null && _connection != null)
            {
                await _connection.SendMessageAsync(response, CancellationToken.None);
            }
        }

        /// <summary>
        /// Establish communication with host
        /// </summary>
        private async Task EstablishCommunicationAsync()
        {
            // This is called by the GEM state machine action
            var s1f13 = SecsMessageLibrary.S1F13();
            await SendHostMessage(s1f13);
        }

        /// <summary>
        /// Send SECS message to host
        /// </summary>
        private async Task SendHostMessage(SecsMessage message)
        {
            if (_connection == null || !_connection.IsConnected)
            {
                _logger?.LogWarning("Cannot send message - not connected");
                return;
            }

            var hsmsMessage = EncodeSecsMessage(message);
            await _connection.SendMessageAsync(hsmsMessage, CancellationToken.None);

            MessageSent?.Invoke(this, message);
            _logger?.LogDebug("Sent {SxFy} to host", message.SxFy);
        }

        /// <summary>
        /// Encode SECS message to HSMS
        /// </summary>
        private HsmsMessage EncodeSecsMessage(SecsMessage message)
        {
            return new HsmsMessage
            {
                Stream = (byte)message.Stream,
                Function = (byte)message.Function,
                MessageType = HsmsMessageType.DataMessage,
                SystemBytes = message.SystemBytes,
                Data = message.Encode()
            };
        }

        /// <summary>
        /// Decode HSMS message to SECS
        /// </summary>
        private SecsMessage? DecodeSecsMessage(HsmsMessage hsmsMessage)
        {
            try
            {
                var message = new SecsMessage(hsmsMessage.Stream, hsmsMessage.Function, false)
                {
                    SystemBytes = hsmsMessage.SystemBytes
                };

                if (hsmsMessage.Data != null && hsmsMessage.Data.Length > 0)
                {
                    using var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(hsmsMessage.Data));
                    message.Data = SecsItem.Decode(reader);
                }

                return message;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to decode SECS message");
                return null;
            }
        }

        private void OnConnectionStateChanged(object? sender, HsmsConnection.HsmsConnectionState state)
        {
            _logger?.LogInformation("HSMS connection state changed to: {State}", state);

            // Update HSMS session state machine
            if (_stateMachines.TryGetValue(HSMS_SESSION_MACHINE, out var hsmsSession))
            {
                switch (state)
                {
                    case HsmsConnection.HsmsConnectionState.Connected:
                        _ = Task.Run(async () => await hsmsSession.SendAsync("CONNECT"));
                        break;
                    case HsmsConnection.HsmsConnectionState.NotConnected:
                        _ = Task.Run(async () => await hsmsSession.SendAsync("DISCONNECT"));
                        break;
                }
            }
        }

        private void OnConnectionError(object? sender, Exception ex)
        {
            _logger?.LogError(ex, "HSMS connection error");
        }

        /// <summary>
        /// Get current state of a state machine
        /// </summary>
        public string? GetMachineState(string machineName)
        {
            return _stateMachines.TryGetValue(machineName, out var machine)
                ? machine.GetActiveStateNames()
                : null;
        }

        /// <summary>
        /// Get all machine states
        /// </summary>
        public ConcurrentDictionary<string, string> GetAllMachineStates()
        {
            var states = new ConcurrentDictionary<string, string>();
            foreach (var (name, machine) in _stateMachines)
            {
                states[name] = machine.GetActiveStateNames();
            }
            return states;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cancellationTokenSource?.Cancel();

            foreach (var machine in _stateMachines.Values)
            {
                machine.Stop();
            }

            _connection?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
