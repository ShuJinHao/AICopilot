using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal static class CloudReadonlyProductionControlledPilotQueryProjection
{
    public static IReadOnlyDictionary<string, string?> BuildQuery(
        CloudProductionGoalIntentDto intent,
        CloudReadonlyProductionControlledEndpointSpec endpoint,
        int maxRows)
    {
        var query = new Dictionary<string, string?>
        {
            ["maxRows"] = maxRows.ToString()
        };

        if (CloudReadonlyProductionControlledPilotGoalPolicy.RequiresDeviceId(endpoint.Code))
        {
            if (intent.DeviceId is null || intent.DeviceId == Guid.Empty)
            {
                throw new CloudAiReadException(
                    CloudAiReadProblemCodes.MissingRequiredParameter,
                    $"P13 endpoint '{endpoint.Code}' requires deviceId.");
            }

            query["deviceId"] = intent.DeviceId.Value.ToString();
        }

        if (endpoint.Code == "capacity_summary")
        {
            query["startDate"] = intent.TimeRange.From?.UtcDateTime.ToString("yyyy-MM-dd");
            query["endDate"] = intent.TimeRange.To?.UtcDateTime.ToString("yyyy-MM-dd");
        }
        else if (endpoint.Code is "device_logs" or "pass_station_records")
        {
            query["startTime"] = intent.TimeRange.From?.ToUniversalTime().ToString("O");
            query["endTime"] = intent.TimeRange.To?.ToUniversalTime().ToString("O");
        }

        return query;
    }

    public static (IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows, bool IsTruncated) ExtractRows(
        JsonElement root,
        int maxRows,
        string endpointCode,
        string pilotWindowId,
        string intentId)
    {
        var sourceRows = EnumerateRows(root).ToArray();
        var isTruncated = ReadIsTruncated(root) || sourceRows.Length > maxRows;
        var rows = sourceRows
            .Take(maxRows)
            .Select(row =>
            {
                var normalized = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceType"] = CloudReadonlyProductionControlledPilotMarkers.SourceType,
                    ["sourceMode"] = CloudReadonlyProductionControlledPilotMarkers.SourceMode,
                    ["isProductionData"] = true,
                    ["isSandbox"] = false,
                    ["isSimulation"] = false,
                    ["sourceLabel"] = CloudReadonlyProductionControlledPilotMarkers.SourceLabel,
                    ["boundary"] = CloudReadonlyProductionControlledPilotMarkers.Boundary,
                    ["pilotWindowId"] = pilotWindowId,
                    ["intentId"] = intentId,
                    ["endpointCode"] = endpointCode
                };
                return (IReadOnlyDictionary<string, object?>)normalized;
            })
            .ToArray();

        return (rows, isTruncated);
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
