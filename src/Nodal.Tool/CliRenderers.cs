using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nodal.Core.Migrations;
using Nodal.Migrations;

namespace Nodal.Tool;

internal enum CliOutputFormat
{
    Text,
    Json,
    GitHub,
}

internal static class CliRenderers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string RenderDiff(NodalSchemaDiffResult diff, CliOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return format == CliOutputFormat.Json
            ? JsonSerializer.Serialize(diff, JsonOptions)
            : format == CliOutputFormat.GitHub
                ? RenderGitHubDiff(diff)
                : RenderTextDiff(diff);
    }

    public static string RenderPlan(NodalSchemaMigrationPlan plan, CliOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return format switch
        {
            CliOutputFormat.Json => NodalSchemaMigrationPlanSerializer.Serialize(plan),
            CliOutputFormat.GitHub => RenderGitHubPlan(plan),
            _ => NodalSchemaMigrationPlanSerializer.ToMarkdown(plan),
        };
    }

    public static string RenderBundleList(
        IReadOnlyList<NodalMigrationBundle> bundles,
        CliOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        var items = bundles
            .OrderBy(bundle => bundle.MigrationId, StringComparer.Ordinal)
            .Select(bundle => new BundleListItem(
                bundle.MigrationId,
                bundle.ProviderName,
                bundle.ProviderVersion,
                bundle.FrameworkVersion,
                bundle.Checksum,
                bundle.Commands.Count,
                bundle.Commands.Any(command => command.Destructive)))
            .ToArray();

        return format switch
        {
            CliOutputFormat.Json => JsonSerializer.Serialize(items, JsonOptions),
            CliOutputFormat.GitHub => RenderGitHubBundles(items),
            _ => RenderTextBundles(items),
        };
    }

    public static string RenderBundleExecution(
        NodalMigrationBundleExecutionResult result,
        CliOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(result);
        return format switch
        {
            CliOutputFormat.Json => JsonSerializer.Serialize(result, JsonOptions),
            CliOutputFormat.GitHub =>
                $"::notice title=Nodal migration execution::{Escape(RenderExecutionText(result))}",
            _ => RenderExecutionText(result),
        };
    }

    private static string RenderTextDiff(NodalSchemaDiffResult diff)
    {
        if (diff.IsEmpty)
        {
            return "No schema changes.";
        }

        var output = new StringBuilder();
        foreach (var change in diff.Changes)
        {
            output.Append(change.Kind).Append(": ").Append(change.ObjectName);
            if (change.PropertyName is not null)
            {
                output.Append('.').Append(change.PropertyName);
            }

            if (change.NewPropertyName is not null)
            {
                output.Append(" -> ").Append(change.NewPropertyName);
            }

            if (change.Detail is not null)
            {
                output.Append(" (").Append(change.Detail).Append(')');
            }

            output.AppendLine();
        }

        return output.ToString();
    }

    private static string RenderGitHubDiff(NodalSchemaDiffResult diff)
    {
        if (diff.IsEmpty)
        {
            return "::notice title=Nodal schema diff::No schema changes.";
        }

        return string.Join(Environment.NewLine, diff.Changes.Select(change =>
            $"::notice title=Nodal schema diff::{Escape($"{change.Kind}: {change.ObjectName}" +
                (change.PropertyName is null ? string.Empty : $".{change.PropertyName}"))}"));
    }

    private static string RenderGitHubPlan(NodalSchemaMigrationPlan plan)
    {
        var output = new StringBuilder()
            .Append("::notice title=Nodal migration plan::Operations: ")
            .Append(plan.Operations.Count)
            .Append("; manual review: ")
            .Append(plan.ManualReview.Count);
        foreach (var change in plan.ManualReview)
        {
            output.AppendLine().Append("::warning title=Nodal manual review::")
                .Append(Escape($"{change.Kind}: {change.ObjectName}"));
        }

        return output.ToString();
    }

    private static string RenderTextBundles(IReadOnlyList<BundleListItem> items) =>
        items.Count == 0
            ? "No migration bundles."
            : string.Join(Environment.NewLine, items.Select(item =>
                $"{item.MigrationId} {item.ProviderName}@{item.ProviderVersion} " +
                $"commands={item.CommandCount} destructive={item.HasDestructiveCommands.ToString().ToLowerInvariant()} " +
                $"checksum={item.Checksum}"));

    private static string RenderGitHubBundles(IReadOnlyList<BundleListItem> items) =>
        items.Count == 0
            ? "::notice title=Nodal migration bundles::No migration bundles."
            : string.Join(Environment.NewLine, items.Select(item =>
                $"::notice title=Nodal migration bundle::{Escape($"{item.MigrationId} " +
                    $"{item.ProviderName}@{item.ProviderVersion} checksum={item.Checksum}")}"));

    private static string RenderExecutionText(NodalMigrationBundleExecutionResult result) =>
        $"{result.Outcome}: {result.MigrationId} commands={result.CommandCount} checksum={result.Checksum}";

    private static string Escape(string value) => value
        .Replace("%", "%25", StringComparison.Ordinal)
        .Replace("\r", "%0D", StringComparison.Ordinal)
        .Replace("\n", "%0A", StringComparison.Ordinal);

    private sealed record BundleListItem(
        string MigrationId,
        string ProviderName,
        string ProviderVersion,
        string FrameworkVersion,
        string Checksum,
        int CommandCount,
        bool HasDestructiveCommands);
}
