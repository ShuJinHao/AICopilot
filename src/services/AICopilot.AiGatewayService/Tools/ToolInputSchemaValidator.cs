using System.Text.Json;

namespace AICopilot.AiGatewayService.Tools;

internal sealed record ToolInputValidationResult(
    bool IsValid,
    string? Error,
    IReadOnlyDictionary<string, object?> Arguments)
{
    public static ToolInputValidationResult Success(IReadOnlyDictionary<string, object?> arguments)
    {
        return new ToolInputValidationResult(true, null, arguments);
    }

    public static ToolInputValidationResult Failure(string error)
    {
        return new ToolInputValidationResult(false, error, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }
}

internal static class ToolInputSchemaValidator
{
    private const int DefaultMaxInputJsonLength = 8000;

    public static ToolInputValidationResult ValidateAndParse(
        string? inputJson,
        string? schemaJson,
        int maxInputJsonLength = DefaultMaxInputJsonLength)
    {
        if (!string.IsNullOrWhiteSpace(inputJson) && inputJson.Length > maxInputJsonLength)
        {
            return ToolInputValidationResult.Failure("Tool input JSON exceeds the allowed length.");
        }

        JsonDocument inputDocument;
        try
        {
            inputDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        }
        catch (JsonException ex)
        {
            return ToolInputValidationResult.Failure($"Tool input JSON is invalid: {ex.Message}");
        }

        using (inputDocument)
        {
            var root = inputDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ToolInputValidationResult.Failure("Tool input must be a JSON object.");
            }

            var arguments = root.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonElement(property.Value),
                    StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(schemaJson))
            {
                return ToolInputValidationResult.Success(arguments);
            }

            JsonDocument schemaDocument;
            try
            {
                schemaDocument = JsonDocument.Parse(schemaJson);
            }
            catch (JsonException ex)
            {
                return ToolInputValidationResult.Failure($"Tool registry input schema is invalid: {ex.Message}");
            }

            using (schemaDocument)
            {
                var schema = schemaDocument.RootElement;
                if (schema.ValueKind != JsonValueKind.Object)
                {
                    return ToolInputValidationResult.Success(arguments);
                }

                var validationError = ValidateValue("$", root, schema);
                return validationError is null
                    ? ToolInputValidationResult.Success(arguments)
                    : ToolInputValidationResult.Failure(validationError);
            }
        }
    }

    private static string? ValidateValue(string path, JsonElement value, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (schema.TryGetProperty("type", out var typeElement) &&
            !MatchesSchemaType(value, typeElement))
        {
            return $"Tool input field '{path}' does not match registry schema.";
        }

        if (schema.TryGetProperty("enum", out var enumElement) &&
            enumElement.ValueKind == JsonValueKind.Array &&
            !enumElement.EnumerateArray().Any(item => JsonElementEquals(item, value)))
        {
            return $"Tool input field '{path}' is not one of the allowed values.";
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var requiredError = ValidateRequired(path, value, schema);
            if (requiredError is not null)
            {
                return requiredError;
            }

            if (schema.TryGetProperty("properties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertySchema in properties.EnumerateObject())
                {
                    if (!value.TryGetProperty(propertySchema.Name, out var propertyValue) ||
                        propertyValue.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    var childError = ValidateValue($"{path}.{propertySchema.Name}", propertyValue, propertySchema.Value);
                    if (childError is not null)
                    {
                        return childError;
                    }
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array &&
            schema.TryGetProperty("items", out var itemSchema) &&
            itemSchema.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                var itemError = ValidateValue($"{path}[{index}]", item, itemSchema);
                if (itemError is not null)
                {
                    return itemError;
                }

                index++;
            }
        }

        return null;
    }

    private static string? ValidateRequired(string path, JsonElement value, JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in required.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!value.TryGetProperty(name, out var propertyValue) ||
                propertyValue.ValueKind == JsonValueKind.Null)
            {
                return $"Tool input is missing required field '{FormatRequiredPath(path, name)}'.";
            }
        }

        return null;
    }

    private static string FormatRequiredPath(string path, string name)
    {
        if (path == "$")
        {
            return name;
        }

        if (path.StartsWith("$.", StringComparison.Ordinal))
        {
            return $"{path[2..]}.{name}";
        }

        if (path.StartsWith('$'))
        {
            return $"{path[1..]}.{name}";
        }

        return $"{path}.{name}";
    }

    private static bool MatchesSchemaType(JsonElement value, JsonElement typeElement)
    {
        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            return typeElement.EnumerateArray()
                .Any(item => item.ValueKind == JsonValueKind.String && MatchesSchemaType(value, item.GetString()));
        }

        return typeElement.ValueKind != JsonValueKind.String ||
               MatchesSchemaType(value, typeElement.GetString());
    }

    private static bool MatchesSchemaType(JsonElement value, string? schemaType)
    {
        return schemaType?.ToLowerInvariant() switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    private static bool JsonElementEquals(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        return expected.ValueKind switch
        {
            JsonValueKind.String => string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal),
            JsonValueKind.True or JsonValueKind.False => expected.GetBoolean() == actual.GetBoolean(),
            JsonValueKind.Null => true,
            _ => string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal)
        };
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonElement(property.Value),
                    StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
