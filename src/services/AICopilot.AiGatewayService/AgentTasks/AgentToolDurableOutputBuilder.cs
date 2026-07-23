using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Tools;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentToolDurableOutputV1(
    string ContractVersion,
    string ProviderType,
    string ToolCode,
    string TargetType,
    string TargetName,
    string ResultHash,
    int RawUtf8ByteCount,
    string SafeSummary,
    int PersistedSummaryUtf8ByteCount,
    bool SummaryTruncated,
    bool RawPayloadPersisted);

/// <summary>
/// Versioned closed contract for provider-controlled durable output. The raw
/// provider value is used only for the exact registry-schema check in the current
/// execution. Until typed Evidence storage exists, persistence receives identity,
/// digest, size, and a fixed non-provider-derived status. It never receives a
/// sanitized, truncated, or otherwise transformed slice of provider output.
/// </summary>
internal static class AgentToolDurableOutputContractV1
{
    internal const string ContractVersion = "agent-tool-durable-output:v1";
    internal const string SafeSummary =
        "Provider output validated; raw payload withheld by agent-tool-durable-output:v1.";
    internal const string SchemaJson =
        """{"type":"object","properties":{"contractVersion":{"type":"string","enum":["agent-tool-durable-output:v1"]},"providerType":{"type":"string"},"toolCode":{"type":"string"},"targetType":{"type":"string"},"targetName":{"type":"string"},"resultHash":{"type":"string"},"rawUtf8ByteCount":{"type":"integer"},"safeSummary":{"type":"string","enum":["Provider output validated; raw payload withheld by agent-tool-durable-output:v1."]},"persistedSummaryUtf8ByteCount":{"type":"integer"},"summaryTruncated":{"type":"boolean","enum":[false]},"rawPayloadPersisted":{"type":"boolean","enum":[false]}},"required":["contractVersion","providerType","toolCode","targetType","targetName","resultHash","rawUtf8ByteCount","safeSummary","persistedSummaryUtf8ByteCount","summaryTruncated","rawPayloadPersisted"],"additionalProperties":false}""";

    internal static readonly string ContractDigest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
    {
        version = ContractVersion,
        schema = CanonicalJson.Canonicalize(SchemaJson),
        safeSummary = SafeSummary,
        persistedSummaryUtf8ByteCount = Encoding.UTF8.GetByteCount(SafeSummary),
        summaryTruncated = false,
        rawPayloadPersisted = false
    }));

    internal static ToolOutputValidationResult Validate(AgentToolDurableOutputV1 output) =>
        ToolOutputSchemaValidator.ValidateAndCanonicalize(output, SchemaJson);
}

internal static class AgentToolDurableOutputBuilder
{
    internal static AgentToolDurableOutputV1 BuildProviderEnvelope(
        ToolRegistration registration,
        string rawCanonicalJson)
    {
        var rawBytes = Encoding.UTF8.GetBytes(rawCanonicalJson);
        var output = new AgentToolDurableOutputV1(
            AgentToolDurableOutputContractV1.ContractVersion,
            registration.ProviderType.ToString(),
            registration.ToolCode,
            registration.TargetType.ToString(),
            registration.TargetName,
            Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant(),
            rawBytes.Length,
            AgentToolDurableOutputContractV1.SafeSummary,
            Encoding.UTF8.GetByteCount(AgentToolDurableOutputContractV1.SafeSummary),
            SummaryTruncated: false,
            RawPayloadPersisted: false);
        var validation = AgentToolDurableOutputContractV1.Validate(output);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                validation.Error ?? "Provider durable output envelope violates its versioned closed contract.");
        }

        return output;
    }
}

/// <summary>
/// The single runtime output gate. P0 remains BuiltInOnly, but if a later phase
/// admits MCP execution the runtime already requires a closed provider envelope
/// bound to the exact validated contract-output snapshot; executor choice alone
/// can never authorize raw provider persistence.
/// </summary>
internal static class AgentToolRuntimeOutputGate
{
    internal static void EnsureValid(
        ToolRegistration registration,
        AgentToolExecutionResult executionResult)
    {
        var validation = Validate(registration, executionResult);
        if (!validation.IsValid)
        {
            throw new AgentToolExecutionException(
                validation.IsPayloadTooLarge
                    ? AppProblemCodes.EvidencePayloadTooLarge
                    : AppProblemCodes.ToolOutputSchemaInvalid,
                validation.Error ?? "Tool output does not match the registry schema.");
        }
    }

    internal static ToolOutputValidationResult Validate(
        ToolRegistration registration,
        AgentToolExecutionResult executionResult)
    {
        var contractOutput = ToolOutputSchemaValidator.ValidateCanonicalJson(
            executionResult.ContractOutput.CanonicalJson,
            registration.OutputSchemaJson);
        if (!contractOutput.IsValid)
        {
            return contractOutput;
        }

        var durableOutput = ToolOutputSchemaValidator.ValidateCanonicalPayload(
            executionResult.DurableOutput.CanonicalJson);
        if (!durableOutput.IsValid)
        {
            return durableOutput;
        }

        if (registration.ProviderType is ToolProviderType.Mcp or ToolProviderType.MockMcp)
        {
            return AgentProviderDurableOutputGate.Validate(
                registration,
                contractOutput.CanonicalJson!,
                contractOutput.Utf8ByteCount,
                durableOutput.CanonicalJson!);
        }

        return string.Equals(
                   contractOutput.CanonicalJson,
                   durableOutput.CanonicalJson,
                   StringComparison.Ordinal) &&
               contractOutput.Utf8ByteCount == durableOutput.Utf8ByteCount
            ? durableOutput
            : ToolOutputValidationResult.Failure(
                "Runtime durable output is invalid or is not bound to the validated output snapshot.",
                durableOutput.Utf8ByteCount);
    }
}

internal static class AgentProviderDurableOutputGate
{
    private const string InvalidEnvelope =
        "Provider durable output envelope is invalid or is not bound to the validated output snapshot.";

    internal static ToolOutputValidationResult Validate(
        ToolRegistration registration,
        string contractOutputCanonicalJson,
        int contractOutputUtf8ByteCount,
        string durableOutputCanonicalJson)
    {
        var envelopeContract = ToolOutputSchemaValidator.ValidateCanonicalJson(
            durableOutputCanonicalJson,
            AgentToolDurableOutputContractV1.SchemaJson);
        if (!envelopeContract.IsValid)
        {
            return ToolOutputValidationResult.Failure(
                InvalidEnvelope,
                envelopeContract.Utf8ByteCount,
                envelopeContract.IsPayloadTooLarge);
        }

        try
        {
            using var document = JsonDocument.Parse(envelopeContract.CanonicalJson!);
            var root = document.RootElement;
            var expectedHash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(contractOutputCanonicalJson)))
                .ToLowerInvariant();
            var actualCanonicalByteCount = Encoding.UTF8.GetByteCount(contractOutputCanonicalJson);
            if (!TryReadInt32(root, "rawUtf8ByteCount", out var rawUtf8ByteCount) ||
                !TryReadInt32(root, "persistedSummaryUtf8ByteCount", out var persistedSummaryUtf8ByteCount) ||
                actualCanonicalByteCount != contractOutputUtf8ByteCount ||
                rawUtf8ByteCount != actualCanonicalByteCount ||
                persistedSummaryUtf8ByteCount != Encoding.UTF8.GetByteCount(AgentToolDurableOutputContractV1.SafeSummary) ||
                !Matches(root, "contractVersion", AgentToolDurableOutputContractV1.ContractVersion) ||
                !Matches(root, "providerType", registration.ProviderType.ToString()) ||
                !Matches(root, "toolCode", registration.ToolCode) ||
                !Matches(root, "targetType", registration.TargetType.ToString()) ||
                !Matches(root, "targetName", registration.TargetName) ||
                !Matches(root, "resultHash", expectedHash) ||
                !Matches(root, "safeSummary", AgentToolDurableOutputContractV1.SafeSummary) ||
                !Matches(root, "summaryTruncated", expected: false) ||
                !Matches(root, "rawPayloadPersisted", expected: false))
            {
                return ToolOutputValidationResult.Failure(
                    InvalidEnvelope,
                    envelopeContract.Utf8ByteCount);
            }

            return envelopeContract;
        }
        catch (JsonException)
        {
            return ToolOutputValidationResult.Failure(
                InvalidEnvelope,
                envelopeContract.Utf8ByteCount);
        }
    }

    private static bool Matches(JsonElement root, string propertyName, string expected) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool Matches(JsonElement root, string propertyName, bool expected) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean() == expected;

    private static bool TryReadInt32(JsonElement root, string propertyName, out int value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }
}
