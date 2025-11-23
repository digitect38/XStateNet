using Akka.Actor;
using CMPSimXS2.EventDriven.Models;
using CMPSimXS2.EventDriven.Services;
using XStateNet2.Core.Builder;
using XStateNet2.Core.Runtime;

namespace CMPSimXS2.EventDriven.Actions;

/// <summary>
/// All actions for the CMP state machine
/// Actions perform side effects and update context
/// </summary>
public static class CMPActions
{
    private static readonly CMPLogger Logger = CMPLogger.Instance;
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

        // Command sending actions (raise events to self) - Outgoing ←
        builder.WithAction("sendMoveToCarrier", SendOutgoingCommandWithLog(getSelf, "CMD_MOVE_TO_CARRIER"));
        builder.WithAction("sendMoveToPlaten", SendOutgoingCommandWithLog(getSelf, "CMD_MOVE_TO_PLATEN"));
        builder.WithAction("sendMoveToHome", SendOutgoingCommandWithLog(getSelf, "CMD_MOVE_TO_HOME"));
        builder.WithAction("sendPickWafer", SendOutgoingCommandWithLog(getSelf, "CMD_PICK_WAFER"));
        builder.WithAction("sendPlaceWafer", SendOutgoingCommandWithLog(getSelf, "CMD_PLACE_WAFER"));
        builder.WithAction("sendStartPolish", SendOutgoingCommandWithLog(getSelf, "CMD_START_POLISH"));
        builder.WithAction("sendStartProcess", SendOutgoingCommandWithLog(getSelf, "START_PROCESS"));
        builder.WithAction("sendProcessComplete", SendOutgoingCommandWithLog(getSelf, "PROCESS_COMPLETE"));
        builder.WithAction("sendProcessError", SendOutgoingCommandWithLog(getSelf, "PROCESS_ERROR"));
        builder.WithAction("sendAbortProcess", SendOutgoingCommandWithLog(getSelf, "ABORT_PROCESS"));

        // Event notification actions - Incoming →
        builder.WithAction("notifyAtHome", SendIncomingEventWithLog(getSelf, "ROBOT_AT_HOME"));
        builder.WithAction("notifyAtCarrier", SendIncomingEventWithLog(getSelf, "ROBOT_AT_CARRIER"));
        builder.WithAction("notifyAtPlaten", SendIncomingEventWithLog(getSelf, "ROBOT_AT_PLATEN"));
        builder.WithAction("notifyWaferPicked", SendIncomingEventWithLog(getSelf, "WAFER_PICKED"));
        builder.WithAction("notifyWaferPlaced", SendIncomingEventWithLog(getSelf, "WAFER_PLACED"));
        builder.WithAction("notifyPickFailed", SendIncomingEventWithLog(getSelf, "PICK_FAILED"));
        builder.WithAction("notifyPlaceFailed", SendIncomingEventWithLog(getSelf, "PLACE_FAILED"));
        builder.WithAction("notifyPlatenReady", SendIncomingEventWithLog(getSelf, "PLATEN_READY"));
        builder.WithAction("notifyPolishCompleted", SendIncomingEventWithLog(getSelf, "POLISH_COMPLETED"));
        builder.WithAction("notifyProcessError", SendIncomingEventWithLog(getSelf, "PROCESS_ERROR"));
        builder.WithAction("notifyAlarm", SendIncomingEventWithLog(getSelf, "ALARM"));

        // Status check actions - Incoming →
        builder.WithAction("checkPlatenStatus", SendIncomingEventWithLog(getSelf, "PLATEN_READY"));
        builder.WithAction("checkEquipmentState", SendIncomingEventWithLog(getSelf, "EQUIPMENT_STATE_OK"));

        // Logging actions - Equipment
        builder.WithAction("logEquipmentIdle", (ctx, data) => Logger.Info("EQ IDLE"));
        builder.WithAction("logEquipmentExecuting", (ctx, data) => Logger.Info("EQ EXECUTING"));
        builder.WithAction("logEquipmentPaused", (ctx, data) => Logger.Info("EQ PAUSED"));
        builder.WithAction("logEquipmentAlarm", (ctx, data) => Logger.Error("EQ ALARM"));
        builder.WithAction("logEquipmentAborted", (ctx, data) => Logger.Info("EQ ABORTED"));

        // Logging actions - Orchestrator
        builder.WithAction("logOrchIdle", (ctx, data) => Logger.Info("ORCH Idle"));
        builder.WithAction("logCycleComplete", LogCycleComplete);
        builder.WithAction("logAllCompleted", LogAllCompleted);
        builder.WithAction("logError", LogError);
        builder.WithAction("logRetry", LogRetry);
        builder.WithAction("logFatalError", (ctx, data) => Logger.Error("ORCH FATAL ERROR - Manual intervention required"));

        // Logging actions - Robot position
        builder.WithAction("logAtHome", (ctx, data) => Logger.Info("R-1.POS At home"));
        builder.WithAction("logMovingToCarrier", (ctx, data) => Logger.Info("R-1.POS Moving to carrier"));
        builder.WithAction("logAtCarrier", (ctx, data) => Logger.Info("R-1.POS At carrier"));
        builder.WithAction("logMovingToPlaten", (ctx, data) => Logger.Info("R-1.POS Moving to platen"));
        builder.WithAction("logAtPlaten", (ctx, data) => Logger.Info("R-1.POS At platen"));
        builder.WithAction("logMovingToHome", (ctx, data) => Logger.Info("R-1.POS Moving to home"));

        // Logging actions - Robot hand
        builder.WithAction("logHandEmpty", (ctx, data) => Logger.Info("R-1.HAND Empty"));
        builder.WithAction("logPicking", (ctx, data) => Logger.Info("R-1.HAND Picking wafer"));
        builder.WithAction("logHasWafer", (ctx, data) => Logger.Info("R-1.HAND Has wafer"));
        builder.WithAction("logPlacing", (ctx, data) => Logger.Info("R-1.HAND Placing wafer"));

        // Logging actions - Carrier
        builder.WithAction("logCarrierActive", (ctx, data) => Logger.Info("CARRIER Active"));
        builder.WithAction("logCarrierCompleted", (ctx, data) => Logger.Info("CARRIER All wafers done"));

        // Logging actions - Platen
        builder.WithAction("logPlatenEmpty", (ctx, data) => Logger.Info("PL-1 Empty"));
        builder.WithAction("logPolishingStarted", (ctx, data) => Logger.Info("PL-1 Polishing started"));
        builder.WithAction("logPlatenCompleted", (ctx, data) => Logger.Info("PL-1 Polishing completed"));
        builder.WithAction("logPlatenError", (ctx, data) => Logger.Error("PL-1 Process error"));

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
            Id = $"W-{waferNumber:D3}",
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        ctx.Set("current_wafer", wafer);
        Logger.Info($"{wafer.Id} Creating wafer #{waferNumber}");
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
        Logger.Info($"ORCH Wafer #{count + 1} completed. Remaining: {unprocessed}/25");
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
        Logger.Info($"ORCH Cycle complete: {processed}/25 processed, {unprocessed} remaining ({cycleTime}ms)");
    }

    private static void LogAllCompleted(InterpreterContext ctx, object? eventData)
    {
        var history = ctx.Get<List<WaferRecord>>("wafer_history") ?? new List<WaferRecord>();
        if (history.Count > 0)
        {
            var avgCycleTime = history.Average(w => w.CycleTime ?? 0);
            Logger.Info($"ORCH All wafers processed! Avg cycle: {avgCycleTime:F0}ms");
        }
    }

    private static void LogError(InterpreterContext ctx, object? eventData)
    {
        var errorMsg = ctx.Get<string>("error_message") ?? "Unknown error";
        var retryCount = ctx.Get<int>("retry_count");
        var maxRetries = ctx.Get<int>("max_retries");
        Logger.Error($"ORCH Error: {errorMsg} (Retry {retryCount}/{maxRetries})");
    }

    private static void LogRetry(InterpreterContext ctx, object? eventData)
    {
        var retryCount = ctx.Get<int>("retry_count");
        var maxRetries = ctx.Get<int>("max_retries");
        Logger.Info($"ORCH Retrying... ({retryCount}/{maxRetries})");
    }

    private static void GenerateReport(InterpreterContext ctx, object? eventData)
    {
        var processed = ctx.Get<int>("carrier_processed");
        var history = ctx.Get<List<WaferRecord>>("wafer_history") ?? new List<WaferRecord>();

        Logger.Info("=== PROCESS REPORT ===");
        Logger.Info($"Total wafers: {processed}");
        Logger.Info($"Wafer history: {history.Count} records");
        Logger.Info("=====================");
    }

    #endregion

    #region Helper Methods

    // Outgoing commands from orchestrator to components (←)
    private static Action<InterpreterContext, object?> SendOutgoingCommandWithLog(Func<IActorRef> getSelf, string eventType)
    {
        return (ctx, data) =>
        {
            try
            {
                Logger.Info($"ORCH ← {eventType}");
                // Small delay to ensure log is processed before event triggers next action
                System.Threading.Thread.Sleep(5);
                var actor = getSelf();
                actor.Tell(new XStateNet2.Core.Messages.SendEvent(eventType, null));
            }
            catch (Exception ex)
            {
                Logger.Error($"[ERROR] Failed to send {eventType}: {ex.Message}");
            }
        };
    }

    // Incoming events from components to orchestrator (→)
    private static Action<InterpreterContext, object?> SendIncomingEventWithLog(Func<IActorRef> getSelf, string eventType)
    {
        return (ctx, data) =>
        {
            try
            {
                Logger.Info($"ORCH → {eventType}");
                // Small delay to ensure log is processed before event triggers next action
                System.Threading.Thread.Sleep(5);
                var actor = getSelf();
                actor.Tell(new XStateNet2.Core.Messages.SendEvent(eventType, null));
            }
            catch (Exception ex)
            {
                Logger.Error($"[ERROR] Failed to send {eventType}: {ex.Message}");
            }
        };
    }

    #endregion
}
