using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.EntityFrameworkCore.Configuration.McpServer;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class McpServerDbContext(DbContextOptions<McpServerDbContext> options) : DbContext(options)
{
    public DbSet<McpServerInfo> McpServerInfos => Set<McpServerInfo>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("mcp");
        builder.ApplyConfiguration(new McpServerConfiguration());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
        builder.Entity<OutboxMessage>().ToTable(
            "outbox_messages",
            "outbox",
            tableBuilder => tableBuilder.ExcludeFromMigrations());
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

public sealed class McpServerDbContextFactory : IDesignTimeDbContextFactory<McpServerDbContext>
{
    public McpServerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ai-copilot")
                               ?? Environment.GetEnvironmentVariable("AICOPILOT__DESIGNTIME__CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "Design-time migration requires ConnectionStrings__ai-copilot or AICOPILOT__DESIGNTIME__CONNECTION.");

        var optionsBuilder = new DbContextOptionsBuilder<McpServerDbContext>();
        optionsBuilder.UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.McpServer);

        return new McpServerDbContext(optionsBuilder.Options);
    }
}
