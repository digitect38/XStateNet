using Akka.Actor;

public record StartMsg;
public record PingMsg;
public record PongMsg;
public record StopMsg;

/// <summary>
/// Pure Akka.NET actor without state machine overhead for baseline comparison
/// </summary>
public class PurePingActor : ReceiveActor
{
    private readonly IActorRef _pongActor;
    private readonly int _maxCount;
    private int _count = 0;
    private DateTime _startTime;
    private readonly Action<int, int, TimeSpan> _onComplete;

    public PurePingActor(IActorRef pongActor, int maxCount, Action<int, int, TimeSpan> onComplete)
    {
        _pongActor = pongActor;
        _maxCount = maxCount;
        _onComplete = onComplete;

        Receive<StartMsg>(_ =>
        {
            _startTime = DateTime.UtcNow;
            _count++;
            _pongActor.Tell(new PingMsg());
        });

        Receive<PongMsg>(_ =>
        {
            _count++;

            if (_count >= _maxCount)
            {
                var elapsed = DateTime.UtcNow - _startTime;
                _onComplete(_count, _maxCount, elapsed);
                _pongActor.Tell(new StopMsg());
            }
            else
            {
                _pongActor.Tell(new PingMsg());
            }
        });
    }
}

public class PurePongActor : ReceiveActor
{
    private int _count = 0;

    public PurePongActor()
    {
        Receive<PingMsg>(_ =>
        {
            _count++;
            Sender.Tell(new PongMsg());
        });

        Receive<StopMsg>(_ =>
        {
            // Do nothing, just stop receiving
        });
    }
}
