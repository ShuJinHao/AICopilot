using AICopilot.Services.Contracts;

namespace AICopilot.IdentityService;

internal static class IdentityRejectedAuditCommitter
{
    public static Task CommitRejectedAuditAsync(
        this ITransactionalExecutionService transactionalExecutionService,
        IIdentityAuditLogWriter auditLogWriter,
        AuditLogWriteRequest request,
        CancellationToken cancellationToken)
    {
        return transactionalExecutionService.ExecuteAsync(async ct =>
        {
            await auditLogWriter.WriteAsync(request, ct);
            return true;
        }, cancellationToken);
    }
}
