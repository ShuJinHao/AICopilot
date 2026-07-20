using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

public sealed record ToolInputSchemaContractResult(
    bool IsValid,
    string? Error,
    string? CanonicalJson)
{
    public static ToolInputSchemaContractResult Success(string canonicalJson) =>
        new(true, null, canonicalJson);

    public static ToolInputSchemaContractResult Failure(string error) =>
        new(false, error, null);
}

/// <summary>
/// Versioned input-schema authority. Definition and value walking are delegated
/// to the shared strict engine so input and output cannot evolve two subtly
/// different JSON Schema subsets.
/// </summary>
public static class ToolInputSchemaContractV1
{
    public const string ContractVersion = "tool-input-schema-contract:v1";
    public const int MaxPropertyNameCharacters = ToolStrictSchemaEngineV1.MaxPropertyNameCharacters;
    public const int MaxEnumDisplayCharacters = ToolStrictSchemaEngineV1.MaxEnumDisplayCharacters;

    public static readonly string ContractDigest = ComputeDigest(new
    {
        version = ContractVersion,
        engine = ToolStrictSchemaEngineV1.EngineVersion,
        rootType = "object",
        supportedKeywords = ToolStrictSchemaEngineV1.SupportedSchemaKeywords
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray(),
        supportedTypes = ToolStrictSchemaEngineV1.SupportedTypes
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray(),
        additionalProperties = "false-or-omitted-with-strict-false-runtime-semantics",
        required = "unique-subset-of-properties",
        arrays = "typed-items-required",
        enums = "nonempty-unique-and-type-matched",
        maxPropertyNameCharacters = MaxPropertyNameCharacters,
        propertyNames = "nonempty-exact-trimmed-no-whitespace-or-control-characters",
        maxEnumDisplayCharacters = MaxEnumDisplayCharacters,
        numericProjection = ToolInputNumberContractV1.ContractVersion,
        canonicalJsonPolicy = AgentCanonicalJsonV1.PolicyVersion
    });

    public static ToolInputSchemaContractResult Validate(string? schemaJson)
    {
        var result = ToolStrictSchemaEngineV1.ValidateDefinition(
            schemaJson,
            "Tool registry input schema",
            requireObjectRoot: true);
        return result.IsValid
            ? ToolInputSchemaContractResult.Success(result.CanonicalJson!)
            : ToolInputSchemaContractResult.Failure(result.Error!);
    }

    public static string? ValidateValue(JsonElement value, JsonElement schema) =>
        ToolStrictSchemaEngineV1.ValidateValue("$", value, schema, "Tool input");

    private static string ComputeDigest<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var canonical = AgentCanonicalJsonV1.Canonicalize(json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// Exact numeric projection shared by schema validation and MCP invocation.
/// Values outside Int64 or lossless Decimal are rejected instead of becoming a
/// rounded double or a string with a different runtime type.
/// </summary>
public static class ToolInputNumberContractV1
{
    public const string ContractVersion = "tool-input-number-contract:v1";

    public static bool TryReadInteger(JsonElement element, out long value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    public static bool TryReadNumber(JsonElement element, out object? value)
    {
        value = null;
        if (TryReadInteger(element, out var integer))
        {
            value = integer;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var number))
        {
            return false;
        }

        var source = AgentCanonicalNumberPolicyV1.Normalize(element.GetRawText());
        var projected = AgentCanonicalNumberPolicyV1.Normalize(
            number.ToString("G29", CultureInfo.InvariantCulture));
        if (!string.Equals(source, projected, StringComparison.Ordinal))
        {
            return false;
        }

        value = number;
        return true;
    }
}
