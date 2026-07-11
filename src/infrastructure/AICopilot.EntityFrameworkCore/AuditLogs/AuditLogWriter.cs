using AICopilot.Services.Contracts;
using AICopilot.EntityFrameworkCore.Transactions;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.AuditLogs;

public sealed class AuditLogWriter(
    AuditDbContext auditDbContext,
    PersistenceCommitEngine commitEngine,
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
            ChangedFields = AuditMetadataCodec.Combine(request.ChangedFields, request.Metadata),
            CreatedAt = DateTime.UtcNow
        };

        auditDbContext.AuditLogs.Add(entry);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var hasStagedEntries = auditDbContext.ChangeTracker
            .Entries<AuditLogEntry>()
            .Any(entry => entry.State == EntityState.Added);
        if (!hasStagedEntries)
        {
            return Task.FromResult(0);
        }

        return commitEngine.CommitAsync(
            "Audit:Write",
            new AuditCommitParticipant(auditDbContext),
            cancellationToken);
    }

    private sealed class AuditCommitParticipant(AuditDbContext dbContext)
        : IPersistenceCommitParticipant<int>
    {
        public DbContext TransactionOwner => dbContext;

        public async Task<PersistenceAttemptResult<int>> PersistAttemptAsync(
            PersistenceAttemptContext context,
            CancellationToken cancellationToken)
        {
            var affectedRows = await dbContext.SaveChangesAsync(
                acceptAllChangesOnSuccess: false,
                cancellationToken);
            return new PersistenceAttemptResult<int>(
                affectedRows,
                HasPersistentChanges: affectedRows > 0);
        }

        public void CommitConfirmed(int result)
        {
            dbContext.ChangeTracker.AcceptAllChanges();
        }
    }
}
