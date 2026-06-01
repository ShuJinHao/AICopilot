using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;

namespace AICopilot.AiGatewayService.TrialOperations;

internal static class PilotReadinessEvaluator
{
    public static PilotReadinessAssessmentDto Evaluate(TrialCampaign campaign, ToolRegistration? productionTool)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var checks = new List<PilotReadinessCheckDto>();
        var hasEvidence = campaign.ScenarioRuns.Count > 0;
        AddCheck(
            checks,
            "p9_evidence",
            "P9 artifact evidence",
            hasEvidence,
            false,
            hasEvidence ? "Trial campaign has attached Agent task evidence." : "No Agent task evidence has been attached.");

        var sourceBoundariesOk = campaign.ScenarioRuns.All(run =>
            TrialCampaign.SupportedSourceModes.Contains(run.SourceMode, StringComparer.Ordinal));
        AddCheck(
            checks,
            "source_boundaries",
            "Non-production source boundaries",
            sourceBoundariesOk,
            hasEvidence,
            sourceBoundariesOk
                ? "Only SimulationBusiness and CloudReadonlySandbox source modes are referenced."
                : "Unsupported source mode evidence is present.");

        var hasFinalEvidence = campaign.ScenarioRuns.Any(run =>
            run.Status == TrialScenarioRunStatus.Passed &&
            (string.Equals(run.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(run.ApprovalStatus, "Finalized", StringComparison.OrdinalIgnoreCase)));
        AddCheck(
            checks,
            "final_lock",
            "Final artifact lock",
            hasFinalEvidence,
            hasEvidence,
            hasFinalEvidence
                ? "At least one attached run has approved/finalized final artifact evidence."
                : "No approved/finalized final artifact evidence was found.");

        var approvalClosed = campaign.ScenarioRuns
            .Where(run => run.Status == TrialScenarioRunStatus.Passed)
            .All(run =>
                string.Equals(run.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(run.ApprovalStatus, "Finalized", StringComparison.OrdinalIgnoreCase));
        AddCheck(
            checks,
            "approval_closure",
            "Approval closure",
            approvalClosed,
            hasEvidence,
            approvalClosed ? "Passed runs have final approval evidence." : "At least one passed run lacks final approval evidence.");

        var blockingRisks = campaign.RiskIssues
            .Where(issue => issue.Status is TrialRiskStatus.Open or TrialRiskStatus.Mitigating)
            .Where(issue => issue.Severity is TrialRiskSeverity.High or TrialRiskSeverity.Critical)
            .ToArray();
        AddCheck(
            checks,
            "risk_register",
            "Blocking risks",
            blockingRisks.Length == 0,
            blockingRisks.Length > 0,
            blockingRisks.Length == 0
                ? "No open high or critical risks remain."
                : $"{blockingRisks.Length} open high/critical risk issue(s) remain.");

        var productionToolClosed = productionTool is null ||
                                   (!productionTool.IsEnabled &&
                                    !productionTool.IsVisibleToPlanner &&
                                    !productionTool.IsExecutableByAgent);
        AddCheck(
            checks,
            "production_tool_closed",
            "Production CloudReadonly tool remains closed",
            productionToolClosed,
            true,
            productionToolClosed
                ? "query_cloud_data_readonly is absent or disabled/hidden/non-executable."
                : "query_cloud_data_readonly is visible or executable and blocks P11 planning.");

        var blockers = checks.Where(check => check.IsBlocking && check.Status == "Failed")
            .Select(check => $"{check.Code}: {check.Message}")
            .ToArray();
        var warnings = checks.Where(check => !check.IsBlocking && check.Status == "Failed")
            .Select(check => $"{check.Code}: {check.Message}")
            .ToArray();
        var status = !hasEvidence
            ? PilotReadinessStatus.CollectingEvidence
            : blockers.Length > 0
                ? PilotReadinessStatus.Blocked
                : PilotReadinessStatus.ReadyForP11Planning;

        var summary = TrialOperationsMapper.BuildSummary(campaign);
        return new PilotReadinessAssessmentDto(
            campaign.Id.Value,
            status.ToString(),
            checks,
            blockers,
            warnings,
            new PilotReadinessMetricsDto(
                summary.ScenarioRunCount,
                summary.PassedRunCount,
                summary.FinalArtifactCount,
                summary.PendingApprovalCount,
                summary.UnresolvedRiskCount,
                Math.Min(5, summary.QueryHashCount),
                Math.Min(5, summary.ResultHashCount)),
            generatedAt);
    }

    private static void AddCheck(
        List<PilotReadinessCheckDto> checks,
        string code,
        string label,
        bool passed,
        bool isBlocking,
        string message)
    {
        checks.Add(new PilotReadinessCheckDto(
            code,
            label,
            passed ? "Passed" : "Failed",
            isBlocking,
            message));
    }
}
