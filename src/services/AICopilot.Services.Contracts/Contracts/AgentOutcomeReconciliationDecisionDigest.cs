using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.Services.Contracts;

public static class AgentOutcomeReconciliationDecisionDigest
{
    public static string Compute(
        AgentOutcomeReconciliationClaim claim,
        AgentOutcomeReconciliationResolution resolution,
        string reasonCode,
        string actorType,
        string actorIdHash,
        string? evidenceDigest,
        string? providerReceiptHash,
        DateTimeOffset decidedAtUtc)
    {
        var canonical = string.Join('|',
            claim.TaskId.Value.ToString("D"),
            claim.RunAttemptId.Value.ToString("D"),
            claim.NodeRun.Id.Value.ToString("D"),
            claim.TaskFencingToken,
            claim.NodeFencingToken,
            claim.ReconciliationFencingToken,
            resolution,
            reasonCode.Trim(),
            actorType.Trim(),
            actorIdHash.Trim(),
            evidenceDigest?.Trim() ?? string.Empty,
            providerReceiptHash?.Trim() ?? string.Empty,
            decidedAtUtc.ToUniversalTime().ToString("O"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
