using CMPSimXS2.EventDriven.Models;
using XStateNet2.Core.Runtime;

namespace CMPSimXS2.EventDriven.Guards;

/// <summary>
/// All guard conditions for the CMP state machine
/// Guards determine when transitions can occur
/// </summary>
public static class CMPGuards
{
    public static void RegisterAll(InterpreterContext context)
    {
        // Main workflow guards
        context.RegisterGuard("canStartCycle", CanStartCycle);
        context.RegisterGuard("allWafersProcessed", AllWafersProcessed);
        context.RegisterGuard("canRetry", CanRetry);
        context.RegisterGuard("robotAtCarrier", RobotAtCarrier);

        // Resource guards
        context.RegisterGuard("robotNotBusy", RobotNotBusy);
        context.RegisterGuard("platenNotLocked", PlatenNotLocked);
        context.RegisterGuard("cleanerNotLocked", CleanerNotLocked);
        context.RegisterGuard("bufferNotLocked", BufferNotLocked);

        // Success simulation guards
        context.RegisterGuard("pickSuccessful", PickSuccessful);
        context.RegisterGuard("placeSuccessful", PlaceSuccessful);
        context.RegisterGuard("polishSuccessful", PolishSuccessful);
        context.RegisterGuard("cleanSuccessful", CleanSuccessful);
        context.RegisterGuard("bufferSuccessful", BufferSuccessful);
    }

    public static bool CanStartCycle(InterpreterContext ctx, object? eventData)
    {
        var carrierUnprocessed = ctx.Get<int>("carrier_unprocessed");
        var robotBusy = ctx.Get<bool>("robot_busy");

        // In the JavaScript version, this also checks equipment_state === 'IDLE'
        // For simplicity, we'll check if there are wafers and robot is available
        return carrierUnprocessed > 0 && !robotBusy;
    }

    public static bool AllWafersProcessed(InterpreterContext ctx, object? eventData)
    {
        var carrierUnprocessed = ctx.Get<int>("carrier_unprocessed");
        var carrierProcessed = ctx.Get<int>("carrier_processed");

        return carrierUnprocessed == 0 && carrierProcessed == 25;
    }

    public static bool CanRetry(InterpreterContext ctx, object? eventData)
    {
        var retryCount = ctx.Get<int>("retry_count");
        var maxRetries = ctx.Get<int>("max_retries");

        return retryCount < maxRetries;
    }

    public static bool RobotAtCarrier(InterpreterContext ctx, object? eventData)
    {
        // This would need to check robot position state, but since we can't easily access
        // parallel state machine states, we'll return false for now to always move to carrier
        return false;
    }

    public static bool RobotNotBusy(InterpreterContext ctx, object? eventData)
    {
        var robotBusy = ctx.Get<bool>("robot_busy");
        var result = !robotBusy;
        Console.WriteLine($"[GUARD] robotNotBusy: busy={robotBusy}, result={result}");
        return result;
    }

    public static bool PlatenNotLocked(InterpreterContext ctx, object? eventData)
    {
        return !ctx.Get<bool>("platen_locked");
    }

    // Simulation guards (simulate success rates)
    public static bool PickSuccessful(InterpreterContext ctx, object? eventData)
    {
        return true;
        // 95% success rate
        //return Random.Shared.NextDouble() > 0.05;
    }

    public static bool PlaceSuccessful(InterpreterContext ctx, object? eventData)
    {
        return true;
        // 95% success rate
        //return Random.Shared.NextDouble() > 0.05;
    }

    public static bool PolishSuccessful(InterpreterContext ctx, object? eventData)
    {
        return true;
        // 98% success rate
        //return Random.Shared.NextDouble() > 0.02;
    }

    public static bool CleanerNotLocked(InterpreterContext ctx, object? eventData)
    {
        return !ctx.Get<bool>("cleaner_locked");
    }

    public static bool BufferNotLocked(InterpreterContext ctx, object? eventData)
    {
        return !ctx.Get<bool>("buffer_locked");
    }

    public static bool CleanSuccessful(InterpreterContext ctx, object? eventData)
    {
        return true;
        // 98% success rate
        //return Random.Shared.NextDouble() > 0.02;
    }

    public static bool BufferSuccessful(InterpreterContext ctx, object? eventData)
    {
        return true;
        // 99% success rate
        //return Random.Shared.NextDouble() > 0.01;
    }
}
