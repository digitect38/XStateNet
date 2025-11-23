using System.Text;

namespace CMPSimXS2.Parallel;

/// <summary>
/// Logs system events in columnar format showing parallel wafer progression
/// </summary>
public static class TableLogger
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, List<string>> _waferActions = new();
    private static readonly HashSet<string> _activeWafers = new();
    private static readonly List<string> _completedWafers = new();
    private static List<string> _previousActiveWafers = new();
    private static readonly Dictionary<string, string> _currentStepActions = new(); // Actions for current step only
    private const int ColumnWidth = 35;
    private const int MaxParallelColumns = 3; // Show up to 3 wafers in parallel
    private static int _globalStepCounter = 0;
    private static bool _stepHasActions = false;

    // Configuration flag to disable verbose logging
    public static bool EnableVerboseLogging = false;

    // Track if current step is synchronized (all wafers acted)
    private static bool _isCurrentStepSynchronized = false;

    // Track current station for each wafer
    private static readonly Dictionary<string, string> _waferCurrentStation = new();
    private static readonly Dictionary<string, string> _previousWaferStations = new();

    // Fixed columns: COORD (non-position communications), then physical layout
    private static readonly string[] FixedColumns = new[] { "COORD", "R1_FWD", "POLISHER", "R2", "CLEANER", "R3", "BUFFER", "R1_RET" };
    private static readonly Dictionary<string, string> _columnCurrentWafer = new(); // column -> waferId
    private static readonly Dictionary<string, string> _previousColumnWafers = new();


    public static void Log(string message)
    {
        if (EnableVerboseLogging)
        {
            Console.WriteLine(message);
        }
    }

    public static void MarkStepAsSynchronized()
    {
        _isCurrentStepSynchronized = true;
    }

    public static void Initialize()
    {
        _waferActions.Clear();
        _activeWafers.Clear();
        _completedWafers.Clear();
        _previousActiveWafers.Clear();
        _currentStepActions.Clear();
        _waferCurrentStation.Clear();
        _globalStepCounter = 0;
        _stepHasActions = false;

        // Print column header once before Step 1
        PrintColumnHeader();
    }

    private static void PrintColumnHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        var header = new StringBuilder();
        header.Append("Step".PadRight(10));
        foreach (var column in FixedColumns)
        {
            header.Append(column.PadRight(ColumnWidth));
        }
        Console.WriteLine(header.ToString());
        Console.WriteLine(new string('-', 10 + (ColumnWidth * FixedColumns.Length)));
        Console.ResetColor();
    }

    public static void LogWaferAction(string waferId, string action)
    {
        lock (_lock)
        {
            if (!_waferActions.ContainsKey(waferId))
            {
                _waferActions[waferId] = new List<string>();
            }

            _waferActions[waferId].Add(action);

            if (!_activeWafers.Contains(waferId) && !_completedWafers.Contains(waferId))
            {
                _activeWafers.Add(waferId);
            }

            // Add to current step actions
            _currentStepActions[waferId] = action;

            if (!_stepHasActions)
            {
                _stepHasActions = true;
            }

            // Print current pipeline state
            PrintPipelineStep();

            // Clear current step actions for next step
            _currentStepActions.Clear();
            _stepHasActions = false;
        }
    }

    public static void CompleteWafer(string waferId)
    {
        lock (_lock)
        {
            if (_activeWafers.Contains(waferId))
            {
                _activeWafers.Remove(waferId);
                _completedWafers.Add(waferId);

                // Print separator when wafer completes
                Console.WriteLine();
            }
        }
    }

    private static void PrintPipelineStep()
    {
        if (_currentStepActions.Count == 0) return;

        // Build new column assignments based on current actions
        var newColumnAssignments = new Dictionary<string, string>(_columnCurrentWafer);

        foreach (var (waferId, action) in _currentStepActions)
        {
            var column = DetermineColumn(action);
            if (!string.IsNullOrEmpty(column))
            {
                // Remove this wafer from any previous column in new assignments
                foreach (var key in newColumnAssignments.Keys.ToList())
                {
                    if (newColumnAssignments[key] == waferId)
                    {
                        newColumnAssignments.Remove(key);
                    }
                }

                // Assign wafer to new column
                newColumnAssignments[column] = waferId;
            }
        }

        // Check if any column's wafer assignment has actually changed
        bool columnAssignmentChanged = false;
        foreach (var column in FixedColumns)
        {
            var newWafer = newColumnAssignments.ContainsKey(column) ? newColumnAssignments[column] : "";
            var previousWafer = _previousColumnWafers.ContainsKey(column) ? _previousColumnWafers[column] : "";

            if (newWafer != previousWafer)
            {
                columnAssignmentChanged = true;
                break;
            }
        }

        // Update current column assignments
        _columnCurrentWafer.Clear();
        foreach (var kvp in newColumnAssignments)
        {
            _columnCurrentWafer[kvp.Key] = kvp.Value;
        }

        // Update previous column assignments when column assignment changes
        if (columnAssignmentChanged)
        {
            // Update previous column assignments
            _previousColumnWafers.Clear();
            foreach (var kvp in _columnCurrentWafer)
            {
                _previousColumnWafers[kvp.Key] = kvp.Value;
            }
        }

        // Map current actions to their columns
        var columnActions = new Dictionary<string, string>();
        foreach (var (waferId, action) in _currentStepActions)
        {
            var column = DetermineColumn(action);
            if (!string.IsNullOrEmpty(column))
            {
                columnActions[column] = action;
            }
        }

        // Skip printing if no column actions (prevents blank rows)
        if (columnActions.Count == 0)
        {
            return;
        }

        // Increment step counter only when actually printing
        _globalStepCounter++;

        // Print action row with fixed column order
        Console.ForegroundColor = ConsoleColor.Blue;
        var line = new StringBuilder();

        // Always use step number prefix (no sync)
        string stepPrefix = $"Step {_globalStepCounter}";

        line.Append(stepPrefix.PadRight(10));
        foreach (var column in FixedColumns)
        {
            if (columnActions.ContainsKey(column))
            {
                line.Append(columnActions[column].PadRight(ColumnWidth));
            }
            else
            {
                line.Append("".PadRight(ColumnWidth));
            }
        }

        Console.WriteLine(line.ToString());
        Console.ResetColor();
    }

    private static string DetermineColumn(string action)
    {
        // Determine which column this action belongs to based on the 8-column layout:
        // COORD, R1(->), POLISHER, R2, CLEANER, R3, BUFFER, R1(<-)

        // COORD column: communications with coordinator (permissions, notifications)
        if (action.Contains("COORD") || action.Contains("PERMIT_") || action.Contains("FREE_"))
            return "COORD";

        // POLISHER column: at polisher station
        if (action.Contains("place on platen") || action.Contains("POLISHING") || action.Contains("REQUEST_POLISH"))
            return "POLISHER";

        // CLEANER column: at cleaner station
        if (action.Contains("place on cleaner") || action.Contains("CLEANING") || action.Contains("REQUEST_CLEAN"))
            return "CLEANER";

        // BUFFER column: at buffer station
        if (action.Contains("place on buffer") || action.Contains("BUFFERING") || action.Contains("REQUEST_BUFFER"))
            return "BUFFER";

        // R1(->) forward: R1 moving to polisher (pick from carrier, move to platen, request for p1/p4 stages)
        if (action.Contains("R-1") && (action.Contains("pick from carrier") || action.Contains("move to platen") || action.Contains("REQUEST_ROBOT_p1") || action.Contains("REQUEST_ROBOT_p4")))
            return "R1_FWD";

        // R2: R2 between polisher and cleaner (pick from platen, move to cleaner, request for p2)
        if (action.Contains("R-2") && (action.Contains("pick from platen") || action.Contains("move to cleaner") || action.Contains("REQUEST_ROBOT_p2")))
            return "R2";

        // R3: R3 between cleaner and buffer (pick from cleaner, move to buffer, request for p3)
        if (action.Contains("R-3") && (action.Contains("pick from cleaner") || action.Contains("move to buffer") || action.Contains("REQUEST_ROBOT_p3")))
            return "R3";

        // R1(<-) return: R1 returning from buffer (pick from buffer, move to carrier)
        if (action.Contains("R-1") && (action.Contains("pick from buffer") || action.Contains("move to carrier")))
            return "R1_RET";

        return "";
    }

    private static string ExtractStation(string action)
    {
        // Only extract actual station names when wafer is AT the station
        // Robot movements ("move to", "pick from") are transient - wafer stays at previous station
        // Only recognize arrival actions: "place on", processing actions, or requests

        // Check for BUFFER station arrival/processing
        if ((action.Contains("place on buffer") || action.Contains("BUFFERING") || action.Contains("REQUEST_BUFFER")))
            return "BUFFER";

        // Check for CLEANER station arrival/processing
        if ((action.Contains("place on cleaner") || action.Contains("CLEANING") || action.Contains("REQUEST_CLEAN")))
            return "CLEANER";

        // Check for PLATEN/POLISHER station arrival/processing
        if ((action.Contains("place on platen") || action.Contains("POLISHING") || action.Contains("REQUEST_POLISH")))
            return "PLATEN";

        return "";
    }


    private static void PrintCurrentState()
    {
        var activeList = _activeWafers.OrderBy(w => w).ToList();

        if (activeList.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("=".PadRight(175, '='));
        Console.WriteLine();

        // Print header
        var header = new StringBuilder();
        foreach (var waferId in activeList)
        {
            header.Append(waferId.PadRight(ColumnWidth));
        }
        Console.WriteLine(header.ToString());

        // Get max rows
        int maxRows = activeList.Select(w => _waferActions.ContainsKey(w) ? _waferActions[w].Count : 0).Max();

        // Print rows
        for (int row = 0; row < maxRows; row++)
        {
            var line = new StringBuilder();
            foreach (var waferId in activeList)
            {
                if (_waferActions.ContainsKey(waferId) && row < _waferActions[waferId].Count)
                {
                    line.Append(_waferActions[waferId][row].PadRight(ColumnWidth));
                }
                else
                {
                    line.Append("".PadRight(ColumnWidth));
                }
            }
            Console.WriteLine(line.ToString());
        }
    }

    public static void LogEvent(string eventType, string actor, string details = "", string waferId = "", string priority = "")
    {
        // Convert events to actions
        if (string.IsNullOrEmpty(waferId) && eventType != "SYSTEM_READY") return;

        string action = "";
        var waferSchId = waferId.Replace("W-", "WSCH-");

        switch (eventType)
        {
            case "INIT_STATUS":
                // Initial status report FROM subsystem TO coordinator
                action = $"[ {actor} -> COORD ] {details}";
                break;

            case "SYSTEM_READY":
                // Coordinator broadcasts all systems ready confirmation
                action = $"[ COORD -> ALL ] {details}";
                break;

            case "COMMAND_ROBOT":
                // PUSH MODEL: Coordinator commands robot to execute task
                // details format: "R-1:pick:p1"
                action = $"[ COORD ⚡ {details.Split(':')[0]} ] COMMAND: {details.Split(':')[1]} (priority {details.Split(':')[2]})";
                break;

            case "COMMAND_EQUIPMENT":
                // PUSH MODEL: Coordinator commands equipment to process wafer
                // details = equipment name (PLATEN/CLEANER/BUFFER)
                action = $"[ COORD ⚡ {details} ] COMMAND: PROCESS";
                break;

            case "SPAWN":
                // Don't log spawn, will be implicit when first action appears
                break;

            case "REQUEST_PERMISSION":
                // Permission request FROM robot scheduler TO coordinator
                action = $"[ {actor} -> COORD ] REQUEST_PERMISSION";
                break;

            case "PERMIT_RESOURCE":
                // Permission grant FROM coordinator TO robot scheduler
                action = $"[ COORD -> {actor} ] PERMIT";
                break;

            case "WAIT_RESOURCE":
                // Resource wait FROM coordinator TO robot scheduler
                action = $"[ COORD -> {actor} ] WAIT ({details})";
                break;

            case "NOTIFY_WAIT":
                // Wait notification FROM robot scheduler TO wafer scheduler
                action = $"[ {actor} -> {waferSchId} ] WAIT ({details})";
                break;

            case "DENY_RESOURCE":
                // Permission denial FROM coordinator TO robot scheduler (deprecated)
                action = $"[ COORD -> {actor} ] DENY ({details})";
                break;

            case "PERMIT_ROBOT":
                // Permission grant FROM coordinator TO wafer scheduler (deprecated - kept for compatibility)
                action = $"[ COORD -> {waferSchId} ] PERMIT_{actor}";
                break;

            case "FREE_ROBOT":
                // Permission release FROM wafer scheduler TO coordinator
                action = $"[ {waferSchId} -> COORD ] FREE_{actor}";
                break;

            case "R1_ACTION":
            case "R2_ACTION":
            case "R3_ACTION":
                // Detailed robot action
                action = $"[ {actor} -> {waferSchId} ] {details}";
                break;

            case "START_TASK":
                // Notification FROM robot TO wafer scheduler (robot available/starting)
                var eventName = $"{actor.ToUpper().Replace("-", "")}_{details.ToUpper()}_{priority.ToUpper()}";
                action = $"[ {actor} -> {waferSchId} ] {eventName}";
                break;

            case "REQUEST_ROBOT":
                // Command FROM wafer scheduler TO robot (requesting)
                var robotId = actor.Split('(')[0];
                var priorityLevel = actor.Split('(')[1].TrimEnd(')');
                action = $"[ {waferSchId} -> {robotId} ] REQUEST_ROBOT_{priorityLevel}";
                break;

            case "REQUEST_POLISH":
                action = $"[ {waferSchId} -> PLATEN ] REQUEST_POLISH";
                break;

            case "REQUEST_CLEAN":
                action = $"[ {waferSchId} -> CLEANER ] REQUEST_CLEAN";
                break;

            case "REQUEST_BUFFER":
                action = $"[ {waferSchId} -> BUFFER ] REQUEST_BUFFER";
                break;

            case "POLISHING":
                // Notification FROM station TO wafer scheduler
                action = $"[ PLATEN -> {waferSchId} ] POLISHING";
                break;

            case "POLISH_COMPLETE":
                action = $"[ PLATEN -> {waferSchId} ] POLISH_COMPLETE";
                break;

            case "CLEANING":
                action = $"[ CLEANER -> {waferSchId} ] CLEANING";
                break;

            case "CLEAN_COMPLETE":
                action = $"[ CLEANER -> {waferSchId} ] CLEAN_COMPLETE";
                break;

            case "BUFFERING":
                action = $"[ BUFFER -> {waferSchId} ] BUFFERING";
                break;

            case "BUFFER_COMPLETE":
                action = $"[ BUFFER -> {waferSchId} ] BUFFER_COMPLETE";
                break;

            case "PUSH":
                // NEW ARCHITECTURE: Coordinator pushes proceed signal to wafer
                // details format: "ProceedToNextStep:state_name"
                action = $"[ COORD → {waferId} ] PUSH: {details}";
                break;

            case "RECV_PUSH":
                // NEW ARCHITECTURE: Wafer receives proceed signal from coordinator
                // details format: "ProceedToNextStep:state_name"
                action = $"[ {waferId} ] RECV_PUSH: {details}";
                break;

            case "WAFER_CMD":
                // NEW ARCHITECTURE: Wafer commands robot/equipment
                // details format: "R-1:pick:p4" or "PLATEN:process"
                var parts = details.Split(':');
                if (parts.Length >= 2)
                {
                    var targetResource = parts[0];
                    var command = parts[1];
                    var priorityInfo = parts.Length > 2 ? $" ({parts[2]})" : "";
                    action = $"[ {waferId} → {targetResource} ] WAFER_CMD: {command}{priorityInfo}";
                }
                else
                {
                    action = $"[ {waferId} → ? ] WAFER_CMD: {details}";
                }
                break;

            case "COMPLETE":
                // Wafer completed
                CompleteWafer(waferId);
                break;
        }

        if (!string.IsNullOrEmpty(action))
        {
            // For SYSTEM_READY, use "SYSTEM" as waferId
            var logWaferId = eventType == "SYSTEM_READY" ? "SYSTEM" : waferId;
            LogWaferAction(logWaferId, action);
        }
    }
}
