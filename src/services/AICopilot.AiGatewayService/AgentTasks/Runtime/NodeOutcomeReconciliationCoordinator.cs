using System.Security.Cryptography;
using System.Text;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentOutcomeAuthorityProbeResult(
    AgentOutcomeReconciliationResolution Resolution,
    string ReasonCode,
    string SafeMessage,
    string? ProviderReceiptHash,
    string? EvidenceDigest,
    AgentEvidenceRecord? Evidence = null,
    AgentRunUsageLedgerEntry? Usage = null,
    string? OutputDigest = null,
    bool AllowNodeRetry = false,
    DateTimeOffset? RetryAtUtc = null,
    DateTimeOffset? NextCheckAtUtc = null);

internal interface IAgentOutcomeAuthorityProbe
{
    bool CanProbe(AgentOutcomeReconciliationClaim claim);

    Task<AgentOutcomeAuthorityProbeResult> ProbeAsync(
        AgentOutcomeReconciliationClaim claim,
        CancellationToken cancellationToken);
}

internal sealed class NodeOutcomeReconciliationCoordinator(
    IAgentNodeOutcomeReconciliationStore store,
    IEnumerable<IAgentOutcomeAuthorityProbe> probes,
    ICurrentUser? currentUser = null,
    IIdentityAccessService? identityAccessService = null)
{
    private static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(2);

    public async Task<bool> ProcessNextAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var claim = await store.TryClaimNextAsync(owner, DefaultLease, now, cancellationToken);
        if (claim is null)
        {
            return false;
        }

        var probe = probes.SingleOrDefault(candidate =>
            candidate.CanProbe(claim));
        AgentOutcomeAuthorityProbeResult result;
        if (probe is null)
        {
            result = new AgentOutcomeAuthorityProbeResult(
                AgentOutcomeReconciliationResolution.StillUnknown,
                "authoritative_probe_unavailable",
                "No authoritative receipt probe is registered; manual reconciliation remains required.",
                claim.NodeRun.ProviderReceiptHash,
                EvidenceDigest: null,
                NextCheckAtUtc: NextCheck(claim, now));
        }
        else
        {
            try
            {
                result = await probe.ProbeAsync(claim, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                result = new AgentOutcomeAuthorityProbeResult(
                    AgentOutcomeReconciliationResolution.StillUnknown,
                    "authoritative_probe_failed",
                    "Authoritative outcome probe failed without a trusted decision; the node remains blocked.",
                    claim.NodeRun.ProviderReceiptHash,
                    EvidenceDigest: null,
                    NextCheckAtUtc: NextCheck(claim, now));
            }
        }

        var actorIdHash = Hash(owner);
        return (await CommitResultAsync(
            claim,
            result,
            actorType: "Reconciler",
            actorIdHash,
            DateTimeOffset.UtcNow,
            cancellationToken)).IsSuccess;
    }

    public async Task<Result> ResolveManuallyAsync(
        AgentNodeRunId nodeRunId,
        AgentOutcomeReconciliationResolution resolution,
        string reasonCode,
        string safeMessage,
        string evidenceDigest,
        CancellationToken cancellationToken)
    {
        if (resolution is not AgentOutcomeReconciliationResolution.ManualAbandonedAsFailed
            and not AgentOutcomeReconciliationResolution.ManualAbandonedAsCancelled)
        {
            return Result.Invalid("Manual outcome resolution must explicitly abandon as failed or cancelled.");
        }

        if (currentUser?.Id is not { } userId || identityAccessService is null)
        {
            return Result.Unauthorized();
        }

        var access = await identityAccessService.GetCurrentUserAccessAsync(userId, cancellationToken);
        if (!AgentApprovalPermissions.HasPermission(access, AgentApprovalPermissions.ReconcileAgentOutcome))
        {
            return AgentApprovalPermissions.ForbiddenMissing(AgentApprovalPermissions.ReconcileAgentOutcome);
        }

        if (string.IsNullOrWhiteSpace(evidenceDigest))
        {
            return Result.Invalid("Manual reconciliation requires an authoritative evidence digest.");
        }

        var actorIdHash = Hash(userId.ToString("D"));
        var claim = await store.TryClaimAsync(
            nodeRunId,
            $"manual:{actorIdHash[..16]}",
            DefaultLease,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (claim is null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunFenceStale,
                "Outcome-unknown node is no longer available for manual reconciliation."));
        }

        return await CommitResultAsync(
            claim,
            new AgentOutcomeAuthorityProbeResult(
                resolution,
                reasonCode,
                safeMessage,
                claim.NodeRun.ProviderReceiptHash,
                evidenceDigest),
            actorType: "Human",
            actorIdHash,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private async Task<Result> CommitResultAsync(
        AgentOutcomeReconciliationClaim claim,
        AgentOutcomeAuthorityProbeResult result,
        string actorType,
        string actorIdHash,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken)
    {
        var decisionDigest = ComputeDecisionDigest(
            claim,
            result.Resolution,
            result.ReasonCode,
            actorType,
            actorIdHash,
            result.EvidenceDigest,
            result.ProviderReceiptHash,
            decidedAtUtc);
        AgentFencedWriteResult writeResult;
        switch (result.Resolution)
        {
            case AgentOutcomeReconciliationResolution.ConfirmedSucceeded:
                if (result.Evidence is null || result.Usage is null || string.IsNullOrWhiteSpace(result.OutputDigest))
                {
                    return Result.Invalid("Confirmed success requires normalized Evidence, usage, and output digest.");
                }

                writeResult = await store.CommitSucceededAsync(
                    new AgentOutcomeReconciliationSuccessCheckpoint(
                        claim,
                        result.Evidence,
                        result.Usage,
                        result.OutputDigest,
                        result.ProviderReceiptHash,
                        result.ReasonCode,
                        actorType,
                        actorIdHash,
                        decisionDigest,
                        decidedAtUtc),
                    cancellationToken);
                break;

            case AgentOutcomeReconciliationResolution.ConfirmedNotOccurred:
            case AgentOutcomeReconciliationResolution.ConfirmedCancelled:
            case AgentOutcomeReconciliationResolution.ManualAbandonedAsFailed:
            case AgentOutcomeReconciliationResolution.ManualAbandonedAsCancelled:
                writeResult = await store.CommitNegativeDecisionAsync(
                    new AgentOutcomeReconciliationNegativeDecision(
                        claim,
                        result.Resolution,
                        result.ReasonCode,
                        result.SafeMessage,
                        actorType,
                        actorIdHash,
                        result.EvidenceDigest,
                        result.ProviderReceiptHash,
                        decisionDigest,
                        result.AllowNodeRetry,
                        result.RetryAtUtc,
                        decidedAtUtc),
                    cancellationToken);
                break;

            case AgentOutcomeReconciliationResolution.StillUnknown:
            case AgentOutcomeReconciliationResolution.ConflictingEvidence:
                writeResult = await store.DeferAsync(
                    new AgentOutcomeReconciliationDeferral(
                        claim,
                        result.Resolution,
                        result.ReasonCode,
                        result.SafeMessage,
                        actorType,
                        actorIdHash,
                        result.EvidenceDigest,
                        result.ProviderReceiptHash,
                        decisionDigest,
                        result.NextCheckAtUtc ?? NextCheck(claim, decidedAtUtc),
                        decidedAtUtc),
                    cancellationToken);
                break;

            default:
                return Result.Invalid("Unsupported outcome reconciliation resolution.");
        }

        return writeResult switch
        {
            AgentFencedWriteResult.Succeeded or AgentFencedWriteResult.Duplicate => Result.Success(),
            AgentFencedWriteResult.StaleFence => Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunFenceStale,
                "Outcome reconciliation decision was fenced by a newer authority.")),
            _ => Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Outcome reconciliation decision conflicts with the authoritative node state."))
        };
    }

    private static DateTimeOffset NextCheck(AgentOutcomeReconciliationClaim claim, DateTimeOffset nowUtc)
    {
        return nowUtc >= claim.ReconciliationDeadlineAt
            ? nowUtc.AddHours(6)
            : nowUtc.AddMinutes(Math.Min(30, Math.Max(1, claim.NodeRun.ReconciliationAttemptNo * 2)));
    }

    private static string ComputeDecisionDigest(
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
        return Hash(canonical);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }
}
