using AICopilot.Core.AiGateway.Aggregates.TrialOperations;

namespace AICopilot.AiGatewayService.TrialOperations;

internal static class TrialEvidencePackageBuilder
{
    public static TrialEvidencePackageDto Build(TrialCampaign campaign, PilotReadinessAssessmentDto assessment)
    {
        var summary = TrialOperationsMapper.BuildSummary(campaign);
        var metrics = new[]
        {
            new TrialEvidenceMetricDto("scenario_runs", "Scenario runs", summary.ScenarioRunCount),
            new TrialEvidenceMetricDto("passed_runs", "Passed runs", summary.PassedRunCount),
            new TrialEvidenceMetricDto("final_artifacts", "Final artifacts", summary.FinalArtifactCount),
            new TrialEvidenceMetricDto("pending_approvals", "Pending approvals", summary.PendingApprovalCount),
            new TrialEvidenceMetricDto("unresolved_risks", "Unresolved risks", summary.UnresolvedRiskCount),
            new TrialEvidenceMetricDto("query_hash_samples", "Query hash samples", Math.Min(5, summary.QueryHashCount)),
            new TrialEvidenceMetricDto("result_hash_samples", "Result hash samples", Math.Min(5, summary.ResultHashCount))
        };
        var evidenceItems = campaign.ScenarioRuns
            .OrderByDescending(run => run.UpdatedAt)
            .Select(run => new TrialEvidenceItemDto(
                "AgentScenarioRun",
                run.SourceMode,
                run.Boundary,
                run.Status.ToString(),
                run.QueryHashes.Concat(run.ResultHashes).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray(),
                run.Id.Value.ToString()))
            .ToArray();
        var unresolved = campaign.RiskIssues
            .Where(issue => issue.Status is TrialRiskStatus.Open or TrialRiskStatus.Mitigating)
            .OrderByDescending(issue => issue.Severity)
            .ThenByDescending(issue => issue.UpdatedAt)
            .Select(TrialOperationsMapper.MapRisk)
            .ToArray();

        return new TrialEvidencePackageDto(
            campaign.Id.Value,
            assessment.Status,
            metrics,
            evidenceItems,
            unresolved,
            ReportArtifactId: null,
            assessment.GeneratedAt);
    }
}
