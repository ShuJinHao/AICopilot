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
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.TrialOperations;

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
