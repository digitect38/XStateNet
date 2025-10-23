using Akka.Actor;
using Akka.Event;
using XStateNet2.Core.Messages;
using XStateNet2.Core.Runtime;

namespace XStateNet2.Core.Actors;

/// <summary>
/// Service actor - executes invoked services and reports results
/// </summary>
public class ServiceActor : ReceiveActor
{
    private readonly string _serviceName;
    private readonly InterpreterContext _context;
    private readonly ILoggingAdapter _log;

    public ServiceActor(string serviceName, InterpreterContext context)
    {
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _log = Context.GetLogger();

        ReceiveAsync<StartService>(HandleStartService);
    }

    private async Task HandleStartService(StartService msg)
    {
        _log.Debug($"[ServiceActor] Starting service '{_serviceName}'");

        try
        {
            var result = await _context.InvokeService(_serviceName);

            _log.Debug($"[ServiceActor] Service '{_serviceName}' completed successfully");

            // Send result back to parent
            Context.Parent.Tell(new ServiceDone(msg.ServiceId, result));

            // Stop self
            Context.Stop(Self);
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"[ServiceActor] Service '{_serviceName}' failed");

            // Send error back to parent
            Context.Parent.Tell(new ServiceError(msg.ServiceId, ex));

            // Stop self
            Context.Stop(Self);
        }
    }

    protected override void PreStart()
    {
        _log.Debug($"[ServiceActor] Service actor '{_serviceName}' started");
    }

    protected override void PostStop()
    {
        _log.Debug($"[ServiceActor] Service actor '{_serviceName}' stopped");
    }
}

/// <summary>
/// Start service message
/// </summary>
public record StartService(string ServiceId);
