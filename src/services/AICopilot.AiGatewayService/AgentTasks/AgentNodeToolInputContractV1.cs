using System.Text.Json;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentNodeToolInputContractResult(
    bool IsValid,
    string? CanonicalJson,
    int CanonicalUtf8ByteCount,
    string? Error);

internal static class AgentNodeToolInputContractV1
{
    public static AgentNodeToolInputContractResult Normalize(string? inputJson)
    {
        var source = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson;
        try
        {
            var normalized = AgentStructuredPayloadPolicyV1.NormalizeNodeToolInput(source);
            if (!normalized.IsValid)
            {
                return new AgentNodeToolInputContractResult(
                    false,
                    null,
                    normalized.Utf8ByteCount,
                    normalized.Error);
            }

            var canonicalJson = normalized.CanonicalJson!;
            using var document = JsonDocument.Parse(canonicalJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid("Node/tool input must be a JSON object.");
            }

            return new AgentNodeToolInputContractResult(
                true,
                canonicalJson,
                normalized.Utf8ByteCount,
                null);
        }
        catch (JsonException)
        {
            return Invalid("Node/tool input is not valid JSON.");
        }
    }

    private static AgentNodeToolInputContractResult Invalid(string error)
    {
        return new AgentNodeToolInputContractResult(false, null, 0, error);
    }
}
