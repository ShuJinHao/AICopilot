using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Tools;

internal sealed record ToolOutputValidationResult(
    bool IsValid,
    string? Error,
    string? CanonicalJson,
    int Utf8ByteCount,
    bool IsPayloadTooLarge)
{
    internal static ToolOutputValidationResult Success(string canonicalJson, int utf8ByteCount) =>
        new(true, null, canonicalJson, utf8ByteCount, false);

    internal static ToolOutputValidationResult Failure(
        string error,
        int utf8ByteCount = 0,
        bool isPayloadTooLarge = false) =>
        new(false, error, null, utf8ByteCount, isPayloadTooLarge);
}

internal static class ToolOutputSchemaValidator
{
    internal static ToolOutputSchemaContractResult ValidateSchema(string? schemaJson) =>
        ToolOutputSchemaContractV1.Validate(schemaJson);

    internal static ToolOutputValidationResult ValidateAndCanonicalize(
        object? output,
        string? schemaJson)
    {
        var payload = CanonicalizeForPersistence(output);
        if (!payload.IsValid)
        {
            return payload;
        }

        return ValidateCanonicalJson(payload.CanonicalJson, schemaJson);
    }

    /// <summary>
    /// Validates an already captured canonical output snapshot without serializing
    /// the provider object again. The exact snapshot bytes are therefore shared by
    /// schema validation, hashing, and the durable provider envelope.
    /// </summary>
    internal static ToolOutputValidationResult ValidateCanonicalJson(
        string? canonicalJson,
        string? schemaJson)
    {
        var payload = ValidateCanonicalPayload(canonicalJson);
        if (!payload.IsValid)
        {
            return payload;
        }

        var schemaContract = ValidateSchema(schemaJson);
        if (!schemaContract.IsValid)
        {
            return ToolOutputValidationResult.Failure(
                schemaContract.Error ?? "Tool registry output schema is invalid.",
                payload.Utf8ByteCount);
        }

        try
        {
            using var outputDocument = JsonDocument.Parse(payload.CanonicalJson!);
            using var schemaDocument = JsonDocument.Parse(schemaContract.CanonicalJson!);
            var error = ToolOutputSchemaContractV1.ValidateValue(
                outputDocument.RootElement,
                schemaDocument.RootElement);
            return error is null
                ? ToolOutputValidationResult.Success(payload.CanonicalJson!, payload.Utf8ByteCount)
                : ToolOutputValidationResult.Failure(error, payload.Utf8ByteCount);
        }
        catch (JsonException)
        {
            return ToolOutputValidationResult.Failure(
                "Tool output or registry output schema is invalid canonical JSON.",
                payload.Utf8ByteCount);
        }
    }

    internal static ToolOutputValidationResult ValidateCanonicalPayload(string? canonicalJson)
    {
        if (string.IsNullOrWhiteSpace(canonicalJson))
        {
            return ToolOutputValidationResult.Failure("Tool output snapshot is required.");
        }

        var rawUtf8ByteCount = Encoding.UTF8.GetByteCount(canonicalJson);
        if (rawUtf8ByteCount > AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes)
        {
            return ToolOutputValidationResult.Failure(
                $"Tool output is {rawUtf8ByteCount} canonical UTF-8 bytes; maximum is {AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes} ({AgentStructuredPayloadPolicyV1.InlineOutputPolicyVersion}).",
                rawUtf8ByteCount,
                isPayloadTooLarge: true);
        }

        try
        {
            var canonical = AgentCanonicalJsonV1.Canonicalize(
                canonicalJson,
                AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes);
            if (!string.Equals(canonicalJson, canonical, StringComparison.Ordinal))
            {
                return ToolOutputValidationResult.Failure(
                    "Tool output snapshot is not exact canonical JSON.",
                    rawUtf8ByteCount);
            }

            return ToolOutputValidationResult.Success(canonical, rawUtf8ByteCount);
        }
        catch (JsonException)
        {
            return ToolOutputValidationResult.Failure(
                "Tool output snapshot violates the canonical inline output policy.",
                rawUtf8ByteCount);
        }
    }

    internal static ToolOutputValidationResult CanonicalizeForPersistence(object? output)
    {
        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(
                output,
                output?.GetType() ?? typeof(object),
                CanonicalJson.SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return ToolOutputValidationResult.Failure("Tool output cannot be serialized as canonical JSON.");
        }

        var rawUtf8ByteCount = Encoding.UTF8.GetByteCount(serialized);
        if (rawUtf8ByteCount > AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes)
        {
            return ToolOutputValidationResult.Failure(
                $"Tool output is {rawUtf8ByteCount} canonical UTF-8 bytes; maximum is {AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes} ({AgentStructuredPayloadPolicyV1.InlineOutputPolicyVersion}).",
                rawUtf8ByteCount,
                isPayloadTooLarge: true);
        }

        try
        {
            var canonical = AgentCanonicalJsonV1.Canonicalize(
                serialized,
                AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes);
            var canonicalUtf8ByteCount = Encoding.UTF8.GetByteCount(canonical);
            return canonicalUtf8ByteCount <= AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes
                ? ToolOutputValidationResult.Success(canonical, canonicalUtf8ByteCount)
                : ToolOutputValidationResult.Failure(
                    $"Tool output is {canonicalUtf8ByteCount} canonical UTF-8 bytes; maximum is {AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes} ({AgentStructuredPayloadPolicyV1.InlineOutputPolicyVersion}).",
                    canonicalUtf8ByteCount,
                    isPayloadTooLarge: true);
        }
        catch (JsonException)
        {
            return ToolOutputValidationResult.Failure(
                "Tool output violates the canonical inline output policy.",
                rawUtf8ByteCount);
        }
    }
}
