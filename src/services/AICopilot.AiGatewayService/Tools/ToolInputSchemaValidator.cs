using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Tools;

internal sealed record ToolInputValidationResult(
    bool IsValid,
    string? Error,
    IReadOnlyDictionary<string, object?> Arguments,
    string? CanonicalJson)
{
    public static ToolInputValidationResult Success(
        IReadOnlyDictionary<string, object?> arguments,
        string canonicalJson)
    {
        return new ToolInputValidationResult(true, null, arguments, canonicalJson);
    }

    public static ToolInputValidationResult Failure(string error)
    {
        return new ToolInputValidationResult(
            false,
            error,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            null);
    }
}

internal static class ToolInputSchemaValidator
{
    public static ToolInputSchemaContractResult ValidateSchema(string? schemaJson) =>
        ToolInputSchemaContractV1.Validate(schemaJson);

    public static ToolInputValidationResult ValidateAndParse(
        string? inputJson,
        string? schemaJson)
    {
        var inputContract = AgentNodeToolInputContractV1.Normalize(inputJson);
        if (!inputContract.IsValid)
        {
            return ToolInputValidationResult.Failure(
                inputContract.Error ?? "Tool input JSON violates the node/tool input policy.");
        }

        JsonDocument inputDocument;
        try
        {
            inputDocument = JsonDocument.Parse(inputContract.CanonicalJson!);
        }
        catch (JsonException)
        {
            return ToolInputValidationResult.Failure("Tool input JSON is invalid.");
        }

        using (inputDocument)
        {
            var root = inputDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ToolInputValidationResult.Failure("Tool input must be a JSON object.");
            }

            var schemaContract = ValidateSchema(schemaJson);
            if (!schemaContract.IsValid)
            {
                return ToolInputValidationResult.Failure(
                    schemaContract.Error ?? "Tool registry input schema is invalid.");
            }

            using (var schemaDocument = JsonDocument.Parse(schemaContract.CanonicalJson!))
            {
                var schema = schemaDocument.RootElement;
                var validationError = ToolInputSchemaContractV1.ValidateValue(root, schema);
                if (validationError is not null)
                {
                    return ToolInputValidationResult.Failure(validationError);
                }

                var arguments = root.EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => ConvertJsonElement(property.Value),
                        StringComparer.Ordinal);
                return ToolInputValidationResult.Success(arguments, inputContract.CanonicalJson!);
            }
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonElement(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when ToolInputNumberContractV1.TryReadNumber(element, out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Number => throw new InvalidOperationException(
                "Validated tool numbers must fit the exact Int64/Decimal invocation domain."),
            _ => element.GetRawText()
        };
    }
}
