using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

public sealed record ToolOutputSchemaContractResult(
    bool IsValid,
    string? Error,
    string? CanonicalJson,
    int Utf8ByteCount)
{
    public static ToolOutputSchemaContractResult Success(string canonicalJson, int utf8ByteCount) =>
        new(true, null, canonicalJson, utf8ByteCount);

    public static ToolOutputSchemaContractResult Failure(string error, int utf8ByteCount = 0) =>
        new(false, error, null, utf8ByteCount);
}

/// <summary>
/// Versioned fail-closed output-schema authority for registry writes, discovery,
/// planner projection, runtime identity checks, and successful value validation.
/// </summary>
public static class ToolOutputSchemaContractV1
{
    public const string ContractVersion = "tool-output-schema-contract:v1";
    public const int MaxSchemaUtf8Bytes = 4_000;

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
        maxSchemaUtf8Bytes = MaxSchemaUtf8Bytes,
        additionalProperties = "false-or-omitted-with-strict-false-runtime-semantics",
        numericDomain = ToolInputNumberContractV1.ContractVersion,
        canonicalJsonPolicy = AgentCanonicalJsonV1.PolicyVersion,
        inlineOutputPolicy = AgentStructuredPayloadPolicyV1.InlineOutputPolicyVersion
    });

    public static ToolOutputSchemaContractResult Validate(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return ToolOutputSchemaContractResult.Failure(
                "Tool registry output schema is required and must use the supported strict subset.");
        }

        var rawByteCount = Encoding.UTF8.GetByteCount(schemaJson);
        try
        {
            _ = AgentCanonicalJsonV1.Preflight(schemaJson, MaxSchemaUtf8Bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return ToolOutputSchemaContractResult.Failure(
                rawByteCount > MaxSchemaUtf8Bytes
                    ? $"Tool registry output schema raw JSON is {rawByteCount} UTF-8 bytes; maximum is {MaxSchemaUtf8Bytes} ({ContractVersion})."
                    : "Tool registry output schema is invalid.",
                rawByteCount);
        }

        var result = ToolStrictSchemaEngineV1.ValidateDefinition(
            schemaJson,
            "Tool registry output schema",
            requireObjectRoot: true);
        if (!result.IsValid)
        {
            return ToolOutputSchemaContractResult.Failure(result.Error!);
        }

        var byteCount = Encoding.UTF8.GetByteCount(result.CanonicalJson!);
        return byteCount <= MaxSchemaUtf8Bytes
            ? ToolOutputSchemaContractResult.Success(result.CanonicalJson!, byteCount)
            : ToolOutputSchemaContractResult.Failure(
                $"Tool registry output schema is {byteCount} UTF-8 bytes; maximum is {MaxSchemaUtf8Bytes} ({ContractVersion}).",
                byteCount);
    }

    public static string? ValidateValue(JsonElement value, JsonElement schema) =>
        ToolStrictSchemaEngineV1.ValidateValue("$", value, schema, "Tool output");

    private static string ComputeDigest<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var canonical = AgentCanonicalJsonV1.Canonicalize(json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
