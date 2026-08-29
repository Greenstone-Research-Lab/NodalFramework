using System.Text.RegularExpressions;
using Nodal.Core.Analytics;
using Nodal.Core.Migrations;
using Nodal.Core.Providers;
using Nodal.TigerGraph.Extensions;

namespace Nodal.TigerGraph;

/// <summary>
/// Compiles analytics requests to explicitly configured, installed TigerGraph GSQL query endpoints.
/// Installed queries must return canonical <c>nodal_node</c> and <c>nodal_metrics</c> fields.
/// </summary>
public sealed partial class TigerGraphAnalyticsCompiler : IGraphAnalyticsCompiler
{
    private readonly string graphName;
    private readonly IReadOnlyDictionary<GraphAnalyticsAlgorithm, string> installedQueries;
    private readonly TigerGraphAnalyticsBindingManifest? bindingManifest;
    private readonly TigerGraphAnalyticsProvisioningMode provisioningMode;
    private readonly string contractVersion;
    private readonly TigerGraphInstalledQueryCatalog? generatedQueries;

    /// <summary>Initializes the compiler for one graph and its installed algorithm-query mapping.</summary>
    public TigerGraphAnalyticsCompiler(
        string graphName,
        IReadOnlyDictionary<GraphAnalyticsAlgorithm, string> installedQueries)
        : this(
            graphName,
            installedQueries,
            null,
            TigerGraphAnalyticsProvisioningMode.ValidateOnly,
            "1",
            null)
    {
    }

    internal TigerGraphAnalyticsCompiler(
        string graphName,
        IReadOnlyDictionary<GraphAnalyticsAlgorithm, string> installedQueries,
        TigerGraphAnalyticsBindingManifest? bindingManifest,
        TigerGraphAnalyticsProvisioningMode provisioningMode,
        string contractVersion,
        TigerGraphInstalledQueryCatalog? generatedQueries)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        ArgumentNullException.ThrowIfNull(installedQueries);
        foreach (var queryName in installedQueries.Values)
        {
            ValidateIdentifier(queryName, nameof(installedQueries));
        }
        this.graphName = graphName;
        this.installedQueries = installedQueries;
        this.bindingManifest = bindingManifest;
        this.provisioningMode = provisioningMode;
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion);
        this.contractVersion = contractVersion;
        this.generatedQueries = generatedQueries;
    }

    /// <inheritdoc />
    public GraphCommand Compile(GraphAnalyticsQueryModel query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var queryName = ResolveQueryName(query);

        var parameters = query.Nodes.Parameters.ToDictionary(item => item.Name, item => item.Value);
        if (query.TargetNodes is not null)
        {
            foreach (var parameter in query.TargetNodes.Parameters)
            {
                parameters[parameter.Name] = parameter.Value;
            }
            parameters["nodal_target_vertex_type"] = query.TargetNodes.NodeType;
        }
        parameters["nodal_vertex_type"] = query.Nodes.NodeType;
        parameters["nodal_edge_type"] = query.RelationshipType;
        parameters["nodal_edge_types"] = query.EffectiveRelationships.Select(item => item.RelationshipType).ToArray();
        parameters["nodal_relationship_directions"] = query.EffectiveRelationships
            .Select(item => item.Directed ? "directed" : "undirected").ToArray();
        parameters["nodal_relationship_coefficients"] = query.EffectiveRelationships
            .Select(item => item.Coefficient).ToArray();
        parameters["nodal_directed"] = query.Directed;
        if (query.RelationshipWeightProperty is not null)
        {
            parameters["nodal_weight_property"] = query.RelationshipWeightProperty;
        }
        if (query.Limit is not null)
        {
            parameters["nodal_limit"] = query.Limit.Value;
        }
        if (query.MaxDepth is not null)
        {
            parameters["nodal_max_depth"] = query.MaxDepth.Value;
        }
        foreach (var option in query.EffectiveConfiguration)
        {
            parameters[$"nodal_{option.Key}"] = option.Value;
        }

        return new GraphCommand(
            string.Empty,
            parameters,
            $"restpp/query/{Uri.EscapeDataString(graphName)}/{Uri.EscapeDataString(queryName)}");
    }

    internal void Validate(GraphAnalyticsQueryModel query) => _ = ResolveQueryName(query);

    private string ResolveQueryName(GraphAnalyticsQueryModel query)
    {
        var key = GraphAnalyticsBindingKey.Create(query, contractVersion);
        if (bindingManifest?.TryGet(key.Fingerprint, out var binding) == true)
        {
            if (!string.Equals(binding.ContractVersion, contractVersion, StringComparison.Ordinal))
            {
                throw Missing(query, key, "The installed binding contract version does not match the configured version.");
            }
            if (query.EffectiveRelationships.Any(item => item.WeightProperty is not null) && !binding.SupportsWeights)
            {
                throw Missing(query, key, "The installed binding does not declare weighted relationship support.");
            }
            return binding.QueryName;
        }
        if (query.EffectiveRelationships.Count == 1 && installedQueries.TryGetValue(query.Algorithm, out var legacy))
        {
            return legacy;
        }
        if (provisioningMode != TigerGraphAnalyticsProvisioningMode.ValidateOnly && generatedQueries is not null)
        {
            var definition = TigerGraphInstalledQueryDefinitionFactory.CreatePageRank(graphName, query, contractVersion);
            generatedQueries.Register(definition);
            return definition.Name;
        }
        throw Missing(query, key, generatedQueries is null && provisioningMode != TigerGraphAnalyticsProvisioningMode.ValidateOnly
            ? "Automatic provisioning requires an explicit administrative transport."
            : "No verified installed-query binding matches the requested analytics scope.");
    }

    private static NodalCapabilityNotSupportedException Missing(
        GraphAnalyticsQueryModel query,
        GraphAnalyticsBindingKey key,
        string reason) => new(
            "TigerGraph",
            "NODAL-TIGERGRAPH-ANALYTICS-BINDING",
            $"{reason} Algorithm '{query.Algorithm}', node '{query.Nodes.NodeType}', binding '{key.Fingerprint}'.");

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException("TigerGraph identifiers may contain only letters, numbers, and underscores.", parameterName);
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
