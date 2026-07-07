using System.Text.Json;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentDynamicPlannerResponseParser
{
    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "steps"
    };

    private static readonly HashSet<string> AllowedStepProperties = new(StringComparer.Ordinal)
    {
        "title",
        "description",
        "stepType",
        "toolCode",
        "requiresApproval",
        "inputJson"
    };

    internal static JsonDocument ParsePlannerResponse(StructuredAgentResponse<JsonElement> response)
    {
        if (response.Result.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            return JsonDocument.Parse(response.Result.GetRawText());
        }

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new JsonException("Planner response was empty.");
        }

        var text = response.Text.Trim();
        if (text.Length > AgentDynamicPlannerLimits.MaxPlannerResponseTextLength)
        {
            throw new JsonException("Planner response exceeds the allowed length.");
        }

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            throw new JsonException("Planner response must be raw JSON and must not be wrapped in Markdown.");
        }

        return JsonDocument.Parse(text);
    }

    internal static Result<IReadOnlyCollection<AgentStepPlanDto>> ParsePlanDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("Planner output root must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!AllowedRootProperties.Contains(property.Name))
            {
                return Invalid($"Planner output contains unknown root field '{property.Name}'.");
            }
        }

        if (!root.TryGetProperty("steps", out var stepsElement) ||
            stepsElement.ValueKind != JsonValueKind.Array)
        {
            return Invalid("Planner output must contain a steps array.");
        }

        var steps = new List<AgentStepPlanDto>();
        foreach (var stepElement in stepsElement.EnumerateArray())
        {
            if (steps.Count >= AgentDynamicPlannerLimits.MaxDynamicSteps)
            {
                return Invalid($"Planner output exceeds the maximum of {AgentDynamicPlannerLimits.MaxDynamicSteps} steps.");
            }

            var stepResult = ParseStep(stepElement);
            if (!stepResult.IsSuccess)
            {
                return Result.From(stepResult);
            }

            steps.Add(stepResult.Value!);
        }

        if (steps.Count == 0)
        {
            return Invalid("Planner output must contain at least one step.");
        }

        return Result.Success<IReadOnlyCollection<AgentStepPlanDto>>(steps);
    }

    private static Result<AgentStepPlanDto> ParseStep(JsonElement stepElement)
    {
        if (stepElement.ValueKind != JsonValueKind.Object)
        {
            return InvalidStep("Planner step must be a JSON object.");
        }

        foreach (var property in stepElement.EnumerateObject())
        {
            if (!AllowedStepProperties.Contains(property.Name))
            {
                return InvalidStep($"Planner step contains unknown field '{property.Name}'.");
            }
        }

        var title = ReadRequiredString(stepElement, "title");
        var toolCode = ReadRequiredString(stepElement, "toolCode");
        if (title is null || toolCode is null)
        {
            return InvalidStep("Planner step title and toolCode are required.");
        }

        var description = ReadString(stepElement, "description") ?? title;
        if (!TryReadStepType(stepElement, out var stepType))
        {
            return InvalidStep("Planner step has an invalid stepType.");
        }

        var requiresApproval = ReadBool(stepElement, "requiresApproval") ?? false;
        var inputResult = ReadInputJson(stepElement);
        if (!inputResult.IsSuccess)
        {
            return Result.From(inputResult);
        }

        return Result.Success(new AgentStepPlanDto(
            title,
            description,
            stepType,
            toolCode,
            requiresApproval,
            inputResult.Value));
    }

    private static string? ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? CloudReadonlyAgentTextGuard.SanitizeForPlan(property.GetString(), 1000)
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static bool TryReadStepType(JsonElement element, out AgentStepType stepType)
    {
        stepType = default;
        if (!element.TryGetProperty("stepType", out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return Enum.TryParse(property.GetString(), ignoreCase: true, out stepType);
        }

        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value) ||
            !Enum.IsDefined(typeof(AgentStepType), value))
        {
            return false;
        }

        stepType = (AgentStepType)value;
        return true;
    }

    private static Result<string?> ReadInputJson(JsonElement stepElement)
    {
        if (!stepElement.TryGetProperty("inputJson", out var property) ||
            property.ValueKind == JsonValueKind.Null ||
            property.ValueKind == JsonValueKind.Undefined)
        {
            return Result.Success<string?>(null);
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            var rawObject = property.GetRawText();
            return rawObject.Length > AgentDynamicPlannerLimits.MaxStepInputJsonLength
                ? InvalidInput("Planner step inputJson exceeds the allowed length.")
                : Result.Success<string?>(rawObject);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Result.Success<string?>(null);
            }

            if (raw.Length > AgentDynamicPlannerLimits.MaxStepInputJsonLength)
            {
                return InvalidInput("Planner step inputJson exceeds the allowed length.");
            }

            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return InvalidInput("Planner step inputJson must be a JSON object.");
                }

                return Result.Success<string?>(document.RootElement.GetRawText());
            }
            catch (JsonException)
            {
                return InvalidInput("Planner step inputJson is invalid JSON.");
            }
        }

        return InvalidInput("Planner step inputJson must be omitted, a JSON object, or a JSON object string.");
    }

    private static Result<IReadOnlyCollection<AgentStepPlanDto>> Invalid(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static Result<AgentStepPlanDto> InvalidStep(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static Result<string?> InvalidInput(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }
}
