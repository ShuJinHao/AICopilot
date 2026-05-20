using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Core.AiGateway.Specifications.TrialOperations;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.TrialOperations;

public static class TrialOperationsPermissions
{
    public const string Read = "AiGateway.TrialOperations.Read";
    public const string Manage = "AiGateway.TrialOperations.Manage";
    public const string AuditView = "AiGateway.TrialOperations.AuditView";
}

public sealed record TrialCampaignSummaryDto(
    int ScenarioRunCount,
    int PassedRunCount,
    int FailedRunCount,
    int BlockedRunCount,
    int FinalArtifactCount,
    int PendingApprovalCount,
    int UnresolvedRiskCount,
    int QueryHashCount,
    int ResultHashCount);

public sealed record TrialCampaignDto(
    Guid CampaignId,
    string Name,
    string Status,
    IReadOnlyCollection<string> AllowedSourceModes,
    string? OwnerDepartment,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    TrialCampaignSummaryDto Summary,
    DateTimeOffset CreatedAt)
{
    public string? Description { get; init; }

    public string ReadinessStatus { get; init; } = PilotReadinessStatus.NotEvaluated.ToString();

    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyCollection<TrialScenarioRunDto> ScenarioRuns { get; init; } = [];

    public IReadOnlyCollection<TrialRiskIssueDto> Risks { get; init; } = [];
}

public sealed record TrialScenarioRunDto(
    Guid RunId,
    Guid CampaignId,
    string ScenarioId,
    string TrialMode,
    string SourceMode,
    string? Boundary,
    Guid TaskId,
    IReadOnlyCollection<Guid> ArtifactIds,
    IReadOnlyCollection<string> QueryHashes,
    IReadOnlyCollection<string> ResultHashes,
    string ApprovalStatus,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record TrialRiskIssueDto(
    Guid IssueId,
    Guid CampaignId,
    string Severity,
    string Category,
    string Status,
    string? Owner,
    string? SourceRef,
    string? ResolutionHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PilotReadinessCheckDto(
    string Code,
    string Label,
    string Status,
    bool IsBlocking,
    string Message);

public sealed record PilotReadinessMetricsDto(
    int ScenarioRuns,
    int PassedRuns,
    int FinalArtifacts,
    int PendingApprovals,
    int UnresolvedRisks,
    int QueryHashSamples,
    int ResultHashSamples);

public sealed record PilotReadinessAssessmentDto(
    Guid CampaignId,
    string Status,
    IReadOnlyCollection<PilotReadinessCheckDto> Checks,
    IReadOnlyCollection<string> Blockers,
    IReadOnlyCollection<string> Warnings,
    PilotReadinessMetricsDto Metrics,
    DateTimeOffset GeneratedAt);

public sealed record TrialEvidenceMetricDto(string Code, string Label, int Value);

public sealed record TrialEvidenceItemDto(
    string EvidenceType,
    string SourceMode,
    string? Boundary,
    string Status,
    IReadOnlyCollection<string> HashSamples,
    string ReferenceId);

public sealed record TrialEvidencePackageDto(
    Guid CampaignId,
    string ReadinessStatus,
    IReadOnlyCollection<TrialEvidenceMetricDto> Metrics,
    IReadOnlyCollection<TrialEvidenceItemDto> EvidenceItems,
    IReadOnlyCollection<TrialRiskIssueDto> UnresolvedRisks,
    Guid? ReportArtifactId,
    DateTimeOffset GeneratedAt);

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetTrialCampaignsQuery : IQuery<Result<IReadOnlyCollection<TrialCampaignDto>>>;

[AuthorizeRequirement(TrialOperationsPermissions.Read)]
public sealed record GetTrialCampaignDetailQuery(Guid CampaignId) : IQuery<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record CreateTrialCampaignCommand(
    string Name,
    IReadOnlyCollection<string>? AllowedSourceModes = null,
    string? OwnerDepartment = null,
    DateTimeOffset? StartAt = null,
    DateTimeOffset? EndAt = null,
    string? Summary = null) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record UpdateTrialCampaignStatusCommand(Guid CampaignId, string Status) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record AttachAgentTaskToTrialCampaignCommand(
    Guid CampaignId,
    Guid TaskId,
    string? ScenarioId = null,
    string? TrialMode = null) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.Manage)]
public sealed record UpsertTrialRiskIssueCommand(
    Guid CampaignId,
    Guid? IssueId,
    string Severity,
    string Category,
    string Status,
    string? Owner = null,
    string? SourceRef = null,
    string? ResolutionHash = null) : ICommand<Result<TrialCampaignDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record RunPilotReadinessEvaluationCommand(Guid CampaignId) : ICommand<Result<PilotReadinessAssessmentDto>>;

[AuthorizeRequirement(TrialOperationsPermissions.AuditView)]
public sealed record GenerateTrialEvidencePackageCommand(Guid CampaignId) : ICommand<Result<TrialEvidencePackageDto>>;

public sealed class GetTrialCampaignsQueryHandler(IReadRepository<TrialCampaign> campaignRepository)
    : IQueryHandler<GetTrialCampaignsQuery, Result<IReadOnlyCollection<TrialCampaignDto>>>
{
    public async Task<Result<IReadOnlyCollection<TrialCampaignDto>>> Handle(
        GetTrialCampaignsQuery request,
        CancellationToken cancellationToken)
    {
        var campaigns = await campaignRepository.ListAsync(
            new TrialCampaignsListSpec(includeDetails: true),
            cancellationToken);
        return Result.Success<IReadOnlyCollection<TrialCampaignDto>>(
            campaigns.Select(TrialOperationsMapper.Map).ToArray());
    }
}

public sealed class GetTrialCampaignDetailQueryHandler(IReadRepository<TrialCampaign> campaignRepository)
    : IQueryHandler<GetTrialCampaignDetailQuery, Result<TrialCampaignDto>>
{
    public async Task<Result<TrialCampaignDto>> Handle(
        GetTrialCampaignDetailQuery request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        return campaign is null
            ? Result.NotFound()
            : Result.Success(TrialOperationsMapper.Map(campaign));
    }
}

public sealed class CreateTrialCampaignCommandHandler(
    IRepository<TrialCampaign> campaignRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateTrialCampaignCommand, Result<TrialCampaignDto>>
{
    public async Task<Result<TrialCampaignDto>> Handle(
        CreateTrialCampaignCommand request,
        CancellationToken cancellationToken)
    {
        TrialCampaign campaign;
        try
        {
            campaign = new TrialCampaign(
                request.Name,
                request.AllowedSourceModes,
                request.OwnerDepartment,
                request.StartAt,
                request.EndAt,
                request.Summary,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Invalid(ex.Message);
        }

        campaignRepository.Add(campaign);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.CreateTrialCampaign",
                "TrialCampaign",
                campaign.Id.Value.ToString(),
                campaign.Name,
                AuditResults.Succeeded,
                $"Created trial campaign: {campaign.Name}; sourceModes={string.Join(",", campaign.AllowedSourceModes)}.",
                ["name", "allowedSourceModes", "ownerDepartment"]),
            cancellationToken);
        await campaignRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(TrialOperationsMapper.Map(campaign));
    }
}

public sealed class UpdateTrialCampaignStatusCommandHandler(
    IRepository<TrialCampaign> campaignRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateTrialCampaignStatusCommand, Result<TrialCampaignDto>>
{
    public async Task<Result<TrialCampaignDto>> Handle(
        UpdateTrialCampaignStatusCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        if (!Enum.TryParse<TrialCampaignStatus>(request.Status, ignoreCase: true, out var status))
        {
            return Result.Invalid("Trial campaign status is invalid.");
        }

        campaign.UpdateStatus(status, DateTimeOffset.UtcNow);
        campaignRepository.Update(campaign);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.UpdateTrialCampaignStatus",
                "TrialCampaign",
                campaign.Id.Value.ToString(),
                campaign.Name,
                AuditResults.Succeeded,
                $"Updated trial campaign status: {campaign.Name}; status={campaign.Status}.",
                ["status"]),
            cancellationToken);
        await campaignRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(TrialOperationsMapper.Map(campaign));
    }
}

public sealed class AttachAgentTaskToTrialCampaignCommandHandler(
    IRepository<TrialCampaign> campaignRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<AttachAgentTaskToTrialCampaignCommand, Result<TrialCampaignDto>>
{
    public async Task<Result<TrialCampaignDto>> Handle(
        AttachAgentTaskToTrialCampaignCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        var taskId = new AgentTaskId(request.TaskId);
        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(taskId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            return Result.NotFound();
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByTaskSpec(taskId, includeArtifacts: true),
            cancellationToken);
        if (workspace is null || workspace.Artifacts.Count == 0)
        {
            return Result.Invalid("Trial campaign can only attach Agent tasks with artifact evidence.");
        }

        var evidence = TrialTaskEvidence.FromTask(task, workspace);
        if (!evidence.IsSuccess)
        {
            return Result.From(evidence);
        }

        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(taskId),
            cancellationToken);
        var approvalStatus = TrialTaskEvidence.ResolveFinalApprovalStatus(task, workspace, approvals);
        var runStatus = TrialTaskEvidence.ResolveRunStatus(task, workspace, approvalStatus);
        try
        {
            campaign.AttachScenarioRun(
                string.IsNullOrWhiteSpace(request.ScenarioId) ? task.TaskCode : request.ScenarioId,
                string.IsNullOrWhiteSpace(request.TrialMode) ? "AgentTaskEvidence" : request.TrialMode,
                evidence.Value!.SourceMode,
                evidence.Value.Boundary,
                task.Id,
                evidence.Value.ArtifactIds,
                evidence.Value.QueryHashes,
                evidence.Value.ResultHashes,
                approvalStatus,
                runStatus,
                task.CreatedAt,
                task.CompletedAt,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Invalid(ex.Message);
        }

        campaignRepository.Update(campaign);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.AttachAgentTaskToTrialCampaign",
                "TrialCampaign",
                campaign.Id.Value.ToString(),
                campaign.Name,
                AuditResults.Succeeded,
                $"Attached Agent task evidence to campaign: {campaign.Name}; taskId={task.Id.Value}; sourceMode={evidence.Value.SourceMode}; queryHashes={evidence.Value.QueryHashes.Count}; resultHashes={evidence.Value.ResultHashes.Count}.",
                ["taskId", "sourceMode", "queryHashes", "resultHashes"],
                new Dictionary<string, string>
                {
                    ["taskId"] = task.Id.Value.ToString(),
                    ["sourceMode"] = evidence.Value.SourceMode,
                    ["boundary"] = evidence.Value.Boundary ?? string.Empty
                }),
            cancellationToken);
        await campaignRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(TrialOperationsMapper.Map(campaign));
    }
}

public sealed class UpsertTrialRiskIssueCommandHandler(
    IRepository<TrialCampaign> campaignRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpsertTrialRiskIssueCommand, Result<TrialCampaignDto>>
{
    public async Task<Result<TrialCampaignDto>> Handle(
        UpsertTrialRiskIssueCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        if (!Enum.TryParse<TrialRiskSeverity>(request.Severity, ignoreCase: true, out var severity))
        {
            return Result.Invalid("Trial risk severity is invalid.");
        }

        if (!Enum.TryParse<TrialRiskStatus>(request.Status, ignoreCase: true, out var status))
        {
            return Result.Invalid("Trial risk status is invalid.");
        }

        try
        {
            campaign.UpsertRiskIssue(
                request.IssueId.HasValue ? new TrialRiskIssueId(request.IssueId.Value) : null,
                severity,
                request.Category,
                status,
                request.Owner,
                request.SourceRef,
                request.ResolutionHash,
                DateTimeOffset.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(ex.Message);
        }

        campaignRepository.Update(campaign);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.UpsertTrialRiskIssue",
                "TrialCampaign",
                campaign.Id.Value.ToString(),
                campaign.Name,
                AuditResults.Succeeded,
                $"Upserted trial risk issue: {campaign.Name}; severity={severity}; status={status}; category={request.Category}.",
                ["severity", "status", "category", "resolutionHash"]),
            cancellationToken);
        await campaignRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(TrialOperationsMapper.Map(campaign));
    }
}

public sealed class RunPilotReadinessEvaluationCommandHandler(
    IRepository<TrialCampaign> campaignRepository,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunPilotReadinessEvaluationCommand, Result<PilotReadinessAssessmentDto>>
{
    public async Task<Result<PilotReadinessAssessmentDto>> Handle(
        RunPilotReadinessEvaluationCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        var productionTool = await toolRepository.GetAsync(
            tool => tool.ToolCode == "query_cloud_data_readonly",
            cancellationToken: cancellationToken);
        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool);
        campaign.SetPilotReadinessStatus(Enum.Parse<PilotReadinessStatus>(assessment.Status), assessment.GeneratedAt);
        campaignRepository.Update(campaign);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunPilotReadinessEvaluation",
                "TrialCampaign",
                campaign.Id.Value.ToString(),
                campaign.Name,
                assessment.Status == PilotReadinessStatus.Blocked.ToString() ? AuditResults.Rejected : AuditResults.Succeeded,
                $"Ran P11 readiness evaluation: {campaign.Name}; status={assessment.Status}; blockers={assessment.Blockers.Count}; warnings={assessment.Warnings.Count}.",
                ["pilotReadinessStatus", "checks", "blockers"]),
            cancellationToken);
        await campaignRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(assessment);
    }
}

public sealed class GenerateTrialEvidencePackageCommandHandler(
    IRepository<TrialCampaign> campaignRepository,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<GenerateTrialEvidencePackageCommand, Result<TrialEvidencePackageDto>>
{
    public async Task<Result<TrialEvidencePackageDto>> Handle(
        GenerateTrialEvidencePackageCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        var productionTool = await toolRepository.GetAsync(
            tool => tool.ToolCode == "query_cloud_data_readonly",
            cancellationToken: cancellationToken);
        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool);
        campaign.SetPilotReadinessStatus(Enum.Parse<PilotReadinessStatus>(assessment.Status), assessment.GeneratedAt);
        var package = TrialEvidencePackageBuilder.Build(campaign, assessment);
        campaignRepository.Update(campaign);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.GenerateTrialEvidencePackage",
                "TrialCampaign",
                campaign.Id.Value.ToString(),
                campaign.Name,
                AuditResults.Succeeded,
                $"Generated trial evidence package: {campaign.Name}; readiness={package.ReadinessStatus}; evidenceItems={package.EvidenceItems.Count}; unresolvedRisks={package.UnresolvedRisks.Count}.",
                ["readinessStatus", "metrics", "evidenceItems", "unresolvedRisks"]),
            cancellationToken);
        await campaignRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(package);
    }
}

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
