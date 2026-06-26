using System.Text.Json;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentRuntimeStepInputReader
{
    public static string BuildResultErrorSummary(IResult result)
    {
        return result.Errors is null
            ? result.Status.ToString()
            : string.Join("; ", result.Errors.Select(error => error?.ToString()).Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    public static string? ReadString(string? inputJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int? ReadInt(string? inputJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
