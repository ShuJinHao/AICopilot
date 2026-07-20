using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

/// <summary>
/// Shared deterministic schema walker for tool input and output contracts.
/// Public contract owners provide their own version, digest, limits, and error
/// vocabulary; this engine is the single implementation of the supported P0
/// JSON Schema subset and runtime value matching rules.
/// </summary>
internal static class ToolStrictSchemaEngineV1
{
    internal const string EngineVersion = "tool-strict-schema-engine:v1";
    internal const int MaxPropertyNameCharacters = 120;
    internal const int MaxEnumDisplayCharacters = 240;

    internal static readonly IReadOnlySet<string> SupportedSchemaKeywords = new HashSet<string>(
        ["type", "properties", "required", "enum", "items", "description", "title", "additionalProperties"],
        StringComparer.Ordinal);

    internal static readonly IReadOnlySet<string> SupportedTypes = new HashSet<string>(
        ["array", "boolean", "integer", "number", "object", "string"],
        StringComparer.Ordinal);

    internal static ToolStrictSchemaDefinitionResult ValidateDefinition(
        string? schemaJson,
        string contractLabel,
        bool requireObjectRoot)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return ToolStrictSchemaDefinitionResult.Failure(
                $"{contractLabel} is required and must use the supported strict subset.");
        }

        string canonicalSchema;
        try
        {
            canonicalSchema = AgentCanonicalJsonV1.Canonicalize(schemaJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return ToolStrictSchemaDefinitionResult.Failure($"{contractLabel} is invalid.");
        }

        try
        {
            using var document = JsonDocument.Parse(canonicalSchema);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ToolStrictSchemaDefinitionResult.Failure(
                    $"{contractLabel} must be an object schema from the supported strict subset.");
            }

            var error = ValidateSchemaDefinition(
                "$schema",
                root,
                contractLabel,
                requireObjectRoot);
            return error is null
                ? ToolStrictSchemaDefinitionResult.Success(canonicalSchema)
                : ToolStrictSchemaDefinitionResult.Failure(error);
        }
        catch (JsonException)
        {
            return ToolStrictSchemaDefinitionResult.Failure($"{contractLabel} is invalid.");
        }
    }

    internal static string? ValidateValue(
        string path,
        JsonElement value,
        JsonElement schema,
        string payloadLabel)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return $"{payloadLabel} schema at '{path}' is outside the supported strict subset.";
        }

        if (schema.TryGetProperty("type", out var typeElement) &&
            !MatchesSchemaType(value, typeElement))
        {
            return $"{payloadLabel} field '{path}' does not match registry schema.";
        }

        if (schema.TryGetProperty("enum", out var enumElement) &&
            enumElement.ValueKind == JsonValueKind.Array &&
            !enumElement.EnumerateArray().Any(item => JsonElementEquals(item, value)))
        {
            return $"{payloadLabel} field '{path}' is not one of the allowed values.";
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var requiredError = ValidateRequired(path, value, schema, payloadLabel);
            if (requiredError is not null)
            {
                return requiredError;
            }

            var declaredNames = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var propertySchema in properties.EnumerateObject())
                {
                    declaredNames.Add(propertySchema.Name);
                    if (!value.TryGetProperty(propertySchema.Name, out var propertyValue))
                    {
                        continue;
                    }

                    var childError = ValidateValue(
                        $"{path}.{propertySchema.Name}",
                        propertyValue,
                        propertySchema.Value,
                        payloadLabel);
                    if (childError is not null)
                    {
                        return childError;
                    }
                }
            }

            foreach (var property in value.EnumerateObject())
            {
                if (!declaredNames.Contains(property.Name))
                {
                    // The undeclared name belongs to the provider-controlled payload.
                    // Never echo it into a step failure, task summary, audit record, or DTO.
                    return $"{payloadLabel} contains an undeclared field at schema path '{path}'.";
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
                var itemError = ValidateValue($"{path}[{index}]", item, itemSchema, payloadLabel);
                if (itemError is not null)
                {
                    return itemError;
                }

                index++;
            }
        }

        return null;
    }

    private static string? ValidateSchemaDefinition(
        string path,
        JsonElement schema,
        string contractLabel,
        bool requireObjectRoot = false)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return $"{contractLabel} at '{path}' must be an object.";
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (!SupportedSchemaKeywords.Contains(property.Name))
            {
                return $"{contractLabel} keyword '{property.Name}' is not supported by the strict P0 subset.";
            }
        }

        if (!schema.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            !SupportedTypes.Contains(typeElement.GetString()!))
        {
            return $"{contractLabel} at '{path}' requires one supported string type.";
        }

        var type = typeElement.GetString()!;
        if (requireObjectRoot && !string.Equals(type, "object", StringComparison.Ordinal))
        {
            return $"{contractLabel} root type must be object.";
        }

        if (schema.TryGetProperty("description", out var description) &&
            description.ValueKind != JsonValueKind.String ||
            schema.TryGetProperty("title", out var title) && title.ValueKind != JsonValueKind.String)
        {
            return $"{contractLabel} metadata at '{path}' must be strings.";
        }

        if (schema.TryGetProperty("additionalProperties", out var additionalProperties) &&
            additionalProperties.ValueKind != JsonValueKind.False)
        {
            return $"{contractLabel} only supports additionalProperties=false semantics.";
        }

        var declaredProperties = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("properties", out var properties))
        {
            if (!string.Equals(type, "object", StringComparison.Ordinal) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                return $"{contractLabel} properties at '{path}' require type=object.";
            }

            foreach (var property in properties.EnumerateObject())
            {
                if (!IsSafePropertyName(property.Name))
                {
                    return $"{contractLabel} property name at '{path}' is outside the safe exact catalog domain.";
                }

                declaredProperties.Add(property.Name);
                var childError = ValidateSchemaDefinition(
                    $"{path}.properties.{property.Name}",
                    property.Value,
                    contractLabel);
                if (childError is not null)
                {
                    return childError;
                }
            }
        }

        if (schema.TryGetProperty("required", out var required))
        {
            if (!string.Equals(type, "object", StringComparison.Ordinal) ||
                required.ValueKind != JsonValueKind.Array)
            {
                return $"{contractLabel} required at '{path}' requires type=object.";
            }

            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in required.EnumerateArray())
            {
                var name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) ||
                    !requiredNames.Add(name) ||
                    !declaredProperties.Contains(name))
                {
                    return $"{contractLabel} required at '{path}' must be a unique subset of properties.";
                }
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            if (!string.Equals(type, "array", StringComparison.Ordinal))
            {
                return $"{contractLabel} items at '{path}' require type=array.";
            }

            var itemError = ValidateSchemaDefinition($"{path}.items", items, contractLabel);
            if (itemError is not null)
            {
                return itemError;
            }
        }
        else if (string.Equals(type, "array", StringComparison.Ordinal))
        {
            return $"{contractLabel} array at '{path}' requires an items schema.";
        }

        if (schema.TryGetProperty("enum", out var enumElement))
        {
            if (enumElement.ValueKind != JsonValueKind.Array || enumElement.GetArrayLength() == 0)
            {
                return $"{contractLabel} enum at '{path}' must be a non-empty array.";
            }

            var enumValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in enumElement.EnumerateArray())
            {
                if (!MatchesDeclaredType(item, type) ||
                    !IsSafeEnumDisplay(item) ||
                    !enumValues.Add(AgentCanonicalJsonV1.Canonicalize(item)))
                {
                    return $"{contractLabel} enum at '{path}' must contain unique, bounded values matching its declared type.";
                }
            }
        }

        return null;
    }

    private static string? ValidateRequired(
        string path,
        JsonElement value,
        JsonElement schema,
        string payloadLabel)
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
                return $"{payloadLabel} is missing required field '{FormatRequiredPath(path, name)}'.";
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

    private static bool IsSafePropertyName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
               name.Length <= MaxPropertyNameCharacters &&
               string.Equals(name, name.Trim(), StringComparison.Ordinal) &&
               !name.Any(char.IsWhiteSpace) &&
               !name.Any(char.IsControl);
    }

    private static bool IsSafeEnumDisplay(JsonElement value)
    {
        var display = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
        return display.Length <= MaxEnumDisplayCharacters &&
               !display.Any(char.IsControl);
    }

    private static bool MatchesDeclaredType(JsonElement value, string type)
    {
        return type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => ToolInputNumberContractV1.TryReadNumber(value, out _),
            "integer" => ToolInputNumberContractV1.TryReadInteger(value, out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => false
        };
    }

    private static bool MatchesSchemaType(JsonElement value, JsonElement typeElement)
    {
        return typeElement.ValueKind == JsonValueKind.String &&
               MatchesDeclaredType(value, typeElement.GetString()!);
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
}

internal sealed record ToolStrictSchemaDefinitionResult(
    bool IsValid,
    string? Error,
    string? CanonicalJson)
{
    internal static ToolStrictSchemaDefinitionResult Success(string canonicalJson) =>
        new(true, null, canonicalJson);

    internal static ToolStrictSchemaDefinitionResult Failure(string error) =>
        new(false, error, null);
}
