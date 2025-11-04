using Akka.Actor;
using XStateNet2.Core.Builder;
using XStateNet2.Core.Parser;

namespace XStateNet2.Core.Factory;

/// <summary>
/// Factory for creating state machines from XState JSON
/// </summary>
public class XStateMachineFactory
{
    private readonly ActorSystem _actorSystem;
    private readonly XStateParser _parser;

    public XStateMachineFactory(ActorSystem actorSystem)
    {
        _actorSystem = actorSystem ?? throw new ArgumentNullException(nameof(actorSystem));
        _parser = new XStateParser();
    }

    /// <summary>
    /// Create a machine builder from JSON string
    /// </summary>
    public MachineBuilder FromJson(string json)
    {
        var script = _parser.Parse(json);
        return new MachineBuilder(script, _actorSystem, json);
    }

    /// <summary>
    /// Create a machine builder from JSON file
    /// </summary>
    public MachineBuilder FromFile(string filePath)
    {
        var script = _parser.ParseFile(filePath);
        return new MachineBuilder(script, _actorSystem);
    }
}
