using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using CMPSimulator.Controllers;
using CMPSimulator.Models;
using CMPSimulator.Controls;
using CMPSimulator.Helpers;

namespace CMPSimulator;

public partial class MainWindow : Window
{
    private readonly IForwardPriorityController _controller;
    private readonly Storyboard _waferAnimationStoryboard;
    private double _currentZoom = 1.0;
    private const double ZoomMin = 0.5;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;
    private ScaleTransform _zoomTransform = new ScaleTransform(1.0, 1.0);

    private bool _isPanning = false;
    private Point _panStart;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

    private DateTime _simulationStartTime = DateTime.Now;
    private string? _selectedStationName = null;
    private System.Windows.Threading.DispatcherTimer? _remainingTimeTimer;
    private SimulatorSettings _settings;

    // Station control references
    private LoadPortControl LoadPortControl;
    private RobotStationControl R1Control;
    private ProcessStationControl PolisherControl;
    private RobotStationControl R2Control;
    private ProcessStationControl CleanerControl;
    private RobotStationControl R3Control;
    private RobotStationControl BufferControl;

    public MainWindow()
    {
        InitializeComponent();

        // Load settings from file
        _settings = SettingsManager.LoadSettings();

        // Switch between implementations:
        // _controller = new ForwardPriorityController();  // Non-XStateNet version
        _controller = new OrchestratedForwardPriorityController();  // XStateNet Orchestrated version
        _waferAnimationStoryboard = new Storyboard();

        // Set data context for wafer binding
        DataContext = _controller;

        // Subscribe to events
        _controller.LogMessage += Controller_LogMessage;
        _controller.StationStatusChanged += Controller_StationStatusChanged;

        // Subscribe to simulation completion event (only for orchestrated controller)
        if (_controller is OrchestratedForwardPriorityController orchestratedController)
        {
            // Subscribe to carrier removal event
            orchestratedController.RemoveOldCarrierFromStateTree += Controller_RemoveOldCarrierFromStateTree;
        }

        // Apply zoom transform to entire CMP System (grid zooms, but dot size stays fixed via inverse transform)
        CMPSystem.LayoutTransform = _zoomTransform;

        // Create and add station controls to CMP System
        InitializeStationControls();

        // Initialize State Tree
        InitializeStateTree();

        Log("═══════════════════════════════════════════════════════════");
        Log("CMP Tool Simulator - Forward Priority Scheduler");
        Log("Priority: P1(C→B) > P2(P→C) > P3(L→P) > P4(B→L)");
        Log("Press Start to begin simulation");
        Log("═══════════════════════════════════════════════════════════");

        // Setup canvas events (both panning and selection)
        CMPSystem.Canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
        CMPSystem.Canvas.MouseMove += SimulationCanvas_MouseMove;
        CMPSystem.Canvas.MouseLeftButtonUp += SimulationCanvas_MouseLeftButtonUp;
        CMPSystem.Canvas.MouseLeave += SimulationCanvas_MouseLeave;

        // Setup station click handlers (will check Edit Mode inside)
        LoadPortControl.MouseLeftButtonDown += (s, e) => { if (!_isPanning && !StationEditor.IsEditMode) { SelectStation("LoadPort"); e.Handled = true; } };
        R1Control.MouseLeftButtonDown += (s, e) => { if (!_isPanning && !StationEditor.IsEditMode) { SelectStation("R1"); e.Handled = true; } };
        PolisherControl.MouseLeftButtonDown += (s, e) => { if (!_isPanning && !StationEditor.IsEditMode) { SelectStation("Polisher"); e.Handled = true; } };
        R2Control.MouseLeftButtonDown += (s, e) => { if (!_isPanning && !StationEditor.IsEditMode) { SelectStation("R2"); e.Handled = true; } };
        CleanerControl.MouseLeftButtonDown += (s, e) => { if (!_isPanning && !StationEditor.IsEditMode) { SelectStation("Cleaner"); e.Handled = true; } };
        R3Control.MouseLeftButtonDown += (s, e) => { if (!_isPanning && !StationEditor.IsEditMode) { SelectStation("R3"); e.Handled = true; } };
        BufferControl.MouseLeftButtonDown += (s, e) => { if (!_isPanning && !StationEditor.IsEditMode) { SelectStation("Buffer"); e.Handled = true; } };

        // Load timing settings into UI TextBoxes
        LoadTimingSettingsToUI();

        Log($"Settings loaded from: {SettingsManager.GetSettingsFilePath()}");
    }

    private void LoadTimingSettingsToUI()
    {
        // Load timing settings from _settings into UI TextBoxes
        R1TransferTextBox.Text = _settings.R1TransferTime.ToString();
        PolisherTextBox.Text = _settings.PolisherTime.ToString();
        R2TransferTextBox.Text = _settings.R2TransferTime.ToString();
        CleanerTextBox.Text = _settings.CleanerTime.ToString();
        R3TransferTextBox.Text = _settings.R3TransferTime.ToString();
        BufferHoldTextBox.Text = _settings.BufferHoldTime.ToString();
        LoadPortReturnTextBox.Text = _settings.LoadPortReturnTime.ToString();
    }

    private void InitializeStationControls()
    {
        var converter = new BrushConverter();

        // Create LoadPort (using geometry from settings)
        LoadPortControl = new LoadPortControl
        {
            StationName = "LoadPort",
            StatusText = "0/0",
            BackgroundColor = Brushes.White,
            Width = _settings.LoadPort.Width,
            Height = _settings.LoadPort.Height
        };
        CMPSystem.AddStation(LoadPortControl, _settings.LoadPort.Left, _settings.LoadPort.Top);

        // Create R1 (using geometry from settings)
        R1Control = new RobotStationControl
        {
            StationName = "R1",
            StatusText = "Empty",
            BackgroundColor = (Brush)converter.ConvertFromString("#D0E8FF")!,
            Width = _settings.R1.Width,
            Height = _settings.R1.Height
        };
        CMPSystem.AddStation(R1Control, _settings.R1.Left, _settings.R1.Top);

        // Create Polisher (using geometry from settings)
        PolisherControl = new ProcessStationControl
        {
            StationName = "Polisher",
            StatusText = "Idle",
            BackgroundColor = (Brush)converter.ConvertFromString("#FFFACD")!,
            Width = _settings.Polisher.Width,
            Height = _settings.Polisher.Height
        };
        CMPSystem.AddStation(PolisherControl, _settings.Polisher.Left, _settings.Polisher.Top);

        // Create R2 (using geometry from settings)
        R2Control = new RobotStationControl
        {
            StationName = "R2",
            StatusText = "Empty",
            BackgroundColor = (Brush)converter.ConvertFromString("#D0E8FF")!,
            Width = _settings.R2.Width,
            Height = _settings.R2.Height
        };
        CMPSystem.AddStation(R2Control, _settings.R2.Left, _settings.R2.Top);

        // Create Cleaner (using geometry from settings)
        CleanerControl = new ProcessStationControl
        {
            StationName = "Cleaner",
            StatusText = "Idle",
            BackgroundColor = (Brush)converter.ConvertFromString("#E0FFFF")!,
            Width = _settings.Cleaner.Width,
            Height = _settings.Cleaner.Height
        };
        CMPSystem.AddStation(CleanerControl, _settings.Cleaner.Left, _settings.Cleaner.Top);

        // Create R3 (using geometry from settings)
        R3Control = new RobotStationControl
        {
            StationName = "R3",
            StatusText = "Empty",
            BackgroundColor = (Brush)converter.ConvertFromString("#FFD0E8")!,
            Width = _settings.R3.Width,
            Height = _settings.R3.Height
        };
        CMPSystem.AddStation(R3Control, _settings.R3.Left, _settings.R3.Top);

        // Create Buffer (using geometry from settings)
        BufferControl = new RobotStationControl
        {
            StationName = "Buffer",
            StatusText = "Empty",
            BackgroundColor = (Brush)converter.ConvertFromString("#FFE0B2")!,
            Width = _settings.Buffer.Width,
            Height = _settings.Buffer.Height
        };
        CMPSystem.AddStation(BufferControl, _settings.Buffer.Left, _settings.Buffer.Top);

        // Update total wafer count (will be updated with wafers during simulation)
        CMPSystem.UpdateTotalWaferCount();

        // Setup station connections
        // LoadPort → Forward → R1
        LoadPortControl.NextForward.Add(R1Control);

        // R1 → Forward → Polisher, Backward → LoadPort
        R1Control.NextForward.Add(PolisherControl);
        R1Control.NextBackward.Add(LoadPortControl);

        // Polisher → Forward → R2, Backward → R1
        PolisherControl.NextForward.Add(R2Control);
        PolisherControl.NextBackward.Add(R1Control);

        // R2 → Forward → Cleaner, Backward → Polisher
        R2Control.NextForward.Add(CleanerControl);
        R2Control.NextBackward.Add(PolisherControl);

        // Cleaner → Forward → R3, Backward → R2
        CleanerControl.NextForward.Add(R3Control);
        CleanerControl.NextBackward.Add(R2Control);

        // R3 → Forward → Buffer, Backward → Cleaner
        R3Control.NextForward.Add(BufferControl);
        R3Control.NextBackward.Add(CleanerControl);

        // Buffer → Backward → R1 (to return to LoadPort via R1)
        BufferControl.NextBackward.Add(R1Control);
    }

    private void InitializeStateTree()
    {
        // Clear existing tree
        StateTreeControl.RootNodes.Clear();

        // Create root node for CMP System
        var rootNode = new StateTreeNode("CMP_SYSTEM", "CMP System", "System", "Running");
        rootNode.IsExpanded = true;

        // Add LoadPort hierarchy
        var loadPortNode = new StateTreeNode("LoadPort", "LoadPort", "LoadPort", "Empty");
        loadPortNode.IsExpanded = true;

        // Add E84 LoadPort state children
        loadPortNode.Children.Add(new StateTreeNode("LoadPort_empty", "empty", "State") { IsActive = true });
        loadPortNode.Children.Add(new StateTreeNode("LoadPort_carrierArrived", "carrierArrived", "State"));
        loadPortNode.Children.Add(new StateTreeNode("LoadPort_docked", "docked", "State"));
        loadPortNode.Children.Add(new StateTreeNode("LoadPort_processing", "processing", "State"));
        loadPortNode.Children.Add(new StateTreeNode("LoadPort_unloading", "unloading", "State"));

        rootNode.Children.Add(loadPortNode);

        // Note: Carrier nodes will be added dynamically as carriers are created
        // Initial carrier (CARRIER_001) will be added when simulation starts

        // Add Robot nodes with all possible states
        var r1Node = new StateTreeNode("R1", "Robot R1", "Robot", "idle");
        r1Node.Children.Add(new StateTreeNode("R1_idle", "idle", "State") { IsActive = true });
        r1Node.Children.Add(new StateTreeNode("R1_pickingUp", "pickingUp", "State"));
        r1Node.Children.Add(new StateTreeNode("R1_holding", "holding", "State"));
        r1Node.Children.Add(new StateTreeNode("R1_placingDown", "placingDown", "State"));
        r1Node.Children.Add(new StateTreeNode("R1_returning", "returning", "State"));
        rootNode.Children.Add(r1Node);

        var r2Node = new StateTreeNode("R2", "Robot R2", "Robot", "idle");
        r2Node.Children.Add(new StateTreeNode("R2_idle", "idle", "State") { IsActive = true });
        r2Node.Children.Add(new StateTreeNode("R2_pickingUp", "pickingUp", "State"));
        r2Node.Children.Add(new StateTreeNode("R2_holding", "holding", "State"));
        r2Node.Children.Add(new StateTreeNode("R2_placingDown", "placingDown", "State"));
        r2Node.Children.Add(new StateTreeNode("R2_returning", "returning", "State"));
        rootNode.Children.Add(r2Node);

        var r3Node = new StateTreeNode("R3", "Robot R3", "Robot", "idle");
        r3Node.Children.Add(new StateTreeNode("R3_idle", "idle", "State") { IsActive = true });
        r3Node.Children.Add(new StateTreeNode("R3_pickingUp", "pickingUp", "State"));
        r3Node.Children.Add(new StateTreeNode("R3_holding", "holding", "State"));
        r3Node.Children.Add(new StateTreeNode("R3_placingDown", "placingDown", "State"));
        r3Node.Children.Add(new StateTreeNode("R3_returning", "returning", "State"));
        rootNode.Children.Add(r3Node);

        // Add Process Station nodes with all possible states
        var polisherNode = new StateTreeNode("Polisher", "Polisher", "Polisher", "empty");
        polisherNode.Children.Add(new StateTreeNode("Polisher_empty", "empty", "State") { IsActive = true });

        // Processing is a hierarchical state with sub-states
        var polisherProcessingNode = new StateTreeNode("Polisher_processing", "processing", "State");
        polisherProcessingNode.IsExpanded = true; // Expand to show sub-states
        polisherProcessingNode.Children.Add(new StateTreeNode("Polisher_Loading", "Loading", "State"));
        polisherProcessingNode.Children.Add(new StateTreeNode("Polisher_Chucking", "Chucking", "State"));
        polisherProcessingNode.Children.Add(new StateTreeNode("Polisher_Polishing", "Polishing", "State"));
        polisherProcessingNode.Children.Add(new StateTreeNode("Polisher_Dechucking", "Dechucking", "State"));
        polisherProcessingNode.Children.Add(new StateTreeNode("Polisher_Unloading", "Unloading", "State"));
        polisherNode.Children.Add(polisherProcessingNode);

        polisherNode.Children.Add(new StateTreeNode("Polisher_done", "done", "State"));
        rootNode.Children.Add(polisherNode);

        var cleanerNode = new StateTreeNode("Cleaner", "Cleaner", "Cleaner", "empty");
        cleanerNode.Children.Add(new StateTreeNode("Cleaner_empty", "empty", "State") { IsActive = true });
        cleanerNode.Children.Add(new StateTreeNode("Cleaner_processing", "processing", "State"));
        cleanerNode.Children.Add(new StateTreeNode("Cleaner_done", "done", "State"));
        rootNode.Children.Add(cleanerNode);

        // Add Buffer node with all possible states
        var bufferNode = new StateTreeNode("Buffer", "Buffer", "Buffer", "empty");
        bufferNode.Children.Add(new StateTreeNode("Buffer_empty", "empty", "State") { IsActive = true });
        bufferNode.Children.Add(new StateTreeNode("Buffer_occupied", "occupied", "State"));
        rootNode.Children.Add(bufferNode);

        // Add root node to tree
        StateTreeControl.RootNodes.Add(rootNode);

        Log("✓ State tree initialized with hierarchical component structure");
    }

    /// <summary>
    /// Dynamically add a carrier node to the state tree
    /// </summary>
    private void AddCarrierToStateTree(string carrierId)
    {
        Console.WriteLine($"[DEBUG AddCarrierToStateTree] Called for {carrierId}");

        // Find the LoadPort node
        var rootNode = StateTreeControl.RootNodes.FirstOrDefault(n => n.Id == "CMP_SYSTEM");
        if (rootNode == null)
        {
            Console.WriteLine($"[DEBUG AddCarrierToStateTree] ERROR: Root node CMP_SYSTEM not found!");
            return;
        }

        var loadPortNode = rootNode.Children.FirstOrDefault(n => n.Id == "LoadPort");
        if (loadPortNode == null)
        {
            Console.WriteLine($"[DEBUG AddCarrierToStateTree] ERROR: LoadPort node not found!");
            return;
        }

        // Check if carrier already exists
        if (loadPortNode.Children.Any(n => n.Id == carrierId))
        {
            Console.WriteLine($"[DEBUG AddCarrierToStateTree] Carrier {carrierId} already exists, skipping");
            Log($"⚠ Carrier node {carrierId} already exists in state tree");
            return;
        }

        // Extract carrier number from ID (e.g., "CARRIER_001" → "001")
        string carrierNumber = carrierId.Replace("CARRIER_", "");
        int carrierNum = int.Parse(carrierNumber);

        // Create carrier node
        var carrierNode = new StateTreeNode(carrierId, $"Carrier {carrierNum:D3}", "Carrier", "NotPresent");
        carrierNode.IsExpanded = true;

        // Add E87 carrier state children
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_NotPresent", "NotPresent", "State") { IsActive = true });
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_WaitingForHost", "WaitingForHost", "State"));
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_Mapping", "Mapping", "State"));
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_MappingVerification", "MappingVerification", "State"));
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_ReadyToAccess", "ReadyToAccess", "State"));

        // InAccess is a hierarchical state containing all substrates (wafers)
        var inAccessNode = new StateTreeNode($"{carrierId}_InAccess", "InAccess", "State");
        inAccessNode.IsExpanded = true; // Expand to show wafers during access

        // Add substrate nodes under InAccess state (E87: substrates are accessed during InAccess)
        int totalWafers = _settings.InitialWaferCount;

        for (int i = 0; i < totalWafers; i++)
        {
            // Include carrier ID in wafer node IDs to make them unique per carrier
            var substrateNode = new StateTreeNode($"{carrierId}_WAFER_W{i + 1}", $"Wafer {i + 1}", "Substrate", "WaitingForHost");
            substrateNode.IsExpanded = false; // Collapse by default to reduce clutter

            // Add E90 substrate lifecycle states as children
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_WaitingForHost", "WaitingForHost", "State") { IsActive = true });
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_InCarrier", "InCarrier", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_NeedsProcessing", "NeedsProcessing", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Aligning", "Aligning", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_ReadyToProcess", "ReadyToProcess", "State"));

            // InProcess is a hierarchical state with Polishing and Cleaning sub-states
            var inProcessNode = new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_InProcess", "InProcess", "State");
            inProcessNode.IsExpanded = true; // Expand to show Polishing and Cleaning

            // Polishing is a hierarchical state with Loading, Chucking, Polishing, Dechucking, Unloading sub-states
            var polishingNode = new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Polishing", "Polishing", "State");
            polishingNode.IsExpanded = true; // Expand to show sub-states
            polishingNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Loading", "Loading", "State"));
            polishingNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Chucking", "Chucking", "State"));
            polishingNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Polishing_Substep", "Polishing", "State"));
            polishingNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Dechucking", "Dechucking", "State"));
            polishingNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Unloading", "Unloading", "State"));
            inProcessNode.Children.Add(polishingNode);

            inProcessNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Cleaning", "Cleaning", "State"));
            substrateNode.Children.Add(inProcessNode);

            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Processed", "Processed", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Aborted", "Aborted", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Stopped", "Stopped", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Rejected", "Rejected", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Skipped", "Skipped", "State"));
            substrateNode.Children.Add(new StateTreeNode($"{carrierId}_WAFER_W{i + 1}_Complete", "Complete", "State"));

            // Add wafer to InAccess node (E87: substrates are accessible only during InAccess)
            inAccessNode.Children.Add(substrateNode);
        }

        // Add InAccess node (with all wafers as children) to carrier
        carrierNode.Children.Add(inAccessNode);

        // Add remaining E87 carrier state children (after InAccess)
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_AccessPaused", "AccessPaused", "State"));
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_Complete", "Complete", "State"));
        carrierNode.Children.Add(new StateTreeNode($"{carrierId}_CarrierOut", "CarrierOut", "State"));

        // Add carrier to LoadPort
        loadPortNode.Children.Add(carrierNode);

        Console.WriteLine($"[DEBUG AddCarrierToStateTree] SUCCESS: {carrierId} node added to state tree. LoadPort now has {loadPortNode.Children.Count} children");
        Log($"✓ Added {carrierId} node to state tree");
    }

    private void UpdateStateTree()
    {
        // Helper function to extract state name from hierarchical state (e.g., "#polisher.processing" → "processing")
        static string ExtractStateName(string fullState)
        {
            if (string.IsNullOrEmpty(fullState)) return fullState;

            // Check if it contains the hierarchical delimiter
            if (fullState.Contains("."))
            {
                return fullState.Substring(fullState.LastIndexOf('.') + 1);
            }

            return fullState;
        }

        // Update station states in the tree (extract just the state name from hierarchical path)
        StateTreeControl.UpdateNodeState("R1", ExtractStateName(_controller.R1Status), false);
        StateTreeControl.UpdateNodeState("R2", ExtractStateName(_controller.R2Status), false);
        StateTreeControl.UpdateNodeState("R3", ExtractStateName(_controller.R3Status), false);
        StateTreeControl.UpdateNodeState("Polisher", ExtractStateName(_controller.PolisherStatus), false);
        StateTreeControl.UpdateNodeState("Cleaner", ExtractStateName(_controller.CleanerStatus), false);
        StateTreeControl.UpdateNodeState("Buffer", ExtractStateName(_controller.BufferStatus), false);

        // Update LoadPort and Carrier E87 states from actual state machines
        if (_controller is OrchestratedForwardPriorityController orchestratedController)
        {
            // Get LoadPort E84 state
            string loadPortState = ExtractStateName(orchestratedController.LoadPortStatus);
            bool loadPortActive = loadPortState == "processing" || loadPortState == "carrierArrived" || loadPortState == "docked";
            StateTreeControl.UpdateNodeState("LoadPort", loadPortState, loadPortActive);

            // Update ALL carrier states dynamically by checking CarrierManager
            var carrierManager = orchestratedController.GetCarrierManager();
            if (carrierManager != null)
            {
                // Get all carriers from CarrierManager
                var allCarriers = carrierManager.GetAllCarriers().ToList();

                // DEBUG: Log how many carriers exist
                if (allCarriers.Count > 0)
                {
                    var carrierIds = string.Join(", ", allCarriers.Select(c => c.Id));
                    Console.WriteLine($"[DEBUG UpdateStateTree] Found {allCarriers.Count} carriers in CarrierManager: {carrierIds}");
                }

                // Find LoadPort node to check for carrier nodes
                var rootNode = StateTreeControl.RootNodes.FirstOrDefault(n => n.Id == "CMP_SYSTEM");
                var loadPortNode = rootNode?.Children.FirstOrDefault(n => n.Id == "LoadPort");

                if (loadPortNode != null)
                {
                    var existingCarrierIds = string.Join(", ", loadPortNode.Children.Where(n => n.NodeType == "Carrier").Select(n => n.Id));
                    Console.WriteLine($"[DEBUG UpdateStateTree] Existing carrier nodes in state tree: {existingCarrierIds}");
                }

                foreach (var carrier in allCarriers)
                {
                    string carrierId = carrier.Id;
                    string carrierState = ExtractStateName(carrier.CurrentState ?? "NotPresent");

                    // SKIP carriers that are no longer active (CarrierOut or NotPresent)
                    // This prevents old carriers from being re-added to the state tree
                    if (carrierState == "CarrierOut" || carrierState == "NotPresent")
                    {
                        Console.WriteLine($"[DEBUG UpdateStateTree] SKIPPING {carrierId} (state={carrierState}) - not active");
                        continue;
                    }

                    // Ensure carrier node exists in state tree (only for active carriers)
                    if (loadPortNode != null && !loadPortNode.Children.Any(n => n.Id == carrierId))
                    {
                        // Carrier node doesn't exist yet, add it
                        Console.WriteLine($"[DEBUG UpdateStateTree] Carrier node {carrierId} NOT FOUND in state tree, adding it now...");
                        AddCarrierToStateTree(carrierId);
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG UpdateStateTree] Carrier node {carrierId} already exists in state tree");
                    }

                    bool carrierActive = carrierState != "NotPresent" && carrierState != "CarrierOut" && carrierState != "None";

                    Console.WriteLine($"[DEBUG UpdateStateTree] Updating {carrierId} state to: {carrierState} (active={carrierActive})");

                    // Update the node state
                    StateTreeControl.UpdateNodeState(carrierId, carrierState, carrierActive);
                }
            }
            else
            {
                Console.WriteLine("[DEBUG UpdateStateTree] CarrierManager is NULL!");
            }
        }

        // Clear all highlights first
        StateTreeControl.ClearAllHighlights();

        // Highlight active/processing nodes
        if (_controller.PolisherStatus.Contains("Processing") || _controller.PolisherStatus.Contains("Busy"))
        {
            StateTreeControl.UpdateNodeState("Polisher", _controller.PolisherStatus, true);
        }

        if (_controller.CleanerStatus.Contains("Processing") || _controller.CleanerStatus.Contains("Busy"))
        {
            StateTreeControl.UpdateNodeState("Cleaner", _controller.CleanerStatus, true);
        }

        if (_controller.R1Status.Contains("Transferring") || _controller.R1Status.Contains("Busy"))
        {
            StateTreeControl.UpdateNodeState("R1", _controller.R1Status, true);
        }

        if (_controller.R2Status.Contains("Transferring") || _controller.R2Status.Contains("Busy"))
        {
            StateTreeControl.UpdateNodeState("R2", _controller.R2Status, true);
        }

        if (_controller.R3Status.Contains("Transferring") || _controller.R3Status.Contains("Busy"))
        {
            StateTreeControl.UpdateNodeState("R3", _controller.R3Status, true);
        }

        // Update substrate (wafer) E90 states from their state machines
        foreach (var wafer in _controller.Wafers)
        {
            // Include carrier ID in the substrate node ID (e.g., "CARRIER_001_WAFER_W1")
            string substrateId = $"{wafer.CarrierId}_WAFER_W{wafer.Id}";
            string e90State = wafer.E90State ?? "WaitingForHost";
            bool isHighlighted = wafer.CurrentStation != "LoadPort" && !wafer.IsCompleted;

            // Debug: Log font color state for monitoring
            // Font colors: Black (not processed) → Yellow (polished) → White (cleaned)
            string fontColorState = e90State switch
            {
                "Processed" or "Complete" => "White (Cleaned)",
                "Cleaning" => "Yellow (Polished)",
                _ => "Black (Not Processed)"
            };

            // DEBUG: Log state updates for wafers 2 and 10 to trace "Loading" bug
            if (wafer.Id == 2 || wafer.Id == 10)
            {
                Console.WriteLine($"[DEBUG UpdateStateTree] Wafer {wafer.Id}: CarrierId={wafer.CarrierId}, E90State={e90State}, FontColor={fontColorState}, SubstrateId={substrateId}");
            }

            StateTreeControl.UpdateNodeState(substrateId, e90State, isHighlighted);
        }
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Handle Ctrl+Click for panning
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            _isPanning = true;
            _panStart = e.GetPosition(SimulationScrollViewer);
            _panStartHorizontalOffset = SimulationScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = SimulationScrollViewer.VerticalOffset;
            CMPSystem.Canvas.Cursor = Cursors.Hand;
            CMPSystem.Canvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Handle normal click for CMP System selection (not in edit mode)
        if (!_isPanning && !StationEditor.IsEditMode)
        {
            // Check if click is on a station by testing hit test
            var clickedElement = e.OriginalSource as FrameworkElement;

            // If clicked on the canvas background (not a station), select CMP System
            if (clickedElement is Canvas || clickedElement is Border)
            {
                SelectCMPSystem();
            }
        }
    }

    private void SelectCMPSystem()
    {
        _selectedStationName = null;
        SelectedObjectNameText.Text = "Selected: CMP System";

        // Clear and rebuild property display
        PropertyGridContainer.Children.Clear();

        // Show CMP System properties
        ShowCMPSystemProperties();
    }

    private void ShowCMPSystemProperties()
    {
        AddPropertyRow("Type", "CMP System Container");
        AddPropertyRow("Canvas Size", $"{CMPSystem.Width} × {CMPSystem.Height}");
        AddPropertyRow("Grid Size", $"{CMPSystemControl.GridSize}px");
        AddPropertyRow("Total Stations", "7");
        AddPropertyRow("Total Wafers", CMPSystem.TotalWaferCount.ToString());
        AddPropertyRow("Pre-Process", CMPSystem.PreProcessCount.ToString());
        AddPropertyRow("Post-Process", CMPSystem.PostProcessCount.ToString());

        // Add separator
        var separator = new System.Windows.Controls.Separator { Margin = new Thickness(0, 10, 0, 10) };
        PropertyGridContainer.Children.Add(separator);

        // Add Scheduler information (if available)
        if (_controller is OrchestratedForwardPriorityController orchestratedController)
        {
            var schedulerTitle = new System.Windows.Controls.TextBlock
            {
                Text = "Scheduler Status",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            PropertyGridContainer.Children.Add(schedulerTitle);

            // Get elapsed time since simulation start
            var elapsed = DateTime.Now - _simulationStartTime;
            string elapsedTime = $"{elapsed.TotalSeconds:F1}s";

            AddPropertyRow("Elapsed Time", elapsedTime);

            // Add separator before grid options
            var separator2 = new System.Windows.Controls.Separator { Margin = new Thickness(0, 10, 0, 10) };
            PropertyGridContainer.Children.Add(separator2);
        }

        // Add grid display options
        var gridOptionsTitle = new System.Windows.Controls.TextBlock
        {
            Text = "Grid Display Options",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };
        PropertyGridContainer.Children.Add(gridOptionsTitle);

        // Create radio buttons for grid display mode
        var gridModePanel = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(0, 0, 0, 10)
        };

        var lineRadio = new System.Windows.Controls.RadioButton
        {
            Content = "Lines Only",
            GroupName = "GridDisplayMode",
            IsChecked = CMPSystem.GridMode == GridDisplayMode.Line,
            Margin = new Thickness(0, 0, 0, 5)
        };
        lineRadio.Checked += (s, e) => CMPSystem.GridMode = GridDisplayMode.Line;

        var dotRadio = new System.Windows.Controls.RadioButton
        {
            Content = "Dots Only",
            GroupName = "GridDisplayMode",
            IsChecked = CMPSystem.GridMode == GridDisplayMode.Dot,
            Margin = new Thickness(0, 0, 0, 5)
        };
        dotRadio.Checked += (s, e) => CMPSystem.GridMode = GridDisplayMode.Dot;

        var bothRadio = new System.Windows.Controls.RadioButton
        {
            Content = "Lines + Dots",
            GroupName = "GridDisplayMode",
            IsChecked = CMPSystem.GridMode == GridDisplayMode.Both,
            Margin = new Thickness(0, 0, 0, 5)
        };
        bothRadio.Checked += (s, e) => CMPSystem.GridMode = GridDisplayMode.Both;

        gridModePanel.Children.Add(lineRadio);
        gridModePanel.Children.Add(dotRadio);
        gridModePanel.Children.Add(bothRadio);
        PropertyGridContainer.Children.Add(gridModePanel);
    }

    private void SelectStation(string stationName)
    {
        _selectedStationName = stationName;
        SelectedObjectNameText.Text = $"Selected: {stationName}";

        // Clear and rebuild property display
        PropertyGridContainer.Children.Clear();

        // Show station-specific properties
        switch (stationName)
        {
            case "LoadPort":
                ShowLoadPortProperties(stationName);
                break;
            case "R1":
            case "R2":
            case "R3":
                ShowRobotProperties(stationName);
                break;
            case "Polisher":
            case "Cleaner":
                ShowProcessStationProperties(stationName);
                break;
            case "Buffer":
                ShowBufferProperties();
                break;
        }
    }

    private void ShowLoadPortProperties(string stationName)
    {
        LoadPortControl loadPortControl = LoadPortControl;
        StationGeometry geometry = _settings.LoadPort;

        AddPropertyRow("Type", "LoadPort Station");
        AddPropertyRow("Capacity", "25 wafers (5x5 grid)");
        AddPropertyRow("Current Status", loadPortControl.StatusText);
        AddPropertyRow("Function", "Load/Unload wafers");

        // Add separator before wafer count section
        var separator1 = new System.Windows.Controls.Separator { Margin = new Thickness(0, 10, 0, 10) };
        PropertyGridContainer.Children.Add(separator1);

        // Add Wafer Count section title
        var waferCountTitle = new System.Windows.Controls.TextBlock
        {
            Text = "Wafer Configuration",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };
        PropertyGridContainer.Children.Add(waferCountTitle);

        // Add editable InitialWaferCount property
        AddEditablePropertyRow("Initial Wafers", _settings.InitialWaferCount, (newValue) => {
            _settings.InitialWaferCount = (int)newValue;
        });

        // Add Apply button for wafer count
        var applyWaferButton = new Button
        {
            Content = "Apply Wafer Count",
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        applyWaferButton.Click += (s, e) => ApplyWaferCountChanges();
        PropertyGridContainer.Children.Add(applyWaferButton);

        // Add Geometry section
        AddGeometrySection(geometry);

        // Show connections
        if (loadPortControl.NextForward.Count > 0)
        {
            var forwardStations = string.Join(", ", loadPortControl.NextForward.Select(s => s.StationName));
            AddPropertyRow("Next (Forward)", forwardStations);
            if (loadPortControl.NextForward.Count > 1)
            {
                AddPropertyRow("  ↳ Routing", loadPortControl.ForwardRoutingStrategy.ToString());
            }
        }
        if (loadPortControl.NextBackward.Count > 0)
        {
            var backwardStations = string.Join(", ", loadPortControl.NextBackward.Select(s => s.StationName));
            AddPropertyRow("Next (Backward)", backwardStations);
            if (loadPortControl.NextBackward.Count > 1)
            {
                AddPropertyRow("  ↳ Routing", loadPortControl.BackwardRoutingStrategy.ToString());
            }
        }
    }

    private void ApplyWaferCountChanges()
    {
        try
        {
            // Validate value
            if (_settings.InitialWaferCount < 0 || _settings.InitialWaferCount > 25)
            {
                MessageBox.Show("Initial wafer count must be between 0 and 25.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save settings
            SettingsManager.SaveSettings(_settings);

            // Show success message
            MessageBox.Show($"Initial wafer count set to {_settings.InitialWaferCount}\n\nNote: This will take effect on next simulation reset/start.",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            Log($"Initial wafer count updated: {_settings.InitialWaferCount}");

            // Refresh property display
            SelectStation("LoadPort");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply wafer count: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowRobotProperties(string robotName)
    {
        string status = robotName switch
        {
            "R1" => _controller.R1Status,
            "R2" => _controller.R2Status,
            "R3" => _controller.R3Status,
            _ => "Unknown"
        };

        string function = robotName switch
        {
            "R1" => "Transfer: LoadPort ↔ Polisher ↔ Buffer",
            "R2" => "Transfer: Polisher ↔ Cleaner",
            "R3" => "Transfer: Cleaner ↔ Buffer",
            _ => "Unknown"
        };

        StationControl? robotControl = robotName switch
        {
            "R1" => R1Control,
            "R2" => R2Control,
            "R3" => R3Control,
            _ => null
        };

        StationGeometry? geometry = robotName switch
        {
            "R1" => _settings.R1,
            "R2" => _settings.R2,
            "R3" => _settings.R3,
            _ => null
        };

        AddPropertyRow("Type", "Robot Station");
        AddPropertyRow("Current Status", status);
        AddPropertyRow("Function", function);

        // Add Geometry section
        if (geometry != null)
        {
            AddGeometrySection(geometry);
        }

        // Show connections
        if (robotControl != null)
        {
            if (robotControl.NextForward.Count > 0)
            {
                var forwardStations = string.Join(", ", robotControl.NextForward.Select(s => s.StationName));
                AddPropertyRow("Next (Forward)", forwardStations);
                if (robotControl.NextForward.Count > 1)
                {
                    AddPropertyRow("  ↳ Routing", robotControl.ForwardRoutingStrategy.ToString());
                }
            }
            if (robotControl.NextBackward.Count > 0)
            {
                var backwardStations = string.Join(", ", robotControl.NextBackward.Select(s => s.StationName));
                AddPropertyRow("Next (Backward)", backwardStations);
                if (robotControl.NextBackward.Count > 1)
                {
                    AddPropertyRow("  ↳ Routing", robotControl.BackwardRoutingStrategy.ToString());
                }
            }
        }
    }

    private void ShowProcessStationProperties(string stationName)
    {
        string status = stationName == "Polisher" ? _controller.PolisherStatus : _controller.CleanerStatus;
        string processType = stationName == "Polisher" ? "Polishing" : "Cleaning";
        StationControl? processControl = stationName == "Polisher" ? PolisherControl : CleanerControl;
        StationGeometry geometry = stationName == "Polisher" ? _settings.Polisher : _settings.Cleaner;

        AddPropertyRow("Type", "Process Station");
        AddPropertyRow("Process", processType);
        AddPropertyRow("Current Status", status);
        AddPropertyRow("Capacity", "1 wafer");

        // Add Geometry section
        AddGeometrySection(geometry);

        // Show connections
        if (processControl != null)
        {
            if (processControl.NextForward.Count > 0)
            {
                var forwardStations = string.Join(", ", processControl.NextForward.Select(s => s.StationName));
                AddPropertyRow("Next (Forward)", forwardStations);
                if (processControl.NextForward.Count > 1)
                {
                    AddPropertyRow("  ↳ Routing", processControl.ForwardRoutingStrategy.ToString());
                }
            }
            if (processControl.NextBackward.Count > 0)
            {
                var backwardStations = string.Join(", ", processControl.NextBackward.Select(s => s.StationName));
                AddPropertyRow("Next (Backward)", backwardStations);
                if (processControl.NextBackward.Count > 1)
                {
                    AddPropertyRow("  ↳ Routing", processControl.BackwardRoutingStrategy.ToString());
                }
            }
        }
    }

    private void ShowBufferProperties()
    {
        AddPropertyRow("Type", "Buffer Station");
        AddPropertyRow("Current Status", _controller.BufferStatus);
        AddPropertyRow("Capacity", "1 wafer");
        AddPropertyRow("Function", "Temporary storage");

        // Add Geometry section
        AddGeometrySection(_settings.Buffer);

        // Show connections
        if (BufferControl.NextForward.Count > 0)
        {
            var forwardStations = string.Join(", ", BufferControl.NextForward.Select(s => s.StationName));
            AddPropertyRow("Next (Forward)", forwardStations);
            if (BufferControl.NextForward.Count > 1)
            {
                AddPropertyRow("  ↳ Routing", BufferControl.ForwardRoutingStrategy.ToString());
            }
        }
        if (BufferControl.NextBackward.Count > 0)
        {
            var backwardStations = string.Join(", ", BufferControl.NextBackward.Select(s => s.StationName));
            AddPropertyRow("Next (Backward)", backwardStations);
            if (BufferControl.NextBackward.Count > 1)
            {
                AddPropertyRow("  ↳ Routing", BufferControl.BackwardRoutingStrategy.ToString());
            }
        }
    }

    private void AddPropertyRow(string label, string value)
    {
        var grid = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new System.Windows.Controls.TextBlock
        {
            Text = label + ":",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(labelText, 0);

        var valueText = new System.Windows.Controls.TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(valueText, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
        PropertyGridContainer.Children.Add(grid);
    }

    private void AddGeometrySection(StationGeometry geometry)
    {
        // Add separator before geometry section
        var separator = new System.Windows.Controls.Separator { Margin = new Thickness(0, 10, 0, 10) };
        PropertyGridContainer.Children.Add(separator);

        // Add Geometry section title
        var geometryTitle = new System.Windows.Controls.TextBlock
        {
            Text = "Geometry (Editable)",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };
        PropertyGridContainer.Children.Add(geometryTitle);

        // Add read-only summary
        AddPropertyRow("Position", geometry.PositionString);
        AddPropertyRow("Size", geometry.SizeString);

        // Add editable geometry properties
        AddEditablePropertyRow("Left", geometry.Left, (newValue) => {
            geometry.Left = newValue;
            UpdateStationGeometry();
        });
        AddEditablePropertyRow("Top", geometry.Top, (newValue) => {
            geometry.Top = newValue;
            UpdateStationGeometry();
        });
        AddEditablePropertyRow("Width", geometry.Width, (newValue) => {
            geometry.Width = newValue;
            UpdateStationGeometry();
        });
        AddEditablePropertyRow("Height", geometry.Height, (newValue) => {
            geometry.Height = newValue;
            UpdateStationGeometry();
        });

        // Add Apply button
        var applyButton = new Button
        {
            Content = "Apply Geometry",
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        applyButton.Click += (s, e) => ApplyGeometryChanges(geometry);
        PropertyGridContainer.Children.Add(applyButton);
    }

    private void AddEditablePropertyRow(string label, double value, Action<double> onValueChanged)
    {
        var grid = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new System.Windows.Controls.TextBlock
        {
            Text = label + ":",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(labelText, 0);

        var textBox = new TextBox
        {
            Text = value.ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(5, 3, 5, 3)
        };
        textBox.PreviewTextInput += NumberValidationTextBox;
        textBox.TextChanged += (s, e) => {
            if (double.TryParse(textBox.Text, out double newValue))
            {
                onValueChanged(newValue);
            }
        };
        System.Windows.Controls.Grid.SetColumn(textBox, 1);

        grid.Children.Add(labelText);
        grid.Children.Add(textBox);
        PropertyGridContainer.Children.Add(grid);
    }

    private void UpdateStationGeometry()
    {
        // Update the actual station controls with new geometry
        if (_selectedStationName == null) return;

        StationControl? station = _selectedStationName switch
        {
            "LoadPort" => LoadPortControl,
            "R1" => R1Control,
            "Polisher" => PolisherControl,
            "R2" => R2Control,
            "Cleaner" => CleanerControl,
            "R3" => R3Control,
            "Buffer" => BufferControl,
            _ => null
        };

        StationGeometry? geometry = _selectedStationName switch
        {
            "LoadPort" => _settings.LoadPort,
            "R1" => _settings.R1,
            "Polisher" => _settings.Polisher,
            "R2" => _settings.R2,
            "Cleaner" => _settings.Cleaner,
            "R3" => _settings.R3,
            "Buffer" => _settings.Buffer,
            _ => null
        };

        if (station != null && geometry != null)
        {
            station.Width = geometry.Width;
            station.Height = geometry.Height;
            Canvas.SetLeft(station, geometry.Left);
            Canvas.SetTop(station, geometry.Top);
        }
    }

    private void ApplyGeometryChanges(StationGeometry geometry)
    {
        try
        {
            // Validate values
            if (geometry.Left < 0 || geometry.Top < 0 || geometry.Width <= 0 || geometry.Height <= 0)
            {
                MessageBox.Show("All geometry values must be positive (Width and Height must be > 0).",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save settings
            SettingsManager.SaveSettings(_settings);

            // Show success message
            MessageBox.Show($"Geometry updated and saved for {_selectedStationName}",
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            Log($"Geometry applied for {_selectedStationName}: ({geometry.Left}, {geometry.Top}), {geometry.Width}×{geometry.Height}");

            // Refresh property display
            if (_selectedStationName != null)
            {
                SelectStation(_selectedStationName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply geometry: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecutionMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;

        bool isSyncMode = SyncModeRadio.IsChecked == true;
        _controller.SetExecutionMode(isSyncMode ? ExecutionMode.Sync : ExecutionMode.Async);

        // Update UI based on mode
        // Note: Don't enable StepButton here - it's enabled by StartButton_Click
        // This just switches the mode for when Start is pressed
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        StepButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ResetButton.IsEnabled = false;

        // Reset simulation start time when starting
        _simulationStartTime = DateTime.Now;

        // Start remaining time update timer (update every 100ms for smooth display)
        if (_remainingTimeTimer == null)
        {
            _remainingTimeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _remainingTimeTimer.Tick += RemainingTimeTimer_Tick;
        }
        _remainingTimeTimer.Start();

        await _controller.StartSimulation();
    }

    private async void StepButton_Click(object sender, RoutedEventArgs e)
    {
        StepButton.IsEnabled = false;
        StartButton.IsEnabled = false;

        await _controller.ExecuteOneStep();

        StepButton.IsEnabled = true;
        StartButton.IsEnabled = true;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.StopSimulation();

        // Stop remaining time timer
        _remainingTimeTimer?.Stop();

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        ResetButton.IsEnabled = true;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.ResetSimulation();

        // Stop remaining time timer
        _remainingTimeTimer?.Stop();

        // Reset simulation start time
        _simulationStartTime = DateTime.Now;

        StartButton.IsEnabled = true;
        StepButton.IsEnabled = true;
        StopButton.IsEnabled = false;

        // Reset statistics display
        if (_controller is OrchestratedForwardPriorityController orchestratedController)
        {
            ElapsedTimeText.Text = "0.0s";
            CompletedWafersText.Text = "0";
            PendingWafersText.Text = orchestratedController.TOTAL_WAFERS.ToString();
            ThroughputText.Text = "0.00 wafers/s";
            TheoreticalMinText.Text = orchestratedController.TheoreticalMinTime;
            EfficiencyText.Text = "0.0%";
        }

        // Update station status displays
        UpdateStationDisplays();
    }

    private void RemainingTimeTimer_Tick(object? sender, EventArgs e)
    {
        // Update remaining time display for Polisher and Cleaner at regular intervals
        if (_controller is OrchestratedForwardPriorityController orchestratedController)
        {
            PolisherControl.RemainingTime = orchestratedController.PolisherRemainingTime;
            CleanerControl.RemainingTime = orchestratedController.CleanerRemainingTime;

            // Update real-time statistics display
            ElapsedTimeText.Text = orchestratedController.ElapsedTime;
            CompletedWafersText.Text = orchestratedController.CompletedWafers.ToString();
            PendingWafersText.Text = orchestratedController.PendingWafers.ToString();
            ThroughputText.Text = orchestratedController.Throughput;
            TheoreticalMinText.Text = orchestratedController.TheoreticalMinTime;
            EfficiencyText.Text = orchestratedController.Efficiency;

            // Stop timer when all wafers are completed
            if (orchestratedController.CompletedWafers >= orchestratedController.TOTAL_WAFERS)
            {
                _remainingTimeTimer?.Stop();
                StopButton.IsEnabled = false;
                ResetButton.IsEnabled = true;
            }
        }
    }

    private void Controller_LogMessage(object? sender, string message)
    {
        Dispatcher.Invoke(() => Log(message));
    }

    private void Controller_StationStatusChanged(object? sender, EventArgs e)
    {
        // Already on UI thread (called from Dispatcher.Invoke in UIUpdateService)
        UpdateStationDisplays();
        UpdateStateTree();
    }

    private void Controller_RemoveOldCarrierFromStateTree(object? sender, string carrierId)
    {
        Dispatcher.Invoke(() =>
        {
            StateTreeControl.RemoveCarrierNode(carrierId);
            Log($"🗑️ Removed {carrierId} from state tree");
        });
    }

    // Static file logger (shared with SchedulingRuleEngine)
    private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recent processing history.log");
    private static readonly object _logFileLock = new object();

    private void Log(string message)
    {
        // Calculate elapsed time since simulation start
        var elapsed = DateTime.Now - _simulationStartTime;
        string timestamp = $"[{elapsed.TotalSeconds:000.000}] ";

        // Write to log file (same file used by SchedulingRuleEngine)
        try
        {
            lock (_logFileLock)
            {
                File.AppendAllText(_logFilePath, timestamp + message + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow.Log] Failed to write to log file: {ex.Message}");
        }

        // NOTE: Carrier creation detection REMOVED - carriers are now added dynamically via UpdateStateTree()
        // based on CarrierManager state, not log messages. This prevents old carriers from being re-added
        // when log messages mention them.

        // Update station displays when relevant events occur
        if (message.Contains("LoadPort") || message.Contains("Polisher") || message.Contains("Cleaner"))
        {
            UpdateStationDisplays();
        }
    }

    private void UpdateStationDisplays()
    {
        // Update all station controls with current wafer data
        // Set carrier information for LoadPort if available
        if (_controller is OrchestratedForwardPriorityController orchestratedController)
        {
            var carrierManager = orchestratedController.GetCarrierManager();
            if (carrierManager != null)
            {
                LoadPortControl.CurrentCarrier = carrierManager.GetCarrierAtLoadPort("LoadPort");
            }
        }

        LoadPortControl.UpdateWafers(_controller.Wafers);
        R1Control.UpdateWafers(_controller.Wafers);
        R1Control.StatusText = _controller.R1Status;
        PolisherControl.UpdateWafers(_controller.Wafers);
        PolisherControl.StatusText = _controller.PolisherStatus;

        // Note: Remaining time is updated by the RemainingTimeTimer_Tick method (100ms intervals)

        R2Control.UpdateWafers(_controller.Wafers);
        R2Control.StatusText = _controller.R2Status;
        CleanerControl.UpdateWafers(_controller.Wafers);
        CleanerControl.StatusText = _controller.CleanerStatus;

        // Note: Remaining time is updated by the RemainingTimeTimer_Tick method (100ms intervals)

        R3Control.UpdateWafers(_controller.Wafers);
        R3Control.StatusText = _controller.R3Status;
        BufferControl.UpdateWafers(_controller.Wafers);
        BufferControl.StatusText = _controller.BufferStatus;

        // Update total wafer count in CMP System with Pre/Post breakdown
        CMPSystem.UpdateTotalWaferCount(_controller.Wafers);
    }

    private void SeeLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Path to the log file written by SchedulingRuleEngine
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recent processing history.log");

            if (!File.Exists(logFilePath))
            {
                MessageBox.Show($"Log file not found.\n\nPath: {logFilePath}\n\nRun the simulation first to generate the log.",
                    "Log File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Open with default text editor
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = logFilePath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processStartInfo);

            Log($"Log file opened: {logFilePath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open log file: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only zoom when Ctrl key is pressed
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;

            // Calculate new zoom level
            if (e.Delta > 0)
            {
                // Zoom in
                _currentZoom = Math.Min(_currentZoom + ZoomStep, ZoomMax);
            }
            else
            {
                // Zoom out
                _currentZoom = Math.Max(_currentZoom - ZoomStep, ZoomMin);
            }

            // Apply zoom to CMP System
            _zoomTransform.ScaleX = _currentZoom;
            _zoomTransform.ScaleY = _currentZoom;

            // Redraw grid with new zoom level to adjust dot size inversely
            CMPSystem.RedrawGrid(_currentZoom);
        }
    }

    private void SimulationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only pan when Ctrl key is pressed
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            _isPanning = true;
            _panStart = e.GetPosition(SimulationScrollViewer);
            _panStartHorizontalOffset = SimulationScrollViewer.HorizontalOffset;
            _panStartVerticalOffset = SimulationScrollViewer.VerticalOffset;
            CMPSystem.Canvas.Cursor = Cursors.Hand;
            CMPSystem.Canvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SimulationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Point currentPosition = e.GetPosition(SimulationScrollViewer);
            double deltaX = _panStart.X - currentPosition.X;
            double deltaY = _panStart.Y - currentPosition.Y;

            SimulationScrollViewer.ScrollToHorizontalOffset(_panStartHorizontalOffset + deltaX);
            SimulationScrollViewer.ScrollToVerticalOffset(_panStartVerticalOffset + deltaY);
            e.Handled = true;
        }
    }

    private void SimulationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            CMPSystem.Canvas.Cursor = Cursors.Arrow;
            CMPSystem.Canvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void SimulationCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            CMPSystem.Canvas.Cursor = Cursors.Arrow;
            CMPSystem.Canvas.ReleaseMouseCapture();
        }
    }

    private void NumberValidationTextBox(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Only allow numeric input
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Parse timing values
            if (!int.TryParse(R1TransferTextBox.Text, out int r1Transfer) || r1Transfer < 0)
            {
                MessageBox.Show("R1 Transfer time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(PolisherTextBox.Text, out int polisher) || polisher < 0)
            {
                MessageBox.Show("Polisher time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(R2TransferTextBox.Text, out int r2Transfer) || r2Transfer < 0)
            {
                MessageBox.Show("R2 Transfer time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(CleanerTextBox.Text, out int cleaner) || cleaner < 0)
            {
                MessageBox.Show("Cleaner time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(R3TransferTextBox.Text, out int r3Transfer) || r3Transfer < 0)
            {
                MessageBox.Show("R3 Transfer time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(BufferHoldTextBox.Text, out int bufferHold) || bufferHold < 0)
            {
                MessageBox.Show("Buffer Hold time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(LoadPortReturnTextBox.Text, out int loadPortReturn) || loadPortReturn < 0)
            {
                MessageBox.Show("LoadPort Return time must be a positive number.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Apply settings to controller (wafer count is managed by LoadPort)
            _controller.UpdateSettings(r1Transfer, polisher, r2Transfer, cleaner, r3Transfer, bufferHold, loadPortReturn);

            // Update settings object with new timing values
            _settings.R1TransferTime = r1Transfer;
            _settings.PolisherTime = polisher;
            _settings.R2TransferTime = r2Transfer;
            _settings.CleanerTime = cleaner;
            _settings.R3TransferTime = r3Transfer;
            _settings.BufferHoldTime = bufferHold;
            _settings.LoadPortReturnTime = loadPortReturn;

            // Auto-save settings to file
            SettingsManager.SaveSettings(_settings);

            // Show success message
            SettingsStatusTextBlock.Text = "Settings applied and saved successfully!";
            SettingsStatusTextBlock.Foreground = Brushes.Green;

            // Clear success message after 3 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, args) =>
            {
                SettingsStatusTextBlock.Text = "";
                timer.Stop();
            };
            timer.Start();

            Log("Settings updated: " +
                "R1=" + r1Transfer + "ms" +
                ", Polisher=" + polisher + "ms" +
                ", R2=" + r2Transfer + "ms" +
                ", Cleaner=" + cleaner + "ms" +
                ", R3=" + r3Transfer + "ms" +
                ", Buffer=" + bufferHold + "ms" +
                ", LoadPort=" + loadPortReturn + "ms");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply settings: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            SettingsStatusTextBlock.Text = "Failed to apply settings";
            SettingsStatusTextBlock.Foreground = Brushes.Red;
        }
    }

    private void EditMode_Checked(object sender, RoutedEventArgs e)
    {
        StationEditor.IsEditMode = true;

        // Subscribe to geometry changed event
        StationEditor.GeometryChanged += StationEditor_GeometryChanged;

        // Enable edit mode for all stations
        StationEditor.EnableEditMode(LoadPortControl);
        StationEditor.EnableEditMode(R1Control);
        StationEditor.EnableEditMode(PolisherControl);
        StationEditor.EnableEditMode(R2Control);
        StationEditor.EnableEditMode(CleanerControl);
        StationEditor.EnableEditMode(R3Control);
        StationEditor.EnableEditMode(BufferControl);

        Log("✏ Edit Mode enabled - You can now move and resize stations");
    }

    private void EditMode_Unchecked(object sender, RoutedEventArgs e)
    {
        StationEditor.IsEditMode = false;

        // Unsubscribe from geometry changed event
        StationEditor.GeometryChanged -= StationEditor_GeometryChanged;

        // Disable edit mode for all stations
        StationEditor.DisableEditMode(LoadPortControl);
        StationEditor.DisableEditMode(R1Control);
        StationEditor.DisableEditMode(PolisherControl);
        StationEditor.DisableEditMode(R2Control);
        StationEditor.DisableEditMode(CleanerControl);
        StationEditor.DisableEditMode(R3Control);
        StationEditor.DisableEditMode(BufferControl);

        Log("Edit Mode disabled");
    }

    private void StationEditor_GeometryChanged(object? sender, StationGeometryChangedEventArgs e)
    {
        // Update settings based on which station was changed
        switch (e.StationName)
        {
            case "LoadPort":
                _settings.LoadPort.Left = e.Left;
                _settings.LoadPort.Top = e.Top;
                _settings.LoadPort.Width = e.Width;
                _settings.LoadPort.Height = e.Height;
                break;
            case "R1":
                _settings.R1.Left = e.Left;
                _settings.R1.Top = e.Top;
                _settings.R1.Width = e.Width;
                _settings.R1.Height = e.Height;
                break;
            case "Polisher":
                _settings.Polisher.Left = e.Left;
                _settings.Polisher.Top = e.Top;
                _settings.Polisher.Width = e.Width;
                _settings.Polisher.Height = e.Height;
                break;
            case "R2":
                _settings.R2.Left = e.Left;
                _settings.R2.Top = e.Top;
                _settings.R2.Width = e.Width;
                _settings.R2.Height = e.Height;
                break;
            case "Cleaner":
                _settings.Cleaner.Left = e.Left;
                _settings.Cleaner.Top = e.Top;
                _settings.Cleaner.Width = e.Width;
                _settings.Cleaner.Height = e.Height;
                break;
            case "R3":
                _settings.R3.Left = e.Left;
                _settings.R3.Top = e.Top;
                _settings.R3.Width = e.Width;
                _settings.R3.Height = e.Height;
                break;
            case "Buffer":
                _settings.Buffer.Left = e.Left;
                _settings.Buffer.Top = e.Top;
                _settings.Buffer.Width = e.Width;
                _settings.Buffer.Height = e.Height;
                break;
        }

        // Auto-save settings
        SettingsManager.SaveSettings(_settings);
        Log($"Station geometry updated: {e.StationName} at ({e.Left}, {e.Top}), size {e.Width}×{e.Height}");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _controller?.Dispose();
    }
}
