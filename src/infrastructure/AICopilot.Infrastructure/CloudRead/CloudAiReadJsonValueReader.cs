using System.Globalization;
using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.CloudRead;

internal static class CloudAiReadJsonValueReader
{
    public static IReadOnlyList<JsonElement> ExtractRecords(
        JsonElement root,
        int limit,
        CloudAiReadOperation operation)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProviderContract();
        }

        var records = items.EnumerateArray().Select(item => item.Clone()).ToArray();
        CloudAiReadProviderItemContractValidator.Validate(operation, records);
        return records
            .Take(CloudAiReadRowLimitPolicy.Normalize(limit))
            .ToArray();
    }

    public static CloudAiReadEnvelopeMetadata ReadEnvelopeMetadata(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("asOfUtc", out var asOfUtcElement)
            || asOfUtcElement.ValueKind != JsonValueKind.String
            || !asOfUtcElement.TryGetDateTimeOffset(out var asOfUtc)
            || !root.TryGetProperty("source", out var sourceElement)
            || sourceElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(sourceElement.GetString())
            || !root.TryGetProperty("queryScope", out var queryScopeElement)
            || queryScopeElement.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("rowCount", out var rowCountElement)
            || rowCountElement.ValueKind != JsonValueKind.Number
            || !rowCountElement.TryGetInt32(out var rowCount)
            || rowCount < 0
            || items.GetArrayLength() != rowCount
            || !root.TryGetProperty("truncated", out var truncatedElement)
            || truncatedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("nextCursor", out var nextCursorElement)
            || nextCursorElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw InvalidProviderContract();
        }

        var nextCursor = nextCursorElement.ValueKind == JsonValueKind.String
            ? nextCursorElement.GetString()
            : null;

        return new CloudAiReadEnvelopeMetadata(
            asOfUtc.ToUniversalTime(),
            sourceElement.GetString()!,
            queryScopeElement.GetString() ?? string.Empty,
            rowCount,
            truncatedElement.GetBoolean(),
            nextCursor);
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

    public static string GetRequiredString(JsonElement record, string name)
    {
        return record.GetProperty(name).GetString()!;
    }

    public static Guid GetRequiredGuid(JsonElement record, string name)
    {
        return Guid.Parse(record.GetProperty(name).GetString()!);
    }

    public static DateOnly GetRequiredDateOnly(JsonElement record, string name)
    {
        return DateOnly.ParseExact(
            record.GetProperty(name).GetString()!,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);
    }

    public static DateTime GetRequiredDate(JsonElement record, string name)
    {
        return record.GetProperty(name).GetDateTime();
    }

    public static int GetRequiredInt(JsonElement record, string name)
    {
        return record.GetProperty(name).GetInt32();
    }

    public static decimal GetRequiredDecimal(JsonElement record, string name)
    {
        return record.GetProperty(name).GetDecimal();
    }

    public static bool GetRequiredBoolean(JsonElement record, string name)
    {
        return record.GetProperty(name).GetBoolean();
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

    public static int? GetInt(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (property.ValueKind == JsonValueKind.String &&
                int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static DateTime? GetDate(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (!record.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String && property.TryGetDateTime(out var parsed))
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

    public static IReadOnlyDictionary<string, object?> GetObject(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (record.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object)
            {
                return property.EnumerateObject()
                    .ToDictionary(
                        item => item.Name,
                        item => ToObject(item.Value),
                        StringComparer.Ordinal);
            }
        }

        return new Dictionary<string, object?>();
    }

    public static IReadOnlyList<JsonElement> GetObjectArray(JsonElement record, params string[] names)
    {
        foreach (var name in names)
        {
            if (record.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => item.Clone())
                    .ToArray();
            }
        }

        return [];
    }

    internal static CloudAiReadException InvalidProviderContract()
        => new(
            CloudAiReadProblemCodes.Unavailable,
            "Cloud AiRead endpoint returned an invalid provider contract.");

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
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ToObject(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ToObject)
                .ToArray(),
            _ => element.GetRawText()
        };
    }
}

internal sealed record CloudAiReadEnvelopeMetadata(
    DateTimeOffset AsOfUtc,
    string Source,
    string QueryScope,
    int RowCount,
    bool Truncated,
    string? NextCursor);
