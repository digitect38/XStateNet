using System;
using System.Collections.Generic;
using System.Linq;
namespace XStateNet;

public static class Helper
{
    public static void WriteLine(int indent, string msg)
    {
        for (int i = 0; i < indent; i++)
        {
            Console.Write("  ");
        }
        Console.WriteLine(msg);
    }

    /// <summary>
    /// String of leaf active states
    /// </summary>
    /// <param name="collection"></param>
    /// <returns></returns>
    public static string ToCsvString(this IEnumerable<RealState> collection, bool leafOnly = true, string separator = ";")
    {
        if (leafOnly)
            return string.Join(separator, collection.Where(state => state.SubStateNames.Count == 0).Select(state => state.Name));
        else
            return string.Join(separator, collection.Select(state => state.Name));
    }
}
