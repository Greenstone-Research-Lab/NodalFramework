using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nodal.Core.ChangeTracking;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;

namespace Nodal.TigerGraph;

/// <summary>
/// Executes a complete mutation plan through one explicitly atomic TigerGraph request or installed query.
/// </summary>
/// <remarks>
/// Delete plans use a deterministic GSQL query compiled and installed through the configured
/// administrative transport. The executor never splits one unit of work into non-atomic requests.
/// </remarks>
public sealed class TigerGraphMutationExecutor : IGraphMutationExecutor
{
    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly ITigerGraphAdministrativeTransport? administrativeTransport;
    private readonly TigerGraphMutationCompiler compiledMutationCompiler;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> installationLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> installedQueries = new(StringComparer.Ordinal);

    /// <summary>Initializes an executor with an externally managed pooled HTTP client.</summary>
    public TigerGraphMutationExecutor(HttpClient httpClient, TigerGraphOptions options, string graphName)
        : this(httpClient, options, graphName, null)
    {
    }

    /// <summary>
    /// Initializes an executor with the privileged channel required to install transactional delete queries.
    /// </summary>
    public TigerGraphMutationExecutor(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeTransport? administrativeTransport)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphName);
        httpClient.BaseAddress ??= options.Endpoint;
        this.httpClient = httpClient;
        this.options = options;
        this.graphName = graphName;
        this.administrativeTransport = administrativeTransport;
        compiledMutationCompiler = new TigerGraphMutationCompiler(graphName);
    }

    /// <inheritdoc />
    public async ValueTask<GraphMutationResult> ExecuteAsync(
        GraphMutationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.IsEmpty)
        {
            return new GraphMutationResult(0, 0, true);
        }

        var unsupported = plan.Operations.FirstOrDefault(operation =>
            operation is DeleteNodeOperation or DeleteRelationOperation);
        if (unsupported is not null)
        {
            if (administrativeTransport is null)
            {
                throw new NotSupportedException(
                    $"TigerGraph operation '{unsupported.GetType().Name}' requires the installed transactional mutation query.");
            }

            return await ExecuteCompiledAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}?vertex_must_exist=true");
        TigerGraphAuthentication.Apply(request, options);
        request.Headers.TryAddWithoutValidation("gsql-atomic-level", "atomic");
        request.Content = new StringContent(BuildPayload(plan), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TigerGraph returned HTTP {(int)response.StatusCode}: {payload}",
                null,
                response.StatusCode);
        }

        return ParseResult(payload);
    }

    private async ValueTask<GraphMutationResult> ExecuteCompiledAsync(
        GraphMutationPlan plan,
        CancellationToken cancellationToken)
    {
        var mutation = compiledMutationCompiler.Compile(plan);
        await EnsureInstalledAsync(mutation, cancellationToken).ConfigureAwait(false);
        var query = string.Join("&", mutation.Parameters.Select(parameter =>
            $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        var uri = $"restpp/query/{Uri.EscapeDataString(graphName)}/{mutation.QueryName}";
        if (query.Length > 0)
        {
            uri += $"?{query}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        TigerGraphAuthentication.Apply(request, options);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TigerGraph installed mutation query returned HTTP {(int)response.StatusCode}: {payload}",
                null,
                response.StatusCode);
        }

        ThrowIfTigerGraphError(payload);
        var affectedNodes = plan.Operations.Count(operation =>
            operation is CreateNodeOperation or UpdateNodeOperation or DeleteNodeOperation);
        var affectedRelations = plan.Operations.Count(operation =>
            operation is CreateRelationOperation or UpdateRelationOperation or DeleteRelationOperation);
        return new GraphMutationResult(affectedNodes, affectedRelations, true);
    }

    private async ValueTask EnsureInstalledAsync(
        TigerGraphCompiledMutation mutation,
        CancellationToken cancellationToken)
    {
        if (installedQueries.ContainsKey(mutation.QueryName))
        {
            return;
        }

        var installationLock = installationLocks.GetOrAdd(mutation.QueryName, _ => new SemaphoreSlim(1, 1));
        await installationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (installedQueries.ContainsKey(mutation.QueryName))
            {
                return;
            }

            await administrativeTransport!.ExecuteAsync(
                new MigrationCommand(mutation.Definition, false, MigrationCommandKind.QueryDefinition),
                cancellationToken).ConfigureAwait(false);
            await administrativeTransport.ExecuteAsync(
                new MigrationCommand(
                    $"INSTALL QUERY -FORCE {mutation.QueryName}",
                    false,
                    MigrationCommandKind.QueryInstallation),
                cancellationToken).ConfigureAwait(false);
            installedQueries.TryAdd(mutation.QueryName, 0);
        }
        finally
        {
            installationLock.Release();
        }
    }

    private static string BuildPayload(GraphMutationPlan plan)
    {
        var root = new JsonObject();
        var vertices = new JsonObject();
        var edges = new JsonObject();

        foreach (var operation in plan.Operations)
        {
            switch (operation)
            {
                case CreateNodeOperation create:
                    AddNode(vertices, create.Identity.NodeType, create.Identity.Value, create.Identity.KeyProperty, create.Properties);
                    break;
                case UpdateNodeOperation update:
                    AddNode(vertices, update.Identity.NodeType, update.Identity.Value, update.Identity.KeyProperty, update.Properties);
                    break;
                case CreateRelationOperation create:
                    AddRelation(edges, create.Source, create.RelationType, create.Target, create.Properties);
                    break;
                case UpdateRelationOperation update:
                    AddRelation(edges, update.Source, update.RelationType, update.Target, update.Properties);
                    break;
            }
        }

        if (vertices.Count > 0)
        {
            root["vertices"] = vertices;
        }

        if (edges.Count > 0)
        {
            root["edges"] = edges;
        }

        return root.ToJsonString();
    }

    private static void AddNode(
        JsonObject vertices,
        string nodeType,
        object identity,
        string keyProperty,
        IReadOnlyDictionary<string, object?> properties)
    {
        var node = GetOrAdd(GetOrAdd(vertices, nodeType), FormatIdentity(identity));
        AddProperties(node, properties, keyProperty);
    }

    private static void AddRelation(
        JsonObject edges,
        GraphIdentity source,
        string relationType,
        GraphIdentity target,
        IReadOnlyDictionary<string, object?> properties)
    {
        var relation = GetOrAdd(
            GetOrAdd(
                GetOrAdd(
                    GetOrAdd(
                        GetOrAdd(edges, source.NodeType),
                        FormatIdentity(source.Value)),
                    relationType),
                target.NodeType),
            FormatIdentity(target.Value));
        AddProperties(relation, properties, null);
    }

    private static void AddProperties(
        JsonObject target,
        IReadOnlyDictionary<string, object?> properties,
        string? excludedProperty)
    {
        foreach (var property in properties.Where(property =>
                     !string.Equals(property.Key, excludedProperty, StringComparison.Ordinal)))
        {
            target[property.Key] = new JsonObject
            {
                ["value"] = JsonSerializer.SerializeToNode(property.Value),
            };
        }
    }

    private static JsonObject GetOrAdd(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static string FormatIdentity(object identity) =>
        Convert.ToString(identity, CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException("A TigerGraph identity cannot be converted to text.");

    private static GraphMutationResult ParseResult(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        ThrowIfTigerGraphError(document.RootElement);

        var counts = CountAccepted(document.RootElement);
        return new GraphMutationResult(counts.Nodes, counts.Relations, true);
    }

    private static void ThrowIfTigerGraphError(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        ThrowIfTigerGraphError(document.RootElement);
    }

    private static void ThrowIfTigerGraphError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
        {
            var message = root.TryGetProperty("message", out var errorMessage)
                ? errorMessage.GetString()
                : "TigerGraph reported an unknown mutation error.";
            throw new InvalidOperationException(message);
        }
    }

    private static (int Nodes, int Relations) CountAccepted(JsonElement element)
    {
        var nodes = 0;
        var relations = 0;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("accepted_vertices") && property.Value.TryGetInt32(out var acceptedNodes))
                {
                    nodes += acceptedNodes;
                }
                else if (property.NameEquals("accepted_edges") && property.Value.TryGetInt32(out var acceptedRelations))
                {
                    relations += acceptedRelations;
                }
                else
                {
                    var nested = CountAccepted(property.Value);
                    nodes += nested.Nodes;
                    relations += nested.Relations;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = CountAccepted(item);
                nodes += nested.Nodes;
                relations += nested.Relations;
            }
        }

        return (nodes, relations);
    }
}
