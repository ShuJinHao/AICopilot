using AICopilot.Core.AiGateway.Aggregates.TrialOperations;

namespace AICopilot.AiGatewayService.TrialOperations;

internal static class TrialOperationsMapper
{
    public static TrialCampaignDto Map(TrialCampaign campaign)
    {
        var runs = campaign.ScenarioRuns
            .OrderByDescending(run => run.UpdatedAt)
            .Select(MapRun)
            .ToArray();
        var risks = campaign.RiskIssues
            .OrderByDescending(issue => issue.UpdatedAt)
            .Select(MapRisk)
            .ToArray();

        return new TrialCampaignDto(
            campaign.Id.Value,
            campaign.Name,
            campaign.Status.ToString(),
            campaign.AllowedSourceModes,
            campaign.OwnerDepartment,
            campaign.StartAt,
            campaign.EndAt,
            BuildSummary(campaign),
            campaign.CreatedAt)
        {
            Description = campaign.Summary,
            ReadinessStatus = campaign.PilotReadinessStatus.ToString(),
            UpdatedAt = campaign.UpdatedAt,
            ScenarioRuns = runs,
            Risks = risks
        };
    }

    public static TrialScenarioRunDto MapRun(TrialScenarioRun run)
    {
        return new TrialScenarioRunDto(
            run.Id.Value,
            run.CampaignId.Value,
            run.ScenarioId,
            run.TrialMode,
            run.SourceMode,
            run.Boundary,
            run.TaskId.Value,
            run.ArtifactIds,
            run.QueryHashes,
            run.ResultHashes,
            run.ApprovalStatus,
            run.Status.ToString(),
            run.StartedAt,
            run.CompletedAt);
    }

    public static TrialRiskIssueDto MapRisk(TrialRiskIssue issue)
    {
        return new TrialRiskIssueDto(
            issue.Id.Value,
            issue.CampaignId.Value,
            issue.Severity.ToString(),
            issue.Category,
            issue.Status.ToString(),
            issue.Owner,
            issue.SourceRef,
            issue.ResolutionHash,
            issue.CreatedAt,
            issue.UpdatedAt);
    }

    public static TrialCampaignSummaryDto BuildSummary(TrialCampaign campaign)
    {
        var runs = campaign.ScenarioRuns;
        var unresolvedRisks = campaign.RiskIssues.Count(issue =>
            issue.Status is TrialRiskStatus.Open or TrialRiskStatus.Mitigating);
        return new TrialCampaignSummaryDto(
            runs.Count,
            runs.Count(run => run.Status == TrialScenarioRunStatus.Passed),
            runs.Count(run => run.Status == TrialScenarioRunStatus.Failed),
            runs.Count(run => run.Status == TrialScenarioRunStatus.Blocked),
            runs.Where(run => IsApproved(run.ApprovalStatus)).SelectMany(run => run.ArtifactIds).Distinct().Count(),
            runs.Count(run => string.Equals(run.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)),
            unresolvedRisks,
            runs.SelectMany(run => run.QueryHashes).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            runs.SelectMany(run => run.ResultHashes).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static bool IsApproved(string value)
    {
        return string.Equals(value, "Approved", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Finalized", StringComparison.OrdinalIgnoreCase);
    }
}
