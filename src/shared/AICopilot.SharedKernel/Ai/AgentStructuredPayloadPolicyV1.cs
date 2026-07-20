using System.Text;
using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

public readonly record struct AgentStructuredPayloadSize(
    int Utf8ByteCount,
    bool IsAllowed);

public readonly record struct AgentInlineOutputNormalization(
    bool IsValid,
    string? CanonicalJson,
    int Utf8ByteCount,
    string? Error);

public static class AgentStructuredPayloadPolicyV1
{
    public const string PolicyVersion = "node-tool-input-policy:v1";
    public const int MaxNodeToolInputUtf8Bytes = 8_000;
    public const string InlineOutputPolicyVersion = "agent-step-inline-output-policy:v1";
    public const int MaxInlineOutputUtf8Bytes = 65_536;

    public static AgentStructuredPayloadSize EvaluateNodeToolInput(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var byteCount = Encoding.UTF8.GetByteCount(value);
        return new AgentStructuredPayloadSize(
            byteCount,
            byteCount <= MaxNodeToolInputUtf8Bytes);
    }

    public static AgentInlineOutputNormalization NormalizeNodeToolInput(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return NormalizeCanonicalJson(value, MaxNodeToolInputUtf8Bytes, PolicyVersion, "Node/tool input");
    }

    public static AgentInlineOutputNormalization NormalizeInlineOutput(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return NormalizeCanonicalJson(value, MaxInlineOutputUtf8Bytes, InlineOutputPolicyVersion, "Inline step output");
    }

    private static AgentInlineOutputNormalization NormalizeCanonicalJson(
        string value,
        int maxUtf8Bytes,
        string policyVersion,
        string payloadName)
    {
        var rawByteCount = Encoding.UTF8.GetByteCount(value);
        if (rawByteCount > maxUtf8Bytes)
        {
            return new AgentInlineOutputNormalization(
                false,
                null,
                rawByteCount,
                $"{payloadName} is {rawByteCount} UTF-8 bytes; maximum is {maxUtf8Bytes} ({policyVersion}).");
        }

        try
        {
            var canonical = AgentCanonicalJsonV1.Canonicalize(value, maxUtf8Bytes);
            var byteCount = Encoding.UTF8.GetByteCount(canonical);
            return byteCount <= maxUtf8Bytes
                ? new AgentInlineOutputNormalization(true, canonical, byteCount, null)
                : new AgentInlineOutputNormalization(
                    false,
                    null,
                    byteCount,
                    $"{payloadName} is {byteCount} UTF-8 bytes; maximum is {maxUtf8Bytes} ({policyVersion}).");
        }
        catch (JsonException)
        {
            return new AgentInlineOutputNormalization(false, null, 0, $"{payloadName} must be valid canonical JSON.");
        }
    }

}
