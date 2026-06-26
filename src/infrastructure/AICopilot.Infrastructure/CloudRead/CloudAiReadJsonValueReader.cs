using System.Globalization;
using System.Text.Json;

namespace AICopilot.Infrastructure.CloudRead;

internal static class CloudAiReadJsonValueReader
{
    public static IReadOnlyList<JsonElement> ExtractRecords(JsonElement root, int limit)
    {
        var effectiveLimit = Math.Max(1, limit);
        var source = EnumerateRecords(root);

        return source.Take(effectiveLimit).Select(item => item.Clone()).ToArray();
    }

    public static bool IsTruncated(JsonElement root, int itemCount, int limit)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            TryGetBoolean(root, out var isTruncated, "isTruncated", "truncated"))
        {
            return isTruncated;
        }

        return itemCount >= Math.Max(1, limit);
    }

    public static string? GetString(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return null;
    }

    public static decimal? GetDecimal(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
            {
                return decimalValue;
            }

            if (property.ValueKind == JsonValueKind.String &&
                decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static DateTimeOffset? GetDate(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, object?> ExtractAdditionalFields(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>();
        }

        return record.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => ToObject(property.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<JsonElement> EnumerateRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        if (root.ValueKind == JsonValueKind.Object && TryGetArray(root, out var array))
        {
            return array.EnumerateArray();
        }

        return root.ValueKind == JsonValueKind.Object ? [root] : [];
    }

    private static bool TryGetArray(JsonElement root, out JsonElement array)
    {
        foreach (var name in new[] { "items", "data", "records", "results" })
        {
            if (root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        array = default;
        return false;
    }

    private static bool TryGetBoolean(JsonElement record, out bool value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }
        }

        value = false;
        return false;
    }

    private static object? ToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
