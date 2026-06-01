using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Core.AiGateway.Specifications.PilotAuthorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.PilotAuthorization;

public sealed class GetPilotAuthorizationSubmissionsQueryHandler(
    IReadRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetPilotAuthorizationSubmissionsQuery, Result<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>>
{
    public async Task<Result<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>> Handle(
        GetPilotAuthorizationSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var accessResult = await PilotAuthorizationAccess.LoadAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!accessResult.IsSuccess || accessResult.Value is null)
        {
            return Result.From(accessResult);
        }

        var access = accessResult.Value;
        Guid? filterUserId = PilotAuthorizationAccess.CanViewAll(access) ? null : access.UserId;
        var submissions = await repository.ListAsync(
            new PilotAuthorizationSubmissionListSpec(filterUserId),
            cancellationToken);
        return Result.Success<IReadOnlyCollection<PilotAuthorizationSubmissionDto>>(
            submissions.Select(PilotAuthorizationMapper.Map).ToArray());
    }
}

public sealed class GetPilotAuthorizationSubmissionQueryHandler(
    IReadRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetPilotAuthorizationSubmissionQuery, Result<PilotAuthorizationSubmissionDto>>
{
    public async Task<Result<PilotAuthorizationSubmissionDto>> Handle(
        GetPilotAuthorizationSubmissionQuery request,
        CancellationToken cancellationToken)
    {
        var loadResult = await PilotAuthorizationAccess.LoadSubmissionAsync(
            repository,
            currentUser,
            identityAccessService,
            request.SubmissionId,
            requireOwnerOrViewAll: true,
            cancellationToken);
        return !loadResult.IsSuccess || loadResult.Value is null
            ? Result.From(loadResult)
            : Result.Success(PilotAuthorizationMapper.Map(loadResult.Value.Submission));
    }
}

public sealed class GetPilotAuthorizationAuditTimelineQueryHandler(
    IReadRepository<PilotAuthorizationSubmission> repository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    IAuditLogQueryService auditLogQueryService)
    : IQueryHandler<GetPilotAuthorizationAuditTimelineQuery, Result<IReadOnlyCollection<PilotAuthorizationAuditTimelineItemDto>>>
{
    private const int MaxTimelineItems = 500;

    public async Task<Result<IReadOnlyCollection<PilotAuthorizationAuditTimelineItemDto>>> Handle(
        GetPilotAuthorizationAuditTimelineQuery request,
        CancellationToken cancellationToken)
    {
        var loadResult = await PilotAuthorizationAccess.LoadSubmissionAsync(
            repository,
            currentUser,
            identityAccessService,
            request.SubmissionId,
            requireOwnerOrViewAll: true,
            cancellationToken);
        if (!loadResult.IsSuccess || loadResult.Value is null)
        {
            return Result.From(loadResult);
        }

        if (!PilotAuthorizationAccess.HasPermission(loadResult.Value.Access, PilotAuthorizationPermissions.Audit))
        {
            return Result.Forbidden(new ApiProblemDescriptor(
                AuthProblemCodes.MissingPermission,
                "Current account is missing the Pilot authorization audit permission.",
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.MissingPermissions] = new[] { PilotAuthorizationPermissions.Audit }
                }));
        }

        var logs = await auditLogQueryService.GetListAsync(
            page: 1,
            pageSize: MaxTimelineItems,
            actionGroup: AuditActionGroups.AiGateway,
            actionCode: null,
            targetType: "PilotAuthorizationSubmission",
            targetId: request.SubmissionId.ToString(),
            targetName: null,
            operatorUserName: null,
            result: null,
            from: null,
            to: null,
            cancellationToken);

        return Result.Success<IReadOnlyCollection<PilotAuthorizationAuditTimelineItemDto>>(
            logs.Items
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Select(item => PilotAuthorizationAudit.MapTimelineItem(request.SubmissionId, item))
                .ToArray());
    }
}
