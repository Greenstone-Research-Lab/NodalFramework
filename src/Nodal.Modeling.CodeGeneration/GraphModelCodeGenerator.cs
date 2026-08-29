using System.Globalization;
using System.Text;
using Nodal.Core.Modeling;

namespace Nodal.Modeling.CodeGeneration;

/// <summary>Generates deterministic, provider-neutral C# strong types from a canonical graph descriptor.</summary>
public sealed class GraphModelCodeGenerator
{
    private const string Version = "1.0.0";

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
        "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while",
    };

    /// <summary>Generates one complete strong-type source set.</summary>
    /// <param name="descriptor">The canonical descriptor to compile.</param>
    /// <param name="options">Optional namespace and context naming options.</param>
    /// <returns>Generated files ordered by relative path.</returns>
    public static IReadOnlyList<GeneratedSourceFile> Generate(
        GraphModelDescriptor descriptor,
        GraphModelGeneratorOptions? options = null)
    {
        var canonical = GraphModelDescriptorJson.Canonicalize(descriptor);
        var configuration = options ?? new GraphModelGeneratorOptions();
        ValidateIdentifier(configuration.ContextName, nameof(configuration.ContextName));
        ValidateNamespace(configuration.RootNamespace);
        ValidateClrNames(canonical);

        var nodes = canonical.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var files = new List<GeneratedSourceFile>(canonical.Nodes.Count + canonical.Relations.Count + 3);
        files.AddRange(canonical.Nodes.Select(node =>
            File($"Nodes/{node.ClrName}.cs", RenderNode(node, configuration.RootNamespace))));
        files.AddRange(canonical.Relations.Select(relation =>
            File($"Relations/{relation.ClrName}.cs", RenderRelation(relation, configuration.RootNamespace))));
        files.Add(File(
            $"{configuration.ContextName}.cs",
            RenderContext(canonical, nodes, configuration)));
        files.Add(File("NodalGeneratedModelManifest.cs", RenderManifest(canonical, configuration.RootNamespace)));
        files.Add(File("NodalGeneratedJsonContext.cs", RenderJsonContext(canonical, configuration.RootNamespace)));
        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static GeneratedSourceFile File(string path, string content) =>
        new(path, string.Concat(content.TrimEnd(), Environment.NewLine));

    private static string RenderNode(NodeTypeDescriptor node, string rootNamespace)
    {
        var builder = Header(rootNamespace, "Nodes")
            .AppendLine("/// <summary>Represents a generated graph node from the canonical Nodal descriptor.</summary>")
            .Append("[GraphNode(\"").Append(Escape(node.Name)).AppendLine("\")]")
            .Append("public sealed class ").Append(node.ClrName).AppendLine()
            .AppendLine("{");
        if (node.Key.Properties.Count > 1)
        {
            builder.AppendLine("    [GraphKey]")
                .AppendLine("    [GraphProperty(\"__nodal_composite_key\")]")
                .AppendLine("    /// <summary>Gets or sets the stable application identity composed from the source key members.</summary>")
                .AppendLine("    public string NodalCompositeKey { get; set; } = string.Empty;");
        }

        foreach (var property in node.Properties)
        {
            if (node.Key.Properties.Count == 1 && node.Key.Properties.Contains(property.Name, StringComparer.Ordinal))
            {
                builder.AppendLine("    [GraphKey]");
            }

            builder.Append("    /// <summary>Gets or sets a generated node property.</summary>").AppendLine()
                .Append("    [GraphProperty(\"").Append(Escape(property.Name)).AppendLine("\")]")
                .Append("    public ").Append(PropertyType(property)).Append(' ').Append(property.ClrName)
                .Append(" { get; set; }").AppendLine(Initializer(property));
        }

        return builder.AppendLine("}").ToString();
    }

    private static string RenderRelation(RelationTypeDescriptor relation, string rootNamespace)
    {
        var builder = Header(rootNamespace, "Relations")
            .AppendLine("/// <summary>Represents a generated graph relation from the canonical Nodal descriptor.</summary>")
            .Append("[GraphRelation(\"").Append(Escape(relation.Name)).Append("\", Directed = ")
            .Append(relation.Directed ? "true" : "false").AppendLine(")]")
            .Append("public sealed class ").Append(relation.ClrName).AppendLine()
            .AppendLine("{");
        foreach (var property in relation.Properties)
        {
            builder.Append("    /// <summary>Gets or sets a generated relation property.</summary>").AppendLine()
                .Append("    [GraphProperty(\"").Append(Escape(property.Name)).AppendLine("\")]")
                .Append("    public ").Append(PropertyType(property)).Append(' ').Append(property.ClrName)
                .Append(" { get; set; }").AppendLine(Initializer(property));
        }

        return builder.AppendLine("}").ToString();
    }

    private static string RenderContext(
        GraphModelDescriptor descriptor,
        Dictionary<string, NodeTypeDescriptor> nodes,
        GraphModelGeneratorOptions options)
    {
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated />")
            .AppendLine("using Nodal.Core;")
            .AppendLine("using Nodal.Core.Execution;")
            .AppendLine("using Nodal.Core.Query;")
            .Append("using ").Append(options.RootNamespace).AppendLine(".Nodes;")
            .Append("using ").Append(options.RootNamespace).AppendLine(".Relations;")
            .AppendLine()
            .Append("namespace ").Append(options.RootNamespace).AppendLine(";")
            .AppendLine()
            .AppendLine("/// <summary>Provides strongly typed sets generated from the canonical Nodal descriptor.</summary>")
            .Append("public sealed class ").Append(options.ContextName)
            .Append("(IGraphProvider provider) : NodalContext(provider)").AppendLine()
            .AppendLine("{");
        foreach (var node in descriptor.Nodes)
        {
            builder.AppendLine("    /// <summary>Gets a generated node set.</summary>")
                .Append("    public GraphSet<").Append(node.ClrName).Append("> ")
                .Append(Pluralize(node.ClrName)).Append(" => Set<").Append(node.ClrName).AppendLine(">();");
        }

        foreach (var relation in descriptor.Relations)
        {
            builder.AppendLine("    /// <summary>Gets a generated relation set.</summary>")
                .Append("    public RelationSet<").Append(nodes[relation.SourceNodeId].ClrName).Append(", ")
                .Append(relation.ClrName).Append(", ").Append(nodes[relation.TargetNodeId].ClrName).Append("> ")
                .Append(Pluralize(relation.ClrName)).Append(" => Relations<")
                .Append(nodes[relation.SourceNodeId].ClrName).Append(", ").Append(relation.ClrName).Append(", ")
                .Append(nodes[relation.TargetNodeId].ClrName).AppendLine(">();");
        }

        return builder.AppendLine("}").ToString();
    }

    private static string RenderManifest(GraphModelDescriptor descriptor, string rootNamespace) =>
        new StringBuilder()
            .AppendLine("// <auto-generated />")
            .Append("namespace ").Append(rootNamespace).AppendLine(";")
            .AppendLine()
            .AppendLine("/// <summary>Records reproducibility evidence for the generated model.</summary>")
            .AppendLine("public static class NodalGeneratedModelManifest")
            .AppendLine("{")
            .AppendLine("    /// <summary>The canonical descriptor fingerprint.</summary>")
            .Append("    public const string DescriptorFingerprint = \"")
            .Append(GraphModelDescriptorJson.ComputeFingerprint(descriptor)).AppendLine("\";")
            .AppendLine("    /// <summary>The canonical descriptor format version.</summary>")
            .Append("    public const string DescriptorFormatVersion = \"")
            .Append(descriptor.FormatVersion).AppendLine("\";")
            .AppendLine("    /// <summary>The deterministic generator version.</summary>")
            .Append("    public const string GeneratorVersion = \"").Append(Version).AppendLine("\";")
            .AppendLine("}")
            .ToString();

    private static string RenderJsonContext(GraphModelDescriptor descriptor, string rootNamespace)
    {
        var builder = new StringBuilder()
            .AppendLine("// <auto-generated />")
            .AppendLine("using System.Text.Json.Serialization;")
            .Append("using ").Append(rootNamespace).AppendLine(".Nodes;")
            .Append("using ").Append(rootNamespace).AppendLine(".Relations;")
            .AppendLine()
            .Append("namespace ").Append(rootNamespace).AppendLine(";")
            .AppendLine();
        foreach (var clrName in descriptor.Nodes.Select(node => node.ClrName)
                     .Concat(descriptor.Relations.Select(relation => relation.ClrName))
                     .Order(StringComparer.Ordinal))
        {
            builder.Append("[JsonSerializable(typeof(").Append(clrName).AppendLine("))]");
        }

        return builder.AppendLine("/// <summary>Provides Native AOT-compatible JSON metadata for generated graph types.</summary>")
            .AppendLine("public partial class NodalGeneratedJsonContext : JsonSerializerContext;")
            .ToString();
    }

    private static StringBuilder Header(string rootNamespace, string segment) => new StringBuilder()
        .AppendLine("// <auto-generated />")
        .AppendLine("using Nodal.Core.Metadata;")
        .AppendLine("using Nodal.Core.Modeling;")
        .AppendLine()
        .Append("namespace ").Append(rootNamespace).Append('.').Append(segment).AppendLine(";")
        .AppendLine();

    private static string PropertyType(GraphPropertyDescriptor property)
    {
        var kind = property.IsCollection ? property.ItemKind!.Value : property.ValueKind;
        var type = kind switch
        {
            GraphValueKind.Text or GraphValueKind.Categorical => "string",
            GraphValueKind.Character => "char",
            GraphValueKind.SignedInteger => "long",
            GraphValueKind.UnsignedInteger => "ulong",
            GraphValueKind.DecimalNumber => "decimal",
            GraphValueKind.FloatingPoint => "double",
            GraphValueKind.Boolean => "bool",
            GraphValueKind.Identifier => "Guid",
            GraphValueKind.Date => "DateOnly",
            GraphValueKind.Time => "TimeOnly",
            GraphValueKind.DateTime => "DateTime",
            GraphValueKind.DateTimeOffset => "DateTimeOffset",
            GraphValueKind.GeoPoint => "GraphGeoPoint",
            GraphValueKind.Vector => "double[]",
            GraphValueKind.NestedObject => "IReadOnlyDictionary<string, GraphValue>",
            GraphValueKind.Null or GraphValueKind.Collection => "GraphValue",
            _ => throw new NotSupportedException($"Graph value kind '{kind}' cannot be generated."),
        };
        if (property.IsCollection)
        {
            type = string.Concat(type, "[]");
        }

        return property.IsNullable && type is not "string" && !type.EndsWith("[]", StringComparison.Ordinal)
            ? string.Concat(type, "?")
            : property.IsNullable && type == "string" ? "string?" : type;
    }

    private static string Initializer(GraphPropertyDescriptor property)
    {
        var type = PropertyType(property);
        if (type == "string")
        {
            return " = string.Empty;";
        }

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            return " = [];";
        }

        if (type.StartsWith("IReadOnlyDictionary", StringComparison.Ordinal))
        {
            return " = new Dictionary<string, GraphValue>(StringComparer.Ordinal);";
        }

        return string.Empty;
    }

    private static string Pluralize(string name) => name.EndsWith('s')
        ? string.Concat(name, "Set")
        : string.Concat(name, "s");

    private static void ValidateClrNames(GraphModelDescriptor descriptor)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in descriptor.Nodes.Select(node =>
                     (node.ClrName, node.Properties, IsComposite: node.Key.Properties.Count > 1))
                     .Concat(descriptor.Relations.Select(relation =>
                         (relation.ClrName, relation.Properties, IsComposite: false))))
        {
            ValidateIdentifier(type.ClrName, "descriptor CLR type name");
            if (!typeNames.Add(type.ClrName))
            {
                throw new ArgumentException($"Duplicate CLR type name '{type.ClrName}'.", nameof(descriptor));
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            if (type.IsComposite)
            {
                propertyNames.Add("NodalCompositeKey");
            }
            foreach (var property in type.Properties)
            {
                ValidateIdentifier(property.ClrName, "descriptor CLR property name");
                if (!propertyNames.Add(property.ClrName))
                {
                    throw new ArgumentException(
                        $"Duplicate CLR property name '{property.ClrName}' on '{type.ClrName}'.",
                        nameof(descriptor));
                }
            }
        }

        var members = descriptor.Nodes.Select(node => Pluralize(node.ClrName))
            .Concat(descriptor.Relations.Select(relation => Pluralize(relation.ClrName)))
            .ToArray();
        if (members.Distinct(StringComparer.Ordinal).Count() != members.Length)
        {
            throw new ArgumentException("Generated context member names collide.", nameof(descriptor));
        }
    }

    private static void ValidateNamespace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (var segment in value.Split('.'))
        {
            ValidateIdentifier(segment, "root namespace segment");
        }
    }

    private static void ValidateIdentifier(string value, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (Keywords.Contains(value) || !IsIdentifierStart(value[0]) || value.Skip(1).Any(character => !IsIdentifierPart(character)))
        {
            throw new ArgumentException($"'{value}' is not a safe C# {description}.");
        }
    }

    private static bool IsIdentifierStart(char character) => character == '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) => IsIdentifierStart(character) || char.IsDigit(character);

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
