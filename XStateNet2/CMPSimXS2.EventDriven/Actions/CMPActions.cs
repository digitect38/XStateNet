using Akka.Actor;
using CMPSimXS2.EventDriven.Models;
using XStateNet2.Core.Builder;
using XStateNet2.Core.Runtime;

namespace CMPSimXS2.EventDriven.Actions;

/// <summary>
/// All actions for the CMP state machine
/// Actions perform side effects and update context
/// </summary>
public static class CMPActions
{
    public static void RegisterAll(MachineBuilder builder, Func<IActorRef> getSelf)
    {
        // Wafer tracking actions
        builder.WithAction("createWaferRecord", CreateWaferRecord);
        builder.WithAction("updateWaferPickTime", UpdateWaferPickTime);
        builder.WithAction("updateWaferLoadTime", UpdateWaferLoadTime);
        builder.WithAction("updateWaferProcessTime", UpdateWaferProcessTime);
        builder.WithAction("updateWaferUnloadTime", UpdateWaferUnloadTime);
        builder.WithAction("finalizeWaferRecord", FinalizeWaferRecord);

        // Resource locking actions
        builder.WithAction("lockRobot", LockRobot);
        builder.WithAction("unlockRobot", UnlockRobot);
        builder.WithAction("lockPlaten", LockPlaten);
        builder.WithAction("unlockPlaten", UnlockPlaten);
        builder.WithAction("unlockAllResources", UnlockAllResources);

        // Context update actions
        builder.WithAction("decrementUnprocessed", DecrementUnprocessed);
        builder.WithAction("incrementProcessed", IncrementProcessed);

        // Error handling actions
        builder.WithAction("incrementRetryCount", IncrementRetryCount);
        builder.WithAction("resetRetryCount", ResetRetryCount);
        builder.WithAction("setTimeoutError", SetTimeoutError);
        builder.WithAction("resetSystem", ResetSystem);

        // Command sending actions (raise events to self)
        builder.WithAction("sendMoveToCarrier", (ctx, data) => SendCommand(getSelf(), "CMD_MOVE_TO_CARRIER"));
        builder.WithAction("sendMoveToPlaten", (ctx, data) => SendCommand(getSelf(), "CMD_MOVE_TO_PLATEN"));
        builder.WithAction("sendMoveToHome", (ctx, data) => SendCommand(getSelf(), "CMD_MOVE_TO_HOME"));
        builder.WithAction("sendPickWafer", (ctx, data) => SendCommand(getSelf(), "CMD_PICK_WAFER"));
        builder.WithAction("sendPlaceWafer", (ctx, data) => SendCommand(getSelf(), "CMD_PLACE_WAFER"));
        builder.WithAction("sendStartPolish", (ctx, data) => SendCommand(getSelf(), "CMD_START_POLISH"));
        builder.WithAction("sendStartProcess", (ctx, data) => SendCommand(getSelf(), "START_PROCESS"));
        builder.WithAction("sendProcessComplete", (ctx, data) => SendCommand(getSelf(), "PROCESS_COMPLETE"));
        builder.WithAction("sendProcessError", (ctx, data) => SendCommand(getSelf(), "PROCESS_ERROR"));
        builder.WithAction("sendAbortProcess", (ctx, data) => SendCommand(getSelf(), "ABORT_PROCESS"));

        // Event notification actions
        builder.WithAction("notifyAtHome", (ctx, data) => SendCommand(getSelf(), "ROBOT_AT_HOME"));
        builder.WithAction("notifyAtCarrier", (ctx, data) => SendCommand(getSelf(), "ROBOT_AT_CARRIER"));
        builder.WithAction("notifyAtPlaten", (ctx, data) => SendCommand(getSelf(), "ROBOT_AT_PLATEN"));
        builder.WithAction("notifyWaferPicked", (ctx, data) => SendCommand(getSelf(), "WAFER_PICKED"));
        builder.WithAction("notifyWaferPlaced", (ctx, data) => SendCommand(getSelf(), "WAFER_PLACED"));
        builder.WithAction("notifyPickFailed", (ctx, data) => SendCommand(getSelf(), "PICK_FAILED"));
        builder.WithAction("notifyPlaceFailed", (ctx, data) => SendCommand(getSelf(), "PLACE_FAILED"));
        builder.WithAction("notifyPlatenReady", (ctx, data) => SendCommand(getSelf(), "PLATEN_READY"));
        builder.WithAction("notifyPolishCompleted", (ctx, data) => SendCommand(getSelf(), "POLISH_COMPLETED"));
        builder.WithAction("notifyProcessError", (ctx, data) => SendCommand(getSelf(), "PROCESS_ERROR"));
        builder.WithAction("notifyAlarm", (ctx, data) => Console.Error.WriteLine("[ALARM] Equipment alarm triggered"));

        // Status check actions
        builder.WithAction("checkPlatenStatus", (ctx, data) => SendCommand(getSelf(), "PLATEN_READY"));
        builder.WithAction("checkEquipmentState", (ctx, data) => Console.WriteLine("[CHECK] Equipment state verified"));

        // Logging actions - Equipment
        builder.WithAction("logEquipmentIdle", (ctx, data) => Console.WriteLine("[EQUIP] IDLE"));
        builder.WithAction("logEquipmentExecuting", (ctx, data) => Console.WriteLine("[EQUIP] EXECUTING"));
        builder.WithAction("logEquipmentPaused", (ctx, data) => Console.WriteLine("[EQUIP] PAUSED"));
        builder.WithAction("logEquipmentAlarm", (ctx, data) => Console.Error.WriteLine("[EQUIP] ALARM"));
        builder.WithAction("logEquipmentAborted", (ctx, data) => Console.WriteLine("[EQUIP] ABORTED"));

        // Logging actions - Orchestrator
        builder.WithAction("logOrchIdle", (ctx, data) => Console.WriteLine("[ORCH] Idle"));
        builder.WithAction("logCycleComplete", LogCycleComplete);
        builder.WithAction("logAllCompleted", LogAllCompleted);
        builder.WithAction("logError", LogError);
        builder.WithAction("logRetry", LogRetry);
        builder.WithAction("logFatalError", (ctx, data) => Console.Error.WriteLine("[ORCH] FATAL ERROR - Manual intervention required"));

        // Logging actions - Robot position
        builder.WithAction("logAtHome", (ctx, data) => Console.WriteLine("[ROBOT] At home"));
        builder.WithAction("logMovingToCarrier", (ctx, data) => Console.WriteLine("[ROBOT] Moving to carrier"));
        builder.WithAction("logAtCarrier", (ctx, data) => Console.WriteLine("[ROBOT] At carrier"));
        builder.WithAction("logMovingToPlaten", (ctx, data) => Console.WriteLine("[ROBOT] Moving to platen"));
        builder.WithAction("logAtPlaten", (ctx, data) => Console.WriteLine("[ROBOT] At platen"));
        builder.WithAction("logMovingToHome", (ctx, data) => Console.WriteLine("[ROBOT] Moving to home"));

        // Logging actions - Robot hand
        builder.WithAction("logHandEmpty", (ctx, data) => Console.WriteLine("[HAND] Empty"));
        builder.WithAction("logPicking", (ctx, data) => Console.WriteLine("[HAND] Picking wafer"));
        builder.WithAction("logHasWafer", (ctx, data) => Console.WriteLine("[HAND] Has wafer"));
        builder.WithAction("logPlacing", (ctx, data) => Console.WriteLine("[HAND] Placing wafer"));

        // Logging actions - Carrier
        builder.WithAction("logCarrierActive", (ctx, data) => Console.WriteLine("[CARRIER] Active"));
        builder.WithAction("logCarrierCompleted", (ctx, data) => Console.WriteLine("[CARRIER] All wafers done"));

        // Logging actions - Platen
        builder.WithAction("logPlatenEmpty", (ctx, data) => Console.WriteLine("[PLATEN] Empty"));
        builder.WithAction("logPolishingStarted", (ctx, data) => Console.WriteLine("[PLATEN] Polishing started"));
        builder.WithAction("logPlatenCompleted", (ctx, data) => Console.WriteLine("[PLATEN] Polishing completed"));
        builder.WithAction("logPlatenError", (ctx, data) => Console.Error.WriteLine("[PLATEN] Process error"));

        // Report generation
        builder.WithAction("generateReport", GenerateReport);
    }

    #region Wafer Tracking Actions

    private static void CreateWaferRecord(InterpreterContext ctx, object? eventData)
    {
        var carrierUnprocessed = ctx.Get<int>("carrier_unprocessed");
        var waferNumber = 26 - carrierUnprocessed;

        var wafer = new WaferRecord
        {
            Id = $"W{waferNumber:D3}",
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        ctx.Set("current_wafer", wafer);
        Console.WriteLine($"[WAFER] Starting wafer #{waferNumber} ({wafer.Id})");
    }

    private static void UpdateWaferPickTime(InterpreterContext ctx, object? eventData)
    {
        var wafer = ctx.Get<WaferRecord>("current_wafer");
        if (wafer != null)
        {
            wafer.PickTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ctx.Set("current_wafer", wafer);
        }
    }

    private static void UpdateWaferLoadTime(InterpreterContext ctx, object? eventData)
    {
        var wafer = ctx.Get<WaferRecord>("current_wafer");
        if (wafer != null)
        {
            wafer.LoadTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ctx.Set("current_wafer", wafer);
        }
    }

    private static void UpdateWaferProcessTime(InterpreterContext ctx, object? eventData)
    {
        var wafer = ctx.Get<WaferRecord>("current_wafer");
        if (wafer != null)
        {
            wafer.ProcessTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ctx.Set("current_wafer", wafer);
        }
    }

    private static void UpdateWaferUnloadTime(InterpreterContext ctx, object? eventData)
    {
        var wafer = ctx.Get<WaferRecord>("current_wafer");
        if (wafer != null)
        {
            wafer.UnloadTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ctx.Set("current_wafer", wafer);
        }
    }

    private static void FinalizeWaferRecord(InterpreterContext ctx, object? eventData)
    {
        var wafer = ctx.Get<WaferRecord>("current_wafer");
        if (wafer != null)
        {
            wafer.CompleteTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            wafer.CycleTime = wafer.CompleteTime - wafer.StartTime;

            var history = ctx.Get<List<WaferRecord>>("wafer_history") ?? new List<WaferRecord>();
            history.Add(wafer);
            ctx.Set("wafer_history", history);
            ctx.Set("current_wafer", null);
        }
    }

    #endregion

    #region Resource Locking Actions

    private static void LockRobot(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("robot_busy", true);
    }

    private static void UnlockRobot(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("robot_busy", false);
    }

    private static void LockPlaten(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("platen_locked", true);
    }

    private static void UnlockPlaten(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("platen_locked", false);
    }

    private static void UnlockAllResources(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("robot_busy", false);
        ctx.Set("platen_locked", false);
    }

    #endregion

    #region Context Update Actions

    private static void DecrementUnprocessed(InterpreterContext ctx, object? eventData)
    {
        var count = ctx.Get<int>("carrier_unprocessed");
        ctx.Set("carrier_unprocessed", count - 1);
    }

    private static void IncrementProcessed(InterpreterContext ctx, object? eventData)
    {
        var count = ctx.Get<int>("carrier_processed");
        ctx.Set("carrier_processed", count + 1);
        var unprocessed = ctx.Get<int>("carrier_unprocessed");
        Console.WriteLine($"[PROGRESS] Wafer #{count + 1} completed. Remaining: {unprocessed}/25");
    }

    #endregion

    #region Error Handling Actions

    private static void IncrementRetryCount(InterpreterContext ctx, object? eventData)
    {
        var count = ctx.Get<int>("retry_count");
        ctx.Set("retry_count", count + 1);
    }

    private static void ResetRetryCount(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("retry_count", 0);
    }

    private static void SetTimeoutError(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("error_code", "TIMEOUT");
        ctx.Set("error_message", "Operation timeout exceeded");
    }

    private static void ResetSystem(InterpreterContext ctx, object? eventData)
    {
        ctx.Set("retry_count", 0);
        ctx.Set("error_code", null);
        ctx.Set("error_message", null);
        ctx.Set("robot_busy", false);
        ctx.Set("platen_locked", false);
    }

    #endregion

    #region Logging Actions

    private static void LogCycleComplete(InterpreterContext ctx, object? eventData)
    {
        var processed = ctx.Get<int>("carrier_processed");
        var unprocessed = ctx.Get<int>("carrier_unprocessed");
        var wafer = ctx.Get<WaferRecord>("current_wafer");
        var cycleTime = wafer?.CycleTime ?? 0;
        Console.WriteLine($"[ORCH] Cycle complete: {processed}/25 processed, {unprocessed} remaining ({cycleTime}ms)");
    }

    private static void LogAllCompleted(InterpreterContext ctx, object? eventData)
    {
        var history = ctx.Get<List<WaferRecord>>("wafer_history") ?? new List<WaferRecord>();
        if (history.Count > 0)
        {
            var avgCycleTime = history.Average(w => w.CycleTime ?? 0);
            Console.WriteLine($"[ORCH] All wafers processed! Avg cycle: {avgCycleTime:F0}ms");
        }
    }

    private static void LogError(InterpreterContext ctx, object? eventData)
    {
        var errorMsg = ctx.Get<string>("error_message") ?? "Unknown error";
        var retryCount = ctx.Get<int>("retry_count");
        var maxRetries = ctx.Get<int>("max_retries");
        Console.Error.WriteLine($"[ORCH] Error: {errorMsg} (Retry {retryCount}/{maxRetries})");
    }

    private static void LogRetry(InterpreterContext ctx, object? eventData)
    {
        var retryCount = ctx.Get<int>("retry_count");
        var maxRetries = ctx.Get<int>("max_retries");
        Console.WriteLine($"[ORCH] Retrying... ({retryCount}/{maxRetries})");
    }

    private static void GenerateReport(InterpreterContext ctx, object? eventData)
    {
        var processed = ctx.Get<int>("carrier_processed");
        var history = ctx.Get<List<WaferRecord>>("wafer_history") ?? new List<WaferRecord>();

        Console.WriteLine("=== PROCESS REPORT ===");
        Console.WriteLine($"Total wafers: {processed}");
        Console.WriteLine($"Wafer history: {history.Count} records");
        Console.WriteLine("=====================");
    }

    #endregion

    #region Helper Methods

    private static void SendCommand(IActorRef self, string eventType)
    {
        self.Tell(new XStateNet2.Core.Messages.SendEvent(eventType, null));
    }

    #endregion
}
