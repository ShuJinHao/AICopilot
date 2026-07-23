using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public static class IntentRoutingResultParser
{
    private static readonly IReadOnlySet<string> AllowedProperties = new HashSet<string>(
        ["intent", "confidence", "query"],
        StringComparer.Ordinal);

    private static readonly JsonSerializerOptions StrictOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static bool TryParse(string? text, out List<IntentResult> intents)
    {
        intents = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleanText = ModelOutputSanitizer.Strip(text).CleanText;
        var json = ExtractJson(cleanText);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() is < 1 or > 16 ||
                document.RootElement.EnumerateArray().Any(item => !HasStrictShape(item)))
            {
                return false;
            }

            intents = JsonSerializer.Deserialize<List<IntentResult>>(
                          document.RootElement.GetRawText(),
                          StrictOptions)
                      ?? [];

            intents = intents
                .Where(intent => !string.IsNullOrWhiteSpace(intent.Intent))
                .ToList();

            return intents.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasStrictShape(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var properties = item.EnumerateObject().ToArray();
        if (properties.Length is < 2 or > 3 ||
            properties.Any(property => !AllowedProperties.Contains(property.Name)) ||
            properties.GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            !item.TryGetProperty("intent", out var intent) ||
            intent.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(intent.GetString()) ||
            !item.TryGetProperty("confidence", out var confidence) ||
            confidence.ValueKind != JsonValueKind.Number ||
            !confidence.TryGetDouble(out var score) ||
            double.IsNaN(score) ||
            double.IsInfinity(score) ||
            score is < 0 or > 1)
        {
            return false;
        }

        return !item.TryGetProperty("query", out var query) ||
               query.ValueKind is JsonValueKind.Null or JsonValueKind.String;
    }

    private static string? ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                trimmed = trimmed[(firstLineEnd + 1)..].Trim();
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].Trim();
            }
        }

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return trimmed[arrayStart..(arrayEnd + 1)];
        }

        return null;
    }
}
