namespace Nodal.Modeling.CodeGeneration;

/// <summary>Configures deterministic strong-type generation from a canonical graph descriptor.</summary>
public sealed record GraphModelGeneratorOptions
{
    /// <summary>Gets the root namespace used by generated source files.</summary>
    public string RootNamespace { get; init; } = "Nodal.Generated";

    /// <summary>Gets the generated <see cref="Nodal.Core.NodalContext"/> type name.</summary>
    public string ContextName { get; init; } = "GeneratedGraphContext";
}

/// <summary>Represents one relative generated source path and its deterministic content.</summary>
/// <param name="RelativePath">The slash-separated path below the requested output directory.</param>
/// <param name="Content">The UTF-8 source content without a byte-order mark.</param>
public sealed record GeneratedSourceFile(string RelativePath, string Content);
