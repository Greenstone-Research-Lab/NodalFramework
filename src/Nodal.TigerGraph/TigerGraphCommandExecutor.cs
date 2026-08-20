using System.Globalization;
using System.Text;
using System.Text.Json;
using Nodal.Core.Execution;
using Nodal.Core.Providers;

namespace Nodal.TigerGraph;

/// <summary>
/// Executes interpreted GSQL commands over TigerGraph's HTTP API.
/// </summary>
public sealed class TigerGraphCommandExecutor : IGraphCommandExecutor
{
    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;

    /// <summary>
    /// Initializes an executor with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    public TigerGraphCommandExecutor(HttpClient httpClient, TigerGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        httpClient.BaseAddress ??= options.Endpoint;
        this.httpClient = httpClient;
        this.options = options;
    }

    /// <inheritdoc />
    public async ValueTask<GraphQueryResult> ExecuteAsync(
        GraphCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildRequestUri(command.Parameters));
        TigerGraphAuthentication.Apply(request, options);
        request.Content = new StringContent(command.Text, Encoding.UTF8, "text/plain");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TigerGraph returned HTTP {(int)response.StatusCode}: {payload}",
                null,
                response.StatusCode);
        }

        return Parse(payload);
    }

    private static string BuildRequestUri(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0)
        {
            return "gsql/v1/queries/interpret";
        }

        var query = string.Join("&", parameters.SelectMany(FormatQueryParameter));
        return $"gsql/v1/queries/interpret?{query}";
    }

    private static IEnumerable<string> FormatQueryParameter(KeyValuePair<string, object?> parameter)
    {
        if (parameter.Value is System.Collections.IEnumerable values and not string)
        {
            foreach (var value in values)
            {
                yield return FormatPair(parameter.Key, value);
            }

            yield break;
        }

        yield return FormatPair(parameter.Key, parameter.Value);
    }

    private static string FormatPair(string name, object? value) =>
        $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(FormatParameter(value))}";

    private static string FormatParameter(object? value) => value switch
    {
        null => string.Empty,
        bool boolean => boolean ? "true" : "false",
        DateTime dateTime => dateTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static GraphQueryResult Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
        {
            var message = document.RootElement.TryGetProperty("message", out var errorMessage)
                ? errorMessage.GetString()
                : "TigerGraph reported an unknown query error.";
            throw new InvalidOperationException(message);
        }

        var nodes = new List<GraphNodeRecord>();
        var relations = new List<GraphRelationRecord>();
        CollectNodes(document.RootElement, nodes);
        CollectRelations(document.RootElement, relations);
        var sources = new List<GraphNodeRecord>();
        var targets = new List<GraphNodeRecord>();
        CollectNamedNodes(document.RootElement, "nodal_sources", sources);
        CollectNamedNodes(document.RootElement, "nodal_targets", targets);
        var paths = relations.SelectMany(relation =>
        {
            var source = sources.FirstOrDefault(node => IsEndpoint(node.Id, relation));
            var target = targets.FirstOrDefault(node => IsEndpoint(node.Id, relation) &&
                (source is null || !Equals(node.Id, source.Id)));
            return source is not null && target is not null
                ? [new GraphPathRecord(source, relation, target)]
                : Array.Empty<GraphPathRecord>();
        }).ToArray();
        var scalars = new Dictionary<string, object?>(StringComparer.Ordinal);
        CollectScalars(document.RootElement, scalars);
        return new GraphQueryResult(nodes, relations, paths, scalars);
    }

    private static void CollectScalars(JsonElement element, IDictionary<string, object?> scalars)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.StartsWith("nodal_", StringComparison.Ordinal) &&
                    property.Value.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
                {
                    scalars[property.Name] = ConvertJsonValue(property.Value);
                }
                else
                {
                    CollectScalars(property.Value, scalars);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectScalars(item, scalars);
            }
        }
    }

    private static void CollectRelations(JsonElement element, ICollection<GraphRelationRecord> relations)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("e_type", out var type) &&
            element.TryGetProperty("from_id", out var sourceId) &&
            element.TryGetProperty("to_id", out var targetId))
        {
            var properties = element.TryGetProperty("attributes", out var attributes)
                ? attributes.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => ConvertJsonValue(property.Value))
                : new Dictionary<string, object?>();
            var normalizedSource = ConvertJsonValue(sourceId) ?? string.Empty;
            var normalizedTarget = ConvertJsonValue(targetId) ?? string.Empty;
            var id = element.TryGetProperty("e_id", out var edgeId)
                ? ConvertJsonValue(edgeId) ?? $"{normalizedSource}->{normalizedTarget}"
                : $"{normalizedSource}->{normalizedTarget}";
            relations.Add(new GraphRelationRecord(
                type.GetString() ?? string.Empty,
                id,
                normalizedSource,
                normalizedTarget,
                properties));
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                CollectRelations(property.Value, relations);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectRelations(item, relations);
            }
        }
    }

    private static void CollectNamedNodes(JsonElement element, string name, ICollection<GraphNodeRecord> nodes)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name))
                {
                    CollectNodes(property.Value, nodes);
                }
                else
                {
                    CollectNamedNodes(property.Value, name, nodes);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectNamedNodes(item, name, nodes);
            }
        }
    }

    private static bool IsEndpoint(object id, GraphRelationRecord relation) =>
        Equals(id, relation.SourceId) || Equals(id, relation.TargetId);

    private static void CollectNodes(JsonElement element, ICollection<GraphNodeRecord> nodes)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("v_id", out var id) &&
            element.TryGetProperty("v_type", out var type))
        {
            var properties = element.TryGetProperty("attributes", out var attributes)
                ? attributes.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => ConvertJsonValue(property.Value))
                : new Dictionary<string, object?>();
            nodes.Add(new GraphNodeRecord(
                type.GetString() ?? string.Empty,
                ConvertJsonValue(id) ?? string.Empty,
                properties));
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                CollectNodes(property.Value, nodes);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectNodes(item, nodes);
            }
        }
    }

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.Clone(),
    };
}
