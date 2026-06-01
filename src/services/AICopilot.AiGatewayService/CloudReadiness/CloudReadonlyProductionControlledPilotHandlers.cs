using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class GetCloudReadonlyProductionControlledPilotStatusQueryHandler(
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository)
    : IQueryHandler<GetCloudReadonlyProductionControlledPilotStatusQuery, Result<CloudReadonlyProductionControlledPilotStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionControlledPilotStatusDto>> Handle(
        GetCloudReadonlyProductionControlledPilotStatusQuery request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p12Status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        return Result.Success(controlledPilotService.BuildStatus(p12Status, protectedTools));
    }
}

public sealed class CreateCloudReadonlyProductionControlledPlanCommandHandler(
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    ISender sender)
    : ICommandHandler<CreateCloudReadonlyProductionControlledPlanCommand, Result<CloudReadonlyProductionControlledPlanDto>>
{
    public async Task<Result<CloudReadonlyProductionControlledPlanDto>> Handle(
        CreateCloudReadonlyProductionControlledPlanCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p12Status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        var intentResult = controlledPilotService.CreateIntent(
            request.Goal,
            request.ArtifactTypes,
            request.TimeRange,
            request.MaxRows,
            p12Status,
            protectedTools);
        if (!intentResult.IsSuccess || intentResult.Value is null)
        {
            return Result.From(intentResult);
        }

        var taskResult = await sender.Send(
            new PlanAgentTaskCommand(
                request.SessionId,
                request.Goal,
                AgentTaskType.CloudDataReport,
                request.ModelId,
                ArtifactTypes: intentResult.Value.ArtifactTypes,
                BusinessDomains: intentResult.Value.EndpointCodes,
                QueryMode: CloudReadonlyProductionControlledPilotMarkers.SourceMode,
                RequiresDataApproval: true,
                PlannerMode: request.PlannerMode ?? "StaticOnly",
                IsCloudProductionControlledPilotTrial: true,
                CloudProductionGoalIntent: intentResult.Value),
            cancellationToken);
        if (!taskResult.IsSuccess || taskResult.Value is null)
        {
            return Result.From(taskResult);
        }

        return Result.Success(new CloudReadonlyProductionControlledPlanDto(taskResult.Value, intentResult.Value));
    }
}

public sealed class RunCloudReadonlyProductionControlledPilotCommandHandler(
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunCloudReadonlyProductionControlledPilotCommand, Result<CloudReadonlyProductionControlledPilotResultDto>>
{
    public async Task<Result<CloudReadonlyProductionControlledPilotResultDto>> Handle(
        RunCloudReadonlyProductionControlledPilotCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolRepository,
            cancellationToken);
        var p12Status = productionPilotService.BuildStatus(
            pilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        var result = await controlledPilotService.RunIntentAsync(
            request.IntentId,
            request.ArtifactTypes,
            request.MaxRows,
            request.TimeoutMs,
            p12Status,
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
                "AiGateway.RunCloudReadonlyProductionControlledPilot",
                "CloudReadonlyProductionControlledPilot",
                query.IntentId,
                result.Value.Status,
                AuditResults.Succeeded,
                $"Ran P13 production controlled readonly Pilot; endpoint={query.EndpointCode}; rows={query.RowCount}; truncated={query.IsTruncated}; resultHash={query.ResultHash}.",
                ["intentId", "endpointCode", "resultHash", "rowCount", "isTruncated"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return result;
    }
}
