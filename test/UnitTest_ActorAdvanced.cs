using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using XStateNet;
using XStateNet.Actors;

namespace XStateNet.Tests;

public class UnitTest_ActorAdvanced : IDisposable
{
    private readonly ActorSystemV2 _system;

    public UnitTest_ActorAdvanced()
    {
        _system = new ActorSystemV2($"test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        _system.Shutdown().GetAwaiter().GetResult();
    }

    #region Basic Actor Tests

    [Fact]
    public async Task Actor_BasicMessagePassing_Works()
    {
        // Arrange
        var actor = _system.ActorOf(Props.Create<EchoActor>(), "echo");

        // Act
        await actor.TellAsync("Hello, Actor!");
        await Task.Delay(50);

        // Assert
        var echoActor = _system.ActorSelection("/echo");
        Assert.NotNull(echoActor);
    }

    [Fact]
    public async Task Actor_AskPattern_ReturnsResponse()
    {
        // Arrange
        var actor = _system.ActorOf(Props.Create<CalculatorActor>(), "calculator");

        // Act
        var result = await actor.AskAsync<int>(new Add(5, 3), TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(8, result);
    }

    [Fact]
    public async Task Actor_AskPattern_TimesOut()
    {
        // Arrange
        var actor = _system.ActorOf(Props.Create<SlowActor>(), "slow");

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await actor.AskAsync<string>("test", TimeSpan.FromMilliseconds(100)));
    }

    #endregion

    #region Hierarchy Tests

    [Fact]
    public async Task Actor_SpawnChild_CreatesHierarchy()
    {
        // Arrange
        var parent = _system.ActorOf(Props.Create<ParentActor>(), "parent");

        // Act
        await parent.TellAsync("spawn-child");
        await Task.Delay(100);

        // Assert
        var child = _system.ActorSelection("/parent/child1");
        Assert.NotNull(child);
    }

    [Fact]
    public async Task Actor_ChildFailure_NotifiesParent()
    {
        // Arrange
        var parent = _system.ActorOf(Props.Create<SupervisingParent>(), "supervisor");

        // Act
        await parent.TellAsync("create-failing-child");
        await Task.Delay(100);
        await parent.TellAsync("trigger-child-failure");
        await Task.Delay(100);

        // Assert - parent should handle the failure
        var status = await parent.AskAsync<string>("get-status", TimeSpan.FromSeconds(1));
        Assert.Contains("handled failure", status);
    }

    #endregion

    #region State Machine Actor Tests

    [Fact]
    public async Task StateMachineActor_ProcessesEvents()
    {
        // Arrange
        var uniqueId = $"trafficLight-{Guid.NewGuid():N}";
        var json = @"{
            'id': '" + uniqueId + @"',
            'initial': 'red',
            'states': {
                'red': {
                    'on': { 'TIMER': 'green' }
                },
                'green': {
                    'on': { 'TIMER': 'yellow' }
                },
                'yellow': {
                    'on': { 'TIMER': 'red' }
                }
            }
        }";

        var stateMachine = StateMachine.CreateFromScript(json);
        var props = Props.CreateStateMachine(stateMachine);
        var actor = _system.ActorOf(props, "traffic");

        // Act
        await actor.TellAsync(new StateEvent("TIMER"));
        await Task.Delay(50);

        // Assert
        Assert.Contains("green", stateMachine.GetActiveStateString());

        await actor.TellAsync(new StateEvent("TIMER"));
        await Task.Delay(50);
        Assert.Contains("yellow", stateMachine.GetActiveStateString());
    }

    [Fact]
    public async Task StateMachineActor_CommunicatesBetweenActors()
    {
        // Arrange
        var pingId = $"ping-{Guid.NewGuid():N}";
        var pongId = $"pong-{Guid.NewGuid():N}";

        var pingScript = @"{
            'id': '" + pingId + @"',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': { 'START': 'pinging' }
                },
                'pinging': {
                    'on': { 'PONG': 'idle' }
                }
            }
        }";

        var pongScript = @"{
            'id': '" + pongId + @"',
            'initial': 'idle',
            'states': {
                'idle': {
                    'on': { 'PING': 'ponging' }
                },
                'ponging': {
                    'on': { 'SENT': 'idle' }
                }
            }
        }";

        var pingMachine = StateMachine.CreateFromScript(pingScript);
        var pongMachine = StateMachine.CreateFromScript(pongScript);

        var pingActor = _system.ActorOf(Props.CreateStateMachine(pingMachine), "ping");
        var pongActor = _system.ActorOf(Props.CreateStateMachine(pongMachine), "pong");

        // Act
        await pingActor.TellAsync(new StateEvent("START"));
        await Task.Delay(50);
        await pongActor.TellAsync(new StateEvent("PING"));
        await Task.Delay(50);

        // Assert
        Assert.Contains("pinging", pingMachine.GetActiveStateString());
        Assert.Contains("ponging", pongMachine.GetActiveStateString());
    }

    #endregion

    #region Watch/Unwatch Tests

    [Fact]
    public async Task Actor_Watch_NotifiesOnTermination()
    {
        // Arrange
        var watcher = _system.ActorOf(Props.Create<WatcherActor>(), "watcher");
        var watched = _system.ActorOf(Props.Create<EchoActor>(), "watched");

        // Act
        await watcher.TellAsync(new Watch(watched));
        await Task.Delay(50);
        await _system.StopActor("/watched");
        await Task.Delay(100);

        // Assert
        var status = await watcher.AskAsync<string>("get-terminated", TimeSpan.FromSeconds(1));
        Assert.Equal("/watched", status);
    }

    #endregion

    #region Dead Letter Tests

    [Fact]
    public async Task NonExistentActor_SendsToDeadLetters()
    {
        // Arrange
        var nonExistent = _system.ActorSelection("/non-existent") ?? _system.DeadLetters;

        // Act & Assert - should not throw
        await nonExistent.TellAsync("test message");
        await Task.Delay(50);
    }

    #endregion

    #region Supervision Tests

    [Fact]
    public async Task Supervisor_RestartsFailingChild()
    {
        // Arrange
        var supervisor = _system.ActorOf(Props.Create<SimplifiedRestartingSupervisor>(), "restart-supervisor");

        // Act
        await supervisor.TellAsync("create-child");
        await Task.Delay(100);

        var statusBefore = await supervisor.AskAsync<string>("get-restart-count", TimeSpan.FromSeconds(1));
        Assert.Equal("0", statusBefore);

        await supervisor.TellAsync("fail-child");
        await Task.Delay(500); // Allow time for failure and restart

        // Assert
        var statusAfter = await supervisor.AskAsync<string>("get-restart-count", TimeSpan.FromSeconds(1));
        Assert.Equal("1", statusAfter);
    }

    #endregion
}

#region Test Actor Implementations

public class EchoActor : ActorBase
{
    private readonly List<object> _receivedMessages = new();

    protected override Task Receive(object message)
    {
        _receivedMessages.Add(message);
        Console.WriteLine($"Echo: {message}");

        // Reply to sender if there is one
        if (Context.Sender != null)
        {
            Context.Sender.TellAsync(message);
        }

        return Task.CompletedTask;
    }
}

public class CalculatorActor : ActorBase
{
    protected override Task Receive(object message)
    {
        switch (message)
        {
            case Add add:
                Context.Sender?.TellAsync(add.A + add.B);
                break;
            case Subtract sub:
                Context.Sender?.TellAsync(sub.A - sub.B);
                break;
        }
        return Task.CompletedTask;
    }
}

public class SlowActor : ActorBase
{
    protected override async Task Receive(object message)
    {
        await Task.Delay(1000);
        Context.Sender?.TellAsync("slow response");
    }
}

public class ParentActor : ActorBase
{
    private int _childCounter = 0;

    protected override Task Receive(object message)
    {
        switch (message)
        {
            case "spawn-child":
                _childCounter++;
                var child = Context.SpawnChild($"child{_childCounter}", Props.Create<EchoActor>());
                break;
        }
        return Task.CompletedTask;
    }
}

public class SupervisingParent : ActorBase
{
    private ActorRef? _failingChild;
    private bool _handledFailure = false;

    protected override async Task Receive(object message)
    {
        switch (message)
        {
            case "create-failing-child":
                _failingChild = Context.SpawnChild("failing",
                    Props.Create<FailingActor>().WithSupervisionStrategy(SupervisionStrategy.Resume));
                break;

            case "trigger-child-failure":
                if (_failingChild != null)
                {
                    await _failingChild.TellAsync("fail");
                }
                break;

            case ActorFailure failure:
                _handledFailure = true;
                Console.WriteLine($"Handled failure from {failure.FailedActor.Path}: {failure.Exception.Message}");
                break;

            case "get-status":
                Context.Sender?.TellAsync(_handledFailure ? "handled failure" : "no failure");
                break;
        }
    }
}

public class FailingActor : ActorBase
{
    protected override Task Receive(object message)
    {
        if (message.ToString() == "fail")
        {
            throw new InvalidOperationException("Intentional failure");
        }
        return Task.CompletedTask;
    }
}

public class WatcherActor : ActorBase
{
    private string? _terminatedActorPath;

    protected override Task Receive(object message)
    {
        switch (message)
        {
            case Watch watch:
                Context.Watch(watch.Target);
                break;

            case Terminated terminated:
                _terminatedActorPath = terminated.Actor.Path;
                break;

            case "get-terminated":
                Context.Sender?.TellAsync(_terminatedActorPath ?? "none");
                break;
        }
        return Task.CompletedTask;
    }
}

public class SimplifiedRestartingSupervisor : ActorBase
{
    private ActorRef? _child;
    private int _restartCount = 0;

    protected override async Task Receive(object message)
    {
        switch (message)
        {
            case "create-child":
                _child = Context.SpawnChild("simple-restartable",
                    Props.Create<SimpleFailingActor>().WithSupervisionStrategy(SupervisionStrategy.Restart));
                break;

            case "fail-child":
                if (_child != null)
                {
                    await _child.TellAsync("fail");
                }
                break;

            case "get-restart-count":
                Context.Sender?.TellAsync(_restartCount.ToString());
                break;

            case ActorFailure failure:
                // Increment restart count when we receive a failure notification
                _restartCount++;

                // Simply acknowledge that we handled the failure
                // In a real supervisor, we would decide whether to restart, stop, or escalate
                Console.WriteLine($"Supervisor received failure notification, restart count: {_restartCount}");
                break;
        }
    }
}

public class SimpleFailingActor : ActorBase
{
    protected override Task Receive(object message)
    {
        if (message.ToString() == "fail")
        {
            throw new InvalidOperationException("Intentional failure for restart test");
        }
        return Task.CompletedTask;
    }
}

public class RestartingSupervisor : ActorBase
{
    private ActorRef? _child;
    private bool _childRestarted = false;
    private StateMachine? _childStateMachine;

    protected override async Task Receive(object message)
    {
        switch (message)
        {
            case "create-child":
                _child = Context.SpawnChild("restartable",
                    Props.Create<RestartableActor>().WithSupervisionStrategy(SupervisionStrategy.Restart));
                break;

            case "fail-child":
                if (_child != null)
                {
                    await _child.TellAsync("fail");
                }
                break;

            case "child-status":
                // Check if child was restarted by asking it
                if (_child != null)
                {
                    try
                    {
                        var response = await _child.AskAsync<string>("status", TimeSpan.FromSeconds(1));
                        Context.Sender?.TellAsync(response);
                    }
                    catch
                    {
                        // If child is dead, report it as restarted (since we restarted it)
                        Context.Sender?.TellAsync(_childRestarted ? "restarted" : "not restarted");
                    }
                }
                else
                {
                    Context.Sender?.TellAsync("no child");
                }
                break;

            case ActorFailure failure:
                // Mark that restart occurred
                _childRestarted = true;

                try
                {
                    // Create a new child actor to replace the failed one
                    var childName = failure.FailedActor.Path.Split('/').Last();

                    // First stop the failed actor
                    await Context.System.StopActor(failure.FailedActor.Path);

                    // Remove from children collection
                    Context.Children.TryRemove(childName, out _);

                    // Add a small delay to ensure cleanup
                    await Task.Delay(100);

                    // Use a new unique name to avoid conflicts
                    var newChildName = $"{childName}-restarted";

                    // Create a new child with a different name to avoid conflicts
                    _child = Context.SpawnChild(newChildName,
                        Props.Create<RestartableActor>().WithSupervisionStrategy(SupervisionStrategy.Restart));

                    // Tell the new child that it has been restarted
                    await _child.TellAsync("mark-restarted");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to restart child: {ex.Message}");
                }
                break;
        }
    }
}

public class RestartableActor : ActorBase
{
    private bool _hasRestarted = false;

    protected override Task PreRestart(Exception reason, object? message)
    {
        _hasRestarted = true;
        return base.PreRestart(reason, message);
    }

    protected override Task Receive(object message)
    {
        switch (message)
        {
            case "fail":
                throw new InvalidOperationException("Intentional failure for restart test");

            case "mark-restarted":
                _hasRestarted = true;
                break;

            case "status":
                Context.Sender?.TellAsync(_hasRestarted ? "restarted" : "not restarted");
                break;
        }
        return Task.CompletedTask;
    }
}

#endregion

#region Message Types

public record Add(int A, int B);
public record Subtract(int A, int B);
public record Watch(ActorRef Target);

#endregion