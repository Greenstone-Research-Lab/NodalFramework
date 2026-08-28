using System.Collections.Concurrent;

namespace Nodal.TigerGraph.Extensions;

internal sealed class TigerGraphInstalledQueryCatalog(string graphName)
{
    private readonly ConcurrentDictionary<string, TigerGraphInstalledQueryDefinition> definitions =
        new(StringComparer.Ordinal);

    public string Register(TigerGraphInstalledQueryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var route = $"restpp/query/{graphName}/{definition.Name}";
        definitions[route] = definition;
        return route;
    }

    public bool TryGet(string? route, out TigerGraphInstalledQueryDefinition definition)
    {
        if (route is not null && definitions.TryGetValue(route, out var value))
        {
            definition = value;
            return true;
        }

        definition = null!;
        return false;
    }
}
