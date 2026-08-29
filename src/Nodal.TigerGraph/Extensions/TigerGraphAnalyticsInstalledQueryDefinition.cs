using System.Globalization;
using Nodal.Core.Analytics;

namespace Nodal.TigerGraph.Extensions;

public static partial class TigerGraphInstalledQueryDefinitionFactory
{
    /// <summary>Creates one deterministic homogeneous unweighted PageRank installed-query definition.</summary>
    public static TigerGraphInstalledQueryDefinition CreatePageRank(
        string graphName,
        GraphAnalyticsQueryModel query,
        string contractVersion = "1")
    {
        ValidateIdentifier(graphName, nameof(graphName));
        ArgumentNullException.ThrowIfNull(query);
        if (query.Algorithm != GraphAnalyticsAlgorithm.PageRank)
        {
            throw new NotSupportedException("The managed TigerGraph analytics generator currently supports PageRank only.");
        }
        var relationships = query.EffectiveRelationships;
        if (relationships.Any(item => item.WeightProperty is not null || item.Coefficient != 1))
        {
            throw new NotSupportedException("Generated TigerGraph PageRank currently requires unweighted unit-coefficient relationships.");
        }
        ValidateIdentifier(query.Nodes.NodeType, nameof(query));
        foreach (var relationship in relationships)
        {
            ValidateIdentifier(relationship.RelationshipType, nameof(query));
        }

        var key = GraphAnalyticsBindingKey.Create(query, contractVersion);
        var name = TigerGraphAnalyticsNaming.CreateQueryName(key);
        var edgeTypes = string.Join('|', relationships.Select(item => item.RelationshipType));
        var traversal = relationships.All(item => !item.Directed)
            ? $"-(({edgeTypes}):nodal_edge)-"
            : $"-(({edgeTypes}):nodal_edge)->";
        var text = string.Create(CultureInfo.InvariantCulture,
            $"CREATE OR REPLACE QUERY {name}(INT nodal_limit=100, DOUBLE nodal_dampingFactor=0.85, INT nodal_maxIterations=30, DOUBLE nodal_tolerance=0.0001) FOR GRAPH {graphName} {{ SumAccum<DOUBLE> @nodal_score = 1.0; SumAccum<DOUBLE> @nodal_received; SumAccum<INT> @nodal_degree; {query.Nodes.NodeType} = {{{query.Nodes.NodeType}.*}}; ranked = SELECT source FROM {query.Nodes.NodeType}:source {traversal} {query.Nodes.NodeType}:target ACCUM source.@nodal_degree += 1; INT iteration = 0; WHILE iteration < nodal_maxIterations DO ranked = SELECT source FROM ranked:source {traversal} {query.Nodes.NodeType}:target ACCUM target.@nodal_received += source.@nodal_score / CASE WHEN source.@nodal_degree == 0 THEN 1 ELSE source.@nodal_degree END POST-ACCUM target.@nodal_score = (1.0 - nodal_dampingFactor) + nodal_dampingFactor * target.@nodal_received, target.@nodal_received = 0; iteration = iteration + 1; END; result = SELECT node FROM ranked:node ORDER BY node.@nodal_score DESC LIMIT nodal_limit; PRINT result AS nodal_node; PRINT result[result.@nodal_score AS score] AS nodal_metrics; }}\nINSTALL QUERY -FORCE {name}");
        return new TigerGraphInstalledQueryDefinition(name, key.Fingerprint, text);
    }
}
