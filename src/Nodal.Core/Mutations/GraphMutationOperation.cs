using Nodal.Core.ChangeTracking;

namespace Nodal.Core.Mutations;

/// <summary>Provides the base contract for a provider-neutral graph mutation.</summary>
public abstract record GraphMutationOperation;

/// <summary>Creates a graph node with its current mapped properties.</summary>
/// <param name="Identity">The stable node identity.</param>
/// <param name="Properties">The graph property values.</param>
public sealed record CreateNodeOperation(
    GraphIdentity Identity,
    IReadOnlyDictionary<string, object?> Properties) : GraphMutationOperation;

/// <summary>Updates a graph node using its stable identity.</summary>
/// <param name="Identity">The stable node identity.</param>
/// <param name="Properties">The graph property values to write.</param>
public sealed record UpdateNodeOperation(
    GraphIdentity Identity,
    IReadOnlyDictionary<string, object?> Properties) : GraphMutationOperation;

/// <summary>Deletes a graph node using its stable identity.</summary>
/// <param name="Identity">The stable node identity.</param>
public sealed record DeleteNodeOperation(GraphIdentity Identity) : GraphMutationOperation;

/// <summary>Creates a relationship between two stable node identities.</summary>
/// <param name="Source">The source identity.</param>
/// <param name="RelationType">The provider-neutral relationship name.</param>
/// <param name="Target">The target identity.</param>
/// <param name="Directed">Whether direction is semantically significant.</param>
/// <param name="Properties">The relationship property values.</param>
public sealed record CreateRelationOperation(
    GraphIdentity Source,
    string RelationType,
    GraphIdentity Target,
    bool Directed,
    IReadOnlyDictionary<string, object?> Properties) : GraphMutationOperation;

/// <summary>Updates the mapped properties of a relationship between two stable node identities.</summary>
public sealed record UpdateRelationOperation(
    GraphIdentity Source,
    string RelationType,
    GraphIdentity Target,
    bool Directed,
    IReadOnlyDictionary<string, object?> Properties,
    object? ProviderId = null) : GraphMutationOperation;

/// <summary>Deletes a relationship between two stable node identities.</summary>
/// <param name="Source">The source identity.</param>
/// <param name="RelationType">The provider-neutral relationship name.</param>
/// <param name="Target">The target identity.</param>
/// <param name="Directed">Whether direction is semantically significant.</param>
public sealed record DeleteRelationOperation(
    GraphIdentity Source,
    string RelationType,
    GraphIdentity Target,
    bool Directed) : GraphMutationOperation;
