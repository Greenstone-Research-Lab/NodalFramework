using System.Text.RegularExpressions;
using Nodal.Core.Analytics;
using Nodal.Core.Providers;

namespace Nodal.TigerGraph;

/// <summary>
/// Compiles analytics requests to explicitly configured, installed TigerGraph GSQL query endpoints.
/// Installed queries must return canonical <c>nodal_node</c> and <c>nodal_metrics</c> fields.
/// </summary>
public sealed partial class TigerGraphAnalyticsCompiler : IGraphAnalyticsCompiler
{
    private readonly string graphName;
    private readonly IReadOnlyDictionary<GraphAnalyticsAlgorithm, string> installedQueries;

    /// <summary>Initializes the compiler for one graph and its installed algorithm-query mapping.</summary>
    public TigerGraphAnalyticsCompiler(
        string graphName,
        IReadOnlyDictionary<GraphAnalyticsAlgorithm, string> installedQueries)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        ArgumentNullException.ThrowIfNull(installedQueries);
        foreach (var queryName in installedQueries.Values)
        {
            ValidateIdentifier(queryName, nameof(installedQueries));
        }
        this.graphName = graphName;
        this.installedQueries = installedQueries;
    }

    /// <inheritdoc />
    public GraphCommand Compile(GraphAnalyticsQueryModel query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!installedQueries.TryGetValue(query.Algorithm, out var queryName))
        {
            throw new NotSupportedException(
                $"TigerGraph algorithm '{query.Algorithm}' has no configured installed GSQL query.");
        }

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
