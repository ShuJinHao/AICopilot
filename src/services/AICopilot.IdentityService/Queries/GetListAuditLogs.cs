using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.IdentityService.Queries;

[AuthorizeRequirement("Identity.GetListAuditLogs")]
public record GetListAuditLogsQuery(
    int Page = 1,
    int PageSize = 20,
    string? ActionGroup = null,
    string? ActionCode = null,
    string? TargetType = null,
    string? TargetName = null,
    string? OperatorUserName = null,
    string? Result = null,
    DateTime? From = null,
    DateTime? To = null) : IQuery<Result<AuditLogListDto>>;

public sealed class GetListAuditLogsQueryHandler(IAuditLogQueryService auditLogQueryService)
    : IQueryHandler<GetListAuditLogsQuery, Result<AuditLogListDto>>
{
    public async Task<Result<AuditLogListDto>> Handle(
        GetListAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0
            ? 20
            : Math.Min(request.PageSize, 100);

        var result = await auditLogQueryService.GetListAsync(
            page,
            pageSize,
            request.ActionGroup,
            request.ActionCode,
            request.TargetType,
            request.TargetName,
            request.OperatorUserName,
            request.Result,
            request.From,
            request.To,
            cancellationToken);

        return Result.Success(result);
    }
}
