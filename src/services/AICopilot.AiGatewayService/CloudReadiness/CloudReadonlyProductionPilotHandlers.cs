using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class GetCloudReadonlyProductionPilotStatusQueryHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository)
    : IQueryHandler<GetCloudReadonlyProductionPilotStatusQuery, Result<CloudReadonlyProductionPilotStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotStatusDto>> Handle(
        GetCloudReadonlyProductionPilotStatusQuery request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        return Result.Success(productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools));
    }
}

public sealed class CreateCloudReadonlyProductionPilotWindowCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<CreateCloudReadonlyProductionPilotWindowCommand, Result<CloudReadonlyProductionPilotWindowDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotWindowDto>> Handle(
        CreateCloudReadonlyProductionPilotWindowCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p11Status = pilotReadinessService.BuildStatus(protectedTools);
        var result = productionPilotService.CreateWindow(request, p11Status, protectedTools);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.CreateCloudReadonlyProductionPilotWindow",
                "CloudReadonlyProductionPilot",
                result.Value.WindowId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Created P12 production readonly Pilot window; windowId={result.Value.WindowId}; endpoints={string.Join(",", result.Value.AllowedEndpointCodes)}.",
                ["windowId", "allowedEndpointCodes", "status"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class UpdateCloudReadonlyProductionPilotWindowStatusCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpdateCloudReadonlyProductionPilotWindowStatusCommand, Result<CloudReadonlyProductionPilotWindowDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotWindowDto>> Handle(
        UpdateCloudReadonlyProductionPilotWindowStatusCommand request,
        CancellationToken cancellationToken)
    {
        var result = productionPilotService.UpdateWindowStatus(request.WindowId, request.Status);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.UpdateCloudReadonlyProductionPilotWindowStatus",
                "CloudReadonlyProductionPilot",
                result.Value.WindowId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Updated P12 production readonly Pilot window status; windowId={result.Value.WindowId}; status={result.Value.Status}.",
                ["windowId", "status"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}

public sealed class RunCloudReadonlyProductionPilotGateEvaluationCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyProductionPilotGateEvaluationCommand, Result<CloudReadonlyProductionPilotStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotStatusDto>> Handle(
        RunCloudReadonlyProductionPilotGateEvaluationCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyProductionPilotGateEvaluation",
                "CloudReadonlyProductionPilot",
                status.PilotWindowId ?? "none",
                status.Status,
                status.Status == CloudReadonlyProductionPilotStatuses.Ready ? AuditResults.Succeeded : AuditResults.Rejected,
                $"Ran P12 production readonly Pilot gate; status={status.Status}; blockers={status.Blockers.Count}.",
                ["status", "blockers", "windowId"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(status);
    }
}

public sealed class RunCloudReadonlyProductionPilotScenarioCommandHandler(
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyProductionPilotScenarioCommand, Result<CloudReadonlyProductionPilotScenarioResultDto>>
{
    public async Task<Result<CloudReadonlyProductionPilotScenarioResultDto>> Handle(
        RunCloudReadonlyProductionPilotScenarioCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var result = await productionPilotService.RunScenarioAsync(
            request,
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return result;
        }

        var query = result.Value.QueryResult;
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunCloudReadonlyProductionPilotScenario",
                "CloudReadonlyProductionPilot",
                query.PilotWindowId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Ran P12 production readonly Pilot scenario; scenarioId={result.Value.ScenarioId}; endpoint={query.EndpointCode}; rows={query.RowCount}; truncated={query.IsTruncated}; resultHash={query.ResultHash}.",
                ["scenarioId", "endpointCode", "resultHash", "rowCount", "isTruncated"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}
