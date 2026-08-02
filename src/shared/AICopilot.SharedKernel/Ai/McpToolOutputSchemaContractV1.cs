using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

/// <summary>
/// MCP-only output-schema authority. MCP v2 structured content can preserve
/// scalar, array, and object JSON values, while the general tool registry v1
/// contract remains object-rooted.
/// </summary>
public static class McpToolOutputSchemaContractV1
{
    public const string ContractVersion = "mcp-tool-output-schema-contract:v1";
    public const int MaxSchemaUtf8Bytes = ToolOutputSchemaContractV1.MaxSchemaUtf8Bytes;

    public static readonly string ContractDigest = ComputeDigest(new
    {
        version = ContractVersion,
        engine = ToolStrictSchemaEngineV1.EngineVersion,
        rootType = "any-supported-json-type",
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
                "MCP tool output schema is required and must use the supported strict subset.");
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
                    ? $"MCP tool output schema raw JSON is {rawByteCount} UTF-8 bytes; maximum is {MaxSchemaUtf8Bytes} ({ContractVersion})."
                    : "MCP tool output schema is invalid.",
                rawByteCount);
        }

        var result = ToolStrictSchemaEngineV1.ValidateDefinition(
            schemaJson,
            "MCP tool output schema",
            requireObjectRoot: false);
        if (!result.IsValid)
        {
            return ToolOutputSchemaContractResult.Failure(result.Error!);
        }

        var byteCount = Encoding.UTF8.GetByteCount(result.CanonicalJson!);
        return byteCount <= MaxSchemaUtf8Bytes
            ? ToolOutputSchemaContractResult.Success(result.CanonicalJson!, byteCount)
            : ToolOutputSchemaContractResult.Failure(
                $"MCP tool output schema is {byteCount} UTF-8 bytes; maximum is {MaxSchemaUtf8Bytes} ({ContractVersion}).",
                byteCount);
    }

    public static string? ValidateValue(JsonElement value, JsonElement schema) =>
        ToolStrictSchemaEngineV1.ValidateValue("$", value, schema, "MCP tool output");

    private static string ComputeDigest<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var canonical = AgentCanonicalJsonV1.Canonicalize(json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
