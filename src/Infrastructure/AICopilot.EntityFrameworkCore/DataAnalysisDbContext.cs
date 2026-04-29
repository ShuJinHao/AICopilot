using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore.Configuration.DataAnalysis;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class DataAnalysisDbContext(DbContextOptions<DataAnalysisDbContext> options) : DbContext(options)
{
    public DbSet<BusinessDatabase> BusinessDatabases => Set<BusinessDatabase>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("dataanalysis");
        builder.ApplyConfiguration(new BusinessDatabaseConfiguration());
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

public sealed class DataAnalysisDbContextFactory : IDesignTimeDbContextFactory<DataAnalysisDbContext>
{
    public DataAnalysisDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ai-copilot")
                               ?? Environment.GetEnvironmentVariable("AICOPILOT__DESIGNTIME__CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "Design-time migration requires ConnectionStrings__ai-copilot or AICOPILOT__DESIGNTIME__CONNECTION.");

        var optionsBuilder = new DbContextOptionsBuilder<DataAnalysisDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new DataAnalysisDbContext(optionsBuilder.Options);
    }
}
