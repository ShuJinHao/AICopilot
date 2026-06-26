using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlySandboxAgentTrialQueryProjection
{
    private static readonly string[] artifactTypes = ["Chart", "Markdown", "Html", "Pdf", "Pptx", "Xlsx"];

    public static IReadOnlyDictionary<string, string?> BuildQuery(
        CloudReadonlySandboxAgentTrialScenarioCatalog.SandboxTrialScenario scenario,
        int maxRows)
    {
        return new Dictionary<string, string?>
        {
            ["scenarioId"] = scenario.Id,
            ["maxRows"] = maxRows.ToString(),
            ["boundary"] = CloudReadonlySandboxAgentTrialMarkers.Boundary
        };
    }

    public static IReadOnlyCollection<string> NormalizeArtifactTypes(
        IReadOnlyCollection<string>? requested,
        IReadOnlyCollection<string> defaults)
    {
        var allowed = new HashSet<string>(artifactTypes, StringComparer.OrdinalIgnoreCase);
        var values = requested is { Count: > 0 } ? requested : defaults;
        return values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Where(item => allowed.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static (IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows, bool IsTruncated) ExtractRows(
        JsonElement root,
        int maxRows,
        string endpointCode)
    {
        var sourceRows = EnumerateRows(root).ToArray();
        var isTruncated = ReadIsTruncated(root) || sourceRows.Length > maxRows;
        var rows = sourceRows
            .Take(maxRows)
            .Select(row =>
            {
                var normalized = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceType"] = CloudReadonlySandboxAgentTrialMarkers.SourceType,
                    ["sourceMode"] = CloudReadonlySandboxAgentTrialMarkers.SourceMode,
                    ["isSandbox"] = true,
                    ["isSimulation"] = false,
                    ["sourceLabel"] = CloudReadonlySandboxAgentTrialMarkers.SourceLabel,
                    ["boundary"] = CloudReadonlySandboxAgentTrialMarkers.Boundary,
                    ["endpointCode"] = endpointCode
                };
                return (IReadOnlyDictionary<string, object?>)normalized;
            })
            .ToArray();

        return (rows, isTruncated);
    }

    public static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> EnumerateRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return ReadObject(item);
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var propertyName in new[] { "items", "rows", "data" })
        {
            if (root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    yield return ReadObject(item);
                }

                yield break;
            }
        }

        yield return ReadObject(root);
    }

    private static bool ReadIsTruncated(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty("isTruncated", out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static IReadOnlyDictionary<string, object?> ReadObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?> { ["value"] = ReadValue(element) };
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ReadValue(property.Value);
        }

        return result;
    }

    private static object? ReadValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }
}
