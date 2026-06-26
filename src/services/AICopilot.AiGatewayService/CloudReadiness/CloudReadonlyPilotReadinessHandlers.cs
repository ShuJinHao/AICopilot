using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.TrialOperations;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.TrialOperations;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class GetCloudReadonlyPilotReadinessQueryHandler(
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository)
    : IQueryHandler<GetCloudReadonlyPilotReadinessQuery, Result<CloudReadonlyPilotReadinessStatusDto>>
{
    public async Task<Result<CloudReadonlyPilotReadinessStatusDto>> Handle(
        GetCloudReadonlyPilotReadinessQuery request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        return Result.Success(pilotReadinessService.BuildStatus(protectedTools));
    }

    internal static async Task<IReadOnlyCollection<ToolRegistration>> LoadProtectedToolRegistrationsAsync(
        IReadRepository<ToolRegistration> repository,
        CancellationToken cancellationToken)
    {
        var tools = await repository.ListAsync(cancellationToken: cancellationToken);
        return tools
            .Where(tool => ProtectedCloudReadonlyToolPolicy.IsProtected(tool.ToolCode))
            .ToArray();
    }
}

public sealed class CreateCloudReadonlyPilotConfigPackageCommandHandler(
    IReadRepository<TrialCampaign> campaignRepository,
    IReadRepository<ToolRegistration> toolRepository,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateCloudReadonlyPilotConfigPackageCommand, Result<CloudReadonlyPilotConfigPackageDto>>
{
    public async Task<Result<CloudReadonlyPilotConfigPackageDto>> Handle(
        CreateCloudReadonlyPilotConfigPackageCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var productionTool = protectedTools.FirstOrDefault(
            tool => string.Equals(tool.ToolCode, ProtectedCloudReadonlyToolPolicy.ProductionToolCode, StringComparison.OrdinalIgnoreCase));
        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool);
        if (assessment.Status != PilotReadinessStatus.ReadyForP11Planning.ToString())
        {
            return Result.Invalid($"P10 evidence package is not ready for P11 planning. Status={assessment.Status}; blockers={string.Join("; ", assessment.Blockers)}");
        }

        var evidencePackage = TrialEvidencePackageBuilder.Build(campaign, assessment);
        var result = pilotReadinessService.CreatePackage(request, evidencePackage, protectedTools);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.CreateCloudReadonlyPilotConfigPackage",
                "CloudReadonlyPilotReadiness",
                result.Value.PackageId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Created P11 Pilot readiness config package; campaignId={campaign.Id.Value}; packageId={result.Value.PackageId}; endpoints={string.Join(",", result.Value.AllowedEndpointCodes)}.",
                ["campaignId", "packageId", "allowedEndpointCodes"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class RunCloudReadonlyPilotGateEvaluationCommandHandler(
    IReadRepository<TrialCampaign> campaignRepository,
    IReadRepository<ToolRegistration> toolRepository,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyPilotGateEvaluationCommand, Result<CloudReadonlyPilotReadinessStatusDto>>
{
    public async Task<Result<CloudReadonlyPilotReadinessStatusDto>> Handle(
        RunCloudReadonlyPilotGateEvaluationCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await campaignRepository.FirstOrDefaultAsync(
            new TrialCampaignByIdSpec(new TrialCampaignId(request.CampaignId), includeDetails: true),
            cancellationToken);
        if (campaign is null)
        {
            return Result.NotFound();
        }

        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var productionTool = protectedTools.FirstOrDefault(
            tool => string.Equals(tool.ToolCode, ProtectedCloudReadonlyToolPolicy.ProductionToolCode, StringComparison.OrdinalIgnoreCase));
        var assessment = PilotReadinessEvaluator.Evaluate(campaign, productionTool);
        var status = pilotReadinessService.EvaluateGate(assessment, protectedTools);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyPilotGateEvaluation",
                "CloudReadonlyPilotReadiness",
                request.CampaignId.ToString(),
                status.Status,
                status.Status == CloudReadonlyPilotReadinessStatuses.Blocked ? AuditResults.Rejected : AuditResults.Succeeded,
                $"Ran P11 Pilot readiness gate; campaignId={request.CampaignId}; status={status.Status}; blockers={status.Blockers.Count}.",
                ["campaignId", "status", "blockers"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(status);
    }
}

public sealed class RunCloudReadonlyPilotApprovalRehearsalCommandHandler(
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyPilotApprovalRehearsalCommand, Result<PilotApprovalRehearsalDto>>
{
    public async Task<Result<PilotApprovalRehearsalDto>> Handle(
        RunCloudReadonlyPilotApprovalRehearsalCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var result = pilotReadinessService.RunApprovalRehearsal(request.PackageId, protectedTools);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyPilotApprovalRehearsal",
                "CloudReadonlyPilotReadiness",
                result.Value.PackageId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Ran P11 Pilot approval rehearsal; packageId={result.Value.PackageId}; rehearsalId={result.Value.RehearsalId}; steps={result.Value.Steps.Count}.",
                ["packageId", "rehearsalId", "status"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class RunCloudReadonlyPilotContractRehearsalCommandHandler(
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyPilotContractRehearsalCommand, Result<CloudReadonlyPilotContractRehearsalDto>>
{
    public async Task<Result<CloudReadonlyPilotContractRehearsalDto>> Handle(
        RunCloudReadonlyPilotContractRehearsalCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var result = pilotReadinessService.RunContractRehearsal(
            request.PackageId,
            request.EndpointCodes,
            request.MaxRows,
            request.TimeoutMs,
            protectedTools);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyPilotContractRehearsal",
                "CloudReadonlyPilotReadiness",
                result.Value.PackageId,
                CloudReadonlyPilotReadinessMarkers.Boundary,
                result.Value.Checks.Any(check => check.Status is "Failed" or "Timeout" or "SchemaMismatch")
                    ? AuditResults.Rejected
                    : AuditResults.Succeeded,
                $"Ran P11 fake production contract rehearsal; packageId={result.Value.PackageId}; checks={result.Value.Checks.Count}; blocked={result.Value.BlockedSamples.Count}.",
                ["packageId", "endpointCodes", "resultHash"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}
