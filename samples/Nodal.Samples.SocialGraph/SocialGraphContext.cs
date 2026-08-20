using Nodal.Core;
using Nodal.Core.Execution;
using Nodal.Core.Query;

namespace Nodal.Samples.SocialGraph;

/// <summary>
/// Exposes the social graph through provider-neutral, strongly typed sets.
/// </summary>
/// <param name="provider">The graph provider that executes queries and mutations.</param>
public sealed class SocialGraphContext(IGraphProvider provider) : NodalContext(provider)
{
    /// <summary>Gets the people stored in the graph.</summary>
    public GraphSet<Person> People => Set<Person>();

    /// <summary>Gets the directed relationships between people.</summary>
    public RelationSet<Person, Knows, Person> Friendships => Relations<Person, Knows, Person>();
}
