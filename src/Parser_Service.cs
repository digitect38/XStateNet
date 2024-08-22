using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace XStateNet;

using ServiceMap = ConcurrentDictionary<string, NamedService>;

internal class Parser_Service
{
    public static NamedService? ParseService(string key, ServiceMap? serviceMap, JToken? token)
    {
        if (token == null)
        {
            throw new Exception("Token is null");
        }

        var srcToken = token[key];

        if (srcToken == null)
        {
            return null;
        }

        var serviceName = srcToken["src"]?.ToString();

        if (serviceName == null)
        {
            return null;
        }

        if (serviceMap == null)
        {
            return null;
        }

        if (serviceMap.TryGetValue(serviceName, out var service))
        {
            return service;
        }
        else
        {
            throw new Exception($"Service {serviceName} not found in the service map.");
        }
    }
}

