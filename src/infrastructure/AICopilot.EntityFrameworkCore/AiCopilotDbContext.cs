using System.Reflection;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore;

public class AiCopilotDbContext(DbContextOptions<AiCopilotDbContext> options) : DbContext(options)
{
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            ShouldApplyMainContextConfiguration);
    }

    private static bool ShouldApplyMainContextConfiguration(Type configurationType)
    {
        var ns = configurationType.Namespace ?? string.Empty;
        return !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.AiGateway", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.DataAnalysis", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.Identity", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.McpServer", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.Rag", StringComparison.Ordinal);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEventEntities = ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToArray();

        var domainEvents = domainEventEntities
            .SelectMany(entity => entity.DomainEvents)
            .ToArray();

        OutboxMessages.AddRange(domainEvents.Select(OutboxMessage.FromIntegrationEvent));

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var entity in domainEventEntities)
        {
            entity.ClearDomainEvents();
        }

        return result;
    }
}
