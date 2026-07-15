using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class AuditPersistenceBoundaryTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task AuditLogWriter_ShouldStageAuditBeforeExplicitSave()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_audit_stage");
        await using (var root = new AiCopilotDbContext(
                         PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                             database.ConnectionString,
                             MigrationHistoryTables.AiCopilot)))
        {
            await root.Database.MigrateAsync();
        }

        await using var auditDbContext = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        var writer = new AuditLogWriter(
            auditDbContext,
            new PersistenceCommitEngine(PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString)));
        var targetId = Guid.NewGuid().ToString();

        await writer.WriteAsync(new AuditLogWriteRequest(
            AuditActionGroups.Config,
            "SecurityHardening.StageAudit",
            "SecurityHardening",
            targetId,
            "staged-audit",
            AuditResults.Succeeded,
            "Audit must remain staged until the explicit save boundary."));

        auditDbContext.ChangeTracker.Entries<AuditLogEntry>()
            .Should().ContainSingle()
            .Which.State.Should().Be(EntityState.Added);

        await using (var beforeSave = new AuditDbContext(
                         PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString)))
        {
            (await beforeSave.AuditLogs.AnyAsync(item => item.TargetId == targetId)).Should().BeFalse();
        }

        (await writer.SaveChangesAsync()).Should().Be(1);

        await using var afterSave = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        (await afterSave.AuditLogs.AnyAsync(item => item.TargetId == targetId)).Should().BeTrue();
    }
}
