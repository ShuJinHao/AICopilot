using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.AuditLogs;

public sealed class IdentityAuditLogWriter(
    IdentityStoreDbContext dbContext,
    ICurrentUser? currentUser = null) : IIdentityAuditLogWriter
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

        dbContext.AuditLogs.Add(entry);
        return Task.CompletedTask;
    }
}
