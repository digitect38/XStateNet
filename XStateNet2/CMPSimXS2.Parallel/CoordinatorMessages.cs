namespace CMPSimXS2.Parallel;

// ===== PUSH MODEL: Coordinator Commands Resources =====

/// <summary>
/// Coordinator commands robot to execute task for wafer
/// Push model: coordinator decides when to execute based on availability
/// </summary>
public record ExecuteRobotTask(string RobotId, string Task, string WaferId, int Priority);

/// <summary>
/// Coordinator commands equipment to process wafer
/// Push model: coordinator decides when to start processing
/// </summary>
public record ExecuteEquipmentTask(string EquipmentId, string WaferId);

/// <summary>
/// Robot/Equipment reports task completion back to coordinator
/// Coordinator then updates availability and schedules next wafer
/// </summary>
public record TaskCompleted(string ResourceId, string WaferId);

/// <summary>
/// Resource reports it is now available (idle)
/// Coordinator updates bitmask and checks for waiting wafers
/// </summary>
public record ResourceAvailable(string ResourceId);

/// <summary>
/// Resource reports it is now busy (processing)
/// Coordinator updates bitmask
/// </summary>
public record ResourceBusy(string ResourceId, string WaferId);

// ===== Wafer State Updates to Coordinator =====

/// <summary>
/// Wafer reports its current state and guard conditions to coordinator
/// Coordinator uses this to determine next processing step
/// </summary>
public record WaferStateUpdate(string WaferId, string State, GuardConditions Conditions);

/// <summary>
/// Coordinator evaluates all wafers and available resources
/// Synchronized scheduling: finds optimal wafer-resource matches
/// </summary>
public record EvaluateScheduling();

// ===== Coordinator to Wafer Commands (NEW ARCHITECTURE) =====

/// <summary>
/// NEW: Coordinator tells wafer it can proceed to next step
/// Wafer decides HOW to proceed (which robot to command, etc.)
/// This gives wafer autonomy over its workflow
/// </summary>
public record ProceedToNextStep(string WaferId);

/// <summary>
/// Coordinator grants resource permission to wafer
/// Wafer can then command the resource directly
/// </summary>
public record ResourcePermissionGranted(string ResourceType, string WaferId);

/// <summary>
/// Coordinator denies resource permission (resource busy)
/// Includes reason for denial
/// </summary>
public record ResourcePermissionDenied(string ResourceType, string WaferId, string Reason);

/// <summary>
/// Wafer requests permission to use a resource
/// Coordinator evaluates availability and grants/denies
/// </summary>
public record RequestResourcePermission(string ResourceType, string WaferId);

/// <summary>
/// Wafer releases a resource when done
/// Coordinator updates availability tracking
/// </summary>
public record ReleaseResource(string ResourceType, string WaferId);

// ProcessingComplete is now defined in Program.cs as ProcessingComplete(string StationId, string WaferId)
