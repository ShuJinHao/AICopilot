using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.AuditLogs;

public sealed class AuditLogQueryService(AuditDbContext auditDbContext) : IAuditLogQueryService
{
    public async Task<AuditLogListDto> GetListAsync(
        int page,
        int pageSize,
        string? actionGroup,
        string? actionCode,
        string? targetType,
        string? targetName,
        string? operatorUserName,
        string? result,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var query = auditDbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(actionGroup))
        {
            query = query.Where(item => item.ActionGroup == actionGroup.Trim());
        }

        if (!string.IsNullOrWhiteSpace(actionCode))
        {
            var keyword = actionCode.Trim();
            query = query.Where(item => EF.Functions.ILike(item.ActionCode, $"%{keyword}%"));
        }

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            query = query.Where(item => item.TargetType == targetType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(targetName))
        {
            var keyword = targetName.Trim();
            query = query.Where(item => EF.Functions.ILike(item.TargetName, $"%{keyword}%"));
        }

        if (!string.IsNullOrWhiteSpace(operatorUserName))
        {
            var keyword = operatorUserName.Trim();
            query = query.Where(item => EF.Functions.ILike(item.OperatorUserName, $"%{keyword}%"));
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            query = query.Where(item => item.Result == result.Trim());
        }

        if (from.HasValue)
        {
            query = query.Where(item => item.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(item => item.CreatedAt <= to.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new AuditLogSummaryDto(
                item.Id,
                item.ActionGroup,
                item.ActionCode,
                item.TargetType,
                item.TargetId,
                item.TargetName,
                item.OperatorUserName,
                item.OperatorRoleName,
                item.Result,
                item.Summary,
                item.ChangedFields,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

        return new AuditLogListDto(page, pageSize, totalCount, items);
    }
}
