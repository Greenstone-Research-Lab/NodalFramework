namespace Nodal.Core.Modeling;

/// <summary>Defines validation issue severities for canonical model documents.</summary>
public enum GraphModelIssueSeverity
{
    /// <summary>The issue does not prevent consumption but requires review.</summary>
    Warning,

    /// <summary>The descriptor is invalid and must not be consumed.</summary>
    Error,
}

/// <summary>Describes one stable, machine-readable descriptor validation issue.</summary>
/// <param name="Code">Stable issue code.</param>
/// <param name="Severity">Issue severity.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Path">Logical descriptor path.</param>
public sealed record GraphModelValidationIssue(
    string Code,
    GraphModelIssueSeverity Severity,
    string Message,
    string Path);

/// <summary>Contains deterministic descriptor validation results.</summary>
/// <param name="Issues">Issues ordered by path and code.</param>
public sealed record GraphModelValidationResult(IReadOnlyList<GraphModelValidationIssue> Issues)
{
    /// <summary>Gets whether the descriptor contains no error issues.</summary>
    public bool IsValid => Issues.All(issue => issue.Severity != GraphModelIssueSeverity.Error);

    /// <summary>Throws a stable exception when validation fails.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new GraphModelValidationException(this);
        }
    }
}

/// <summary>Signals that a canonical graph descriptor failed validation.</summary>
public sealed class GraphModelValidationException : Exception
{
    /// <summary>Initializes the exception with deterministic validation evidence.</summary>
    public GraphModelValidationException(GraphModelValidationResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    /// <summary>Gets the validation evidence.</summary>
    public GraphModelValidationResult Result { get; }

    private static string BuildMessage(GraphModelValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return string.Join(
            Environment.NewLine,
            result.Issues.Where(issue => issue.Severity == GraphModelIssueSeverity.Error)
                .Select(issue => $"[{issue.Code}] {issue.Path}: {issue.Message}"));
    }
}

/// <summary>Produces non-throwing validation evidence for descriptor tooling and automation.</summary>
public static class GraphModelValidation
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
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

    /// <summary>Validates a descriptor without losing a stable failure code.</summary>
    public static GraphModelValidationResult Validate(GraphModelDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return Result("NODAL-MODEL-NULL", "The graph model descriptor is required.", "$");
        }

        try
        {
            GraphModelDescriptorValidator.ThrowIfInvalid(descriptor);
        }
        catch (NotSupportedException exception)
        {
            return Result("NODAL-MODEL-VERSION", exception.Message, "$.formatVersion");
        }
        catch (ArgumentNullException exception)
        {
            return Result("NODAL-MODEL-NULL-MEMBER", exception.Message, "$.");
        }
        catch (ArgumentException exception)
        {
            return Result("NODAL-MODEL-STRUCTURE", exception.Message, "$.");
        }

        var issues = ClrIssues(descriptor).Concat(descriptor.Nodes
            .Where(node => HasReview(node.ProviderAnnotations))
            .Select(node => new GraphModelValidationIssue(
                "NODAL-MODEL-REVIEW",
                GraphModelIssueSeverity.Warning,
                ReviewMessage(node.ProviderAnnotations!),
                $"$.nodes[{node.Id}]"))
            .Concat(descriptor.Relations.Where(relation => HasReview(relation.ProviderAnnotations))
                .Select(relation => new GraphModelValidationIssue(
                    "NODAL-MODEL-REVIEW",
                    GraphModelIssueSeverity.Warning,
                    ReviewMessage(relation.ProviderAnnotations!),
                    $"$.relations[{relation.Id}]"))))
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ToArray();
        return new GraphModelValidationResult(issues);
    }

    internal static bool HasReview(IReadOnlyDictionary<string, string>? annotations) =>
        annotations?.Any(pair =>
            (pair.Key == "nodal:review" || pair.Key.StartsWith("review.", StringComparison.Ordinal)) &&
            !string.Equals(pair.Value, "false", StringComparison.OrdinalIgnoreCase)) == true;

    private static string ReviewMessage(IReadOnlyDictionary<string, string> annotations) =>
        annotations.TryGetValue("nodal:review", out var message)
            ? message
            : "Source metadata requires semantic review.";

    private static IEnumerable<GraphModelValidationIssue> ClrIssues(GraphModelDescriptor descriptor)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in descriptor.Nodes.Select(node => (node.Id, node.ClrName, node.Properties, Segment: "nodes"))
                     .Concat(descriptor.Relations.Select(relation =>
                         (relation.Id, relation.ClrName, relation.Properties, Segment: "relations"))))
        {
            var path = $"$.{type.Segment}[{type.Id}]";
            if (!IsIdentifier(type.ClrName))
            {
                yield return Error("NODAL-MODEL-CLR-NAME", $"'{type.ClrName}' is not a safe C# type name.", $"{path}.clrName");
            }

            if (!typeNames.Add(type.ClrName))
            {
                yield return Error("NODAL-MODEL-CLR-COLLISION", $"CLR type name '{type.ClrName}' is duplicated.", $"{path}.clrName");
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in type.Properties)
            {
                var propertyPath = $"{path}.properties[{property.Name}].clrName";
                if (!IsIdentifier(property.ClrName))
                {
                    yield return Error("NODAL-MODEL-CLR-NAME", $"'{property.ClrName}' is not a safe C# property name.", propertyPath);
                }

                if (!propertyNames.Add(property.ClrName))
                {
                    yield return Error("NODAL-MODEL-CLR-COLLISION", $"CLR property name '{property.ClrName}' is duplicated.", propertyPath);
                }
            }
        }
    }

    private static GraphModelValidationIssue Error(string code, string message, string path) =>
        new(code, GraphModelIssueSeverity.Error, message, path);

    private static bool IsIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !CSharpKeywords.Contains(value) &&
        (value[0] == '_' || char.IsLetter(value[0])) &&
        value.Skip(1).All(character => character == '_' || char.IsLetterOrDigit(character));

    private static GraphModelValidationResult Result(string code, string message, string path) =>
        new([new GraphModelValidationIssue(code, GraphModelIssueSeverity.Error, message, path)]);
}
