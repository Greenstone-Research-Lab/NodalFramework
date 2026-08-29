using System.Collections;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Nodal.Analytics.Observations;

internal static class ObservationValueFreezer
{
    public static IReadOnlyDictionary<string, object?> Project(
        IReadOnlyDictionary<string, object?> properties,
        IReadOnlySet<string> projection,
        int maximumCollectionItems,
        int maximumDepth)
    {
        var selected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propertyName in projection)
        {
            if (properties.TryGetValue(propertyName, out var value))
            {
                selected[propertyName] = Freeze(value, propertyName, maximumCollectionItems, maximumDepth);
            }
        }

        return selected.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static object? Freeze(
        object? value,
        string propertyName,
        int maximumCollectionItems,
        int remainingDepth)
    {
        if (value is null || value is string || value.GetType().IsValueType)
        {
            return value is JsonElement json ? json.Clone() : value;
        }

        if (value is IReadOnlyDictionary<string, object?> dictionary)
        {
            EnsureCompositeAllowed(propertyName, remainingDepth);
            EnsureCollectionWithinLimit(propertyName, dictionary.Count, maximumCollectionItems);
            return dictionary.ToFrozenDictionary(
                item => item.Key,
                item => Freeze(item.Value, propertyName, maximumCollectionItems, remainingDepth - 1),
                StringComparer.Ordinal);
        }

        if (value is IEnumerable sequence)
        {
            EnsureCompositeAllowed(propertyName, remainingDepth);
            var frozen = new List<object?>();
            foreach (var item in sequence)
            {
                EnsureCollectionWithinLimit(propertyName, frozen.Count + 1, maximumCollectionItems);
                frozen.Add(Freeze(item, propertyName, maximumCollectionItems, remainingDepth - 1));
            }

            return Array.AsReadOnly(frozen.ToArray());
        }

        throw new InvalidOperationException(
            $"Projected property '{propertyName}' uses unsupported reference type '{value.GetType().FullName}'.");
    }

    private static void EnsureCompositeAllowed(string propertyName, int remainingDepth)
    {
        if (remainingDepth <= 0)
        {
            throw new InvalidOperationException(
                $"Projected property '{propertyName}' exceeds the configured nesting depth.");
        }
    }

    private static void EnsureCollectionWithinLimit(string propertyName, int count, int maximumCollectionItems)
    {
        if (count > maximumCollectionItems)
        {
            throw new InvalidOperationException(
                $"Projected property '{propertyName}' exceeds the configured collection-item limit.");
        }
    }
}
