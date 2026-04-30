using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.AuditLogs;

public sealed class AuditLogWriter(
    AuditDbContext auditDbContext,
    ICurrentUser? currentUser = null) : IAuditLogWriter
{
    public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        var operatorUserName = string.IsNullOrWhiteSpace(currentUser?.UserName)
            ? "System"
            : currentUser.UserName;

        var entry = new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActionGroup = request.ActionGroup,
            ActionCode = request.ActionCode,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            TargetName = request.TargetName,
            OperatorUserId = currentUser?.Id?.ToString(),
            OperatorUserName = operatorUserName,
            OperatorRoleName = currentUser?.Role,
            Result = request.Result,
            Summary = request.Summary,
            ChangedFields = request.ChangedFields?.ToArray() ?? [],
            CreatedAt = DateTime.UtcNow
        };

        auditDbContext.AuditLogs.Add(entry);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return auditDbContext.SaveChangesAsync(cancellationToken);
    }
}
