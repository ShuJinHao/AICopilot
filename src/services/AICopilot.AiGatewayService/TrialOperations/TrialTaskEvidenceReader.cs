using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.TrialOperations;

internal sealed record TrialTaskEvidence(
    string SourceMode,
    string? Boundary,
    IReadOnlyCollection<Guid> ArtifactIds,
    IReadOnlyCollection<string> QueryHashes,
    IReadOnlyCollection<string> ResultHashes)
{
    public static Result<TrialTaskEvidence> FromTask(AgentTask task, ArtifactWorkspace workspace)
    {
        var sourceModes = workspace.Artifacts
            .Select(artifact => artifact.SourceMode)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceModes.Length == 0)
        {
            return Result.Invalid("Attached task has no source mode evidence.");
        }

        if (sourceModes.Length > 1)
        {
            return Result.Invalid("A single P10 trial scenario run cannot mix SimulationBusiness and CloudReadonlySandbox evidence.");
        }

        var sourceMode = sourceModes[0];
        if (!TrialCampaign.SupportedSourceModes.Contains(sourceMode, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Invalid($"trial_source_mode_blocked: Source mode {sourceMode} cannot be attached to P10 trial operations.");
        }

        var boundary = workspace.Artifacts
            .Select(artifact => artifact.Boundary)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var queryHashes = workspace.Artifacts
            .Select(artifact => artifact.QueryHash)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resultHashes = workspace.Artifacts
            .Select(artifact => artifact.ResultHash)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (queryHashes.Length == 0 && resultHashes.Length == 0)
        {
            return Result.Invalid("Attached task has no query/result hash evidence.");
        }

        return Result.Success(new TrialTaskEvidence(
            sourceMode,
            boundary,
            workspace.Artifacts.Select(artifact => artifact.Id.Value).Distinct().ToArray(),
            queryHashes,
            resultHashes));
    }

    public static string ResolveFinalApprovalStatus(
        AgentTask task,
        ArtifactWorkspace workspace,
        IReadOnlyCollection<ApprovalRequest> approvals)
    {
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized || task.Status == AgentTaskStatus.Completed)
        {
            return "Approved";
        }

        var finalApproval = approvals
            .Where(approval => approval.ApprovalType == AgentApprovalType.FinalOutput)
            .OrderByDescending(approval => approval.CreatedAt)
            .FirstOrDefault(approval =>
                string.Equals(approval.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal));
        return finalApproval?.Status.ToString() ?? "None";
    }

    public static TrialScenarioRunStatus ResolveRunStatus(
        AgentTask task,
        ArtifactWorkspace workspace,
        string approvalStatus)
    {
        if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Cancelled or AgentTaskStatus.Rejected)
        {
            return TrialScenarioRunStatus.Failed;
        }

        if (workspace.Status == ArtifactWorkspaceStatus.Finalized ||
            task.Status is AgentTaskStatus.Completed or AgentTaskStatus.Finalized ||
            string.Equals(approvalStatus, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            return TrialScenarioRunStatus.Passed;
        }

        return task.Status is AgentTaskStatus.WaitingPlanApproval or AgentTaskStatus.PlanApproved
            ? TrialScenarioRunStatus.Planned
            : TrialScenarioRunStatus.Running;
    }
}
