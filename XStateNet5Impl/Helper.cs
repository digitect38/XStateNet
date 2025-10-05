namespace XStateNet;

public static class Helper
{
    public static void WriteLine(int indent, string msg)
    {
        for (int i = 0; i < indent; i++)
        {
            Console.Write("  ");
        }
        Logger.Debug(msg);
    }

    public static string ToCsvString(this IEnumerable<StateNode> collection, string separator = ";")
    {
        if (collection == null)
            throw new ArgumentNullException(nameof(collection));

        return string.Join(separator, collection.Select(state => state?.Name ?? "<null>"));
    }

    public static string ToCsvString(this IEnumerable<string> collection, StateMachine stateMachine, bool leafOnly = true, string separator = ";")
    {
        if (collection == null)
            throw new ArgumentNullException(nameof(collection));
        if (stateMachine == null)
            throw new ArgumentNullException(nameof(stateMachine));

        if (leafOnly)
        {
            return string.Join(separator, collection.Where(state =>
            {
                if (string.IsNullOrWhiteSpace(state)) return false;
                var stateNode = stateMachine.GetState(state);
                return stateNode is CompoundState cs && cs.SubStateNames.Count == 0;
            }).Select(state => state));
        }
        else
        {
            return string.Join(separator, collection.Select(state => state ?? "<null>"));
        }
    }

    public static string ToCsvString(this ICollection<string> collection, StateMachine stateMachine, bool leafOnly = true, string separator = ";")
    {
        if (collection == null)
            throw new ArgumentNullException(nameof(collection));
        if (stateMachine == null)
            throw new ArgumentNullException(nameof(stateMachine));

        if (leafOnly)
        {
            return string.Join(separator, collection.Where(state =>
            {
                if (string.IsNullOrWhiteSpace(state)) return false;
                var stateNode = stateMachine.GetState(state);
                return stateNode is CompoundState cs && cs.SubStateNames.Count == 0;
            }).Select(state => state));
        }
        else
        {
            return string.Join(separator, collection.Select(state => state ?? "<null>"));
        }
    }

    public static StateNode ToState(this string stateName, StateMachine stateMachine)
    {
        if (string.IsNullOrWhiteSpace(stateName))
            throw new ArgumentNullException(nameof(stateName));
        if (stateMachine == null)
            throw new ArgumentNullException(nameof(stateMachine));

        return stateMachine.GetState(stateName);
    }
}
