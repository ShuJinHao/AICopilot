using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.CloudReadiness;

public sealed class GetCloudReadonlyProductionOperationsStatusQueryHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository)
    : IQueryHandler<GetCloudReadonlyProductionOperationsStatusQuery, Result<CloudReadonlyProductionOperationsStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionOperationsStatusDto>> Handle(
        GetCloudReadonlyProductionOperationsStatusQuery request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        return Result.Success(operationsService.BuildStatus(p12, p13));
    }
}

public sealed class GetProductionPilotRunLedgerQueryHandler(CloudReadonlyProductionOperationsService operationsService)
    : IQueryHandler<GetProductionPilotRunLedgerQuery, Result<IReadOnlyCollection<ProductionPilotRunLedgerDto>>>
{
    public Task<Result<IReadOnlyCollection<ProductionPilotRunLedgerDto>>> Handle(
        GetProductionPilotRunLedgerQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(operationsService.BuildLedger()));
}

public sealed class ActivateProductionPilotEmergencyStopCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ActivateProductionPilotEmergencyStopCommand, Result<CloudReadonlyProductionOperationsStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionOperationsStatusDto>> Handle(
        ActivateProductionPilotEmergencyStopCommand request,
        CancellationToken cancellationToken)
    {
        operationsService.ActivateEmergencyStop(request.Reason, request.ActivatedBy);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.ActivateProductionPilotEmergencyStop",
                "CloudReadonlyProductionOperations",
                "emergency-stop",
                "Active",
                AuditResults.Succeeded,
                "Activated P14 production Pilot emergency stop.",
                ["emergencyStop", "sourceMode", "boundary"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        return Result.Success(operationsService.BuildStatus(p12, p13));
    }
}

public sealed class ClearProductionPilotEmergencyStopCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<ClearProductionPilotEmergencyStopCommand, Result<CloudReadonlyProductionOperationsStatusDto>>
{
    public async Task<Result<CloudReadonlyProductionOperationsStatusDto>> Handle(
        ClearProductionPilotEmergencyStopCommand request,
        CancellationToken cancellationToken)
    {
        operationsService.ClearEmergencyStop(request.Reason, request.ClearedBy);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.ClearProductionPilotEmergencyStop",
                "CloudReadonlyProductionOperations",
                "emergency-stop",
                "Cleared",
                AuditResults.Succeeded,
                "Cleared P14 production Pilot emergency stop; original gates still apply.",
                ["emergencyStop", "sourceMode", "boundary"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        return Result.Success(operationsService.BuildStatus(p12, p13));
    }
}

public sealed class UpsertProductionPilotIncidentCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<UpsertProductionPilotIncidentCommand, Result<ProductionPilotIncidentDto>>
{
    public async Task<Result<ProductionPilotIncidentDto>> Handle(
        UpsertProductionPilotIncidentCommand request,
        CancellationToken cancellationToken)
    {
        var incident = operationsService.UpsertIncident(request);
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.UpsertProductionPilotIncident",
                "CloudReadonlyProductionOperations",
                incident.IncidentId.ToString(),
                incident.Status,
                AuditResults.Succeeded,
                $"Upserted P14 production Pilot incident; severity={incident.Severity}; status={incident.Status}; resolutionHash={incident.ResolutionHash ?? "none"}.",
                ["incidentId", "severity", "status", "resolutionHash"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(incident);
    }
}

public sealed class RunProductionPilotGaReadinessEvaluationCommandHandler(
    CloudReadonlyProductionOperationsService operationsService,
    CloudReadonlyProductionPilotService productionPilotService,
    CloudReadonlyProductionControlledPilotService controlledPilotService,
    CloudReadonlyPilotReadinessService pilotReadinessService,
    IReadRepository<ToolRegistration> toolRepository,
    IAuditLogWriter auditLogWriter)
    : ICommandHandler<RunProductionPilotGaReadinessEvaluationCommand, Result<ProductionPilotGaReadinessAssessmentDto>>
{
    public async Task<Result<ProductionPilotGaReadinessAssessmentDto>> Handle(
        RunProductionPilotGaReadinessEvaluationCommand request,
        CancellationToken cancellationToken)
    {
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(toolRepository, cancellationToken);
        var p12 = productionPilotService.BuildStatus(pilotReadinessService.BuildStatus(protectedTools), protectedTools);
        var p13 = controlledPilotService.BuildStatus(p12, protectedTools);
        var assessment = operationsService.BuildGaReadinessAssessment(p12, p13, protectedTools);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                "AiGateway.RunProductionPilotGaReadinessEvaluation",
                "CloudReadonlyProductionOperations",
                "p15-readiness",
                assessment.Status,
                assessment.Status == CloudReadonlyProductionOperationsStatuses.ReadyForP15Planning ? AuditResults.Succeeded : AuditResults.Rejected,
                $"Ran P14 production Pilot GA readiness evaluation; status={assessment.Status}; blockers={assessment.Blockers.Count}.",
                ["status", "blockers", "sourceMode", "boundary"]),
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(assessment);
    }
}
