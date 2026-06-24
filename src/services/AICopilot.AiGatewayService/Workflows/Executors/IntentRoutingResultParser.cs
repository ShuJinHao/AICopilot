using System.Text.Json;
using AICopilot.AiGatewayService.Models;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public static class IntentRoutingResultParser
{
    public static bool TryParse(string? text, out List<IntentResult> intents)
    {
        intents = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                intents = JsonSerializer.Deserialize<List<IntentResult>>(
                              document.RootElement.GetRawText(),
                              JsonSerializerOptions.Web)
                          ?? [];
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var intent = JsonSerializer.Deserialize<IntentResult>(
                    document.RootElement.GetRawText(),
                    JsonSerializerOptions.Web);
                if (intent is not null)
                {
                    intents = [intent];
                }
            }

            intents = intents
                .Select(NormalizeIntent)
                .Where(intent => !string.IsNullOrWhiteSpace(intent.Intent))
                .ToList();

            return intents.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IntentResult NormalizeIntent(IntentResult intent)
    {
        if (string.IsNullOrWhiteSpace(intent.Intent) &&
            !string.IsNullOrWhiteSpace(intent.SkillCode))
        {
            intent.Intent = $"Skill.{intent.SkillCode.Trim()}";
        }

        if (string.IsNullOrWhiteSpace(intent.Reasoning) &&
            !string.IsNullOrWhiteSpace(intent.Reason))
        {
            intent.Reasoning = intent.Reason.Trim();
        }

        return intent;
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

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            return trimmed[objectStart..(objectEnd + 1)];
        }

        return null;
    }
}
