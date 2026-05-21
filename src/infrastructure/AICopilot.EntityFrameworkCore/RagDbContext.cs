using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.EntityFrameworkCore.Configuration.Rag;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;

namespace AICopilot.EntityFrameworkCore;

public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    private readonly List<Func<object>> stagedIntegrationEventFactories = [];

    public DbSet<EmbeddingModel> EmbeddingModels => Set<EmbeddingModel>();

    public DbSet<KnowledgeBase> KnowledgeBases => Set<KnowledgeBase>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();

    public DbSet<KnowledgeSupplement> KnowledgeSupplements => Set<KnowledgeSupplement>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public void StageIntegrationEvent<TEvent>(Func<TEvent> messageFactory)
        where TEvent : class
    {
        stagedIntegrationEventFactories.Add(() => messageFactory());
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("rag");
        builder.ApplyConfiguration(new EmbeddingModelConfiguration());
        builder.ApplyConfiguration(new KnowledgeBaseConfiguration());
        builder.ApplyConfiguration(new KnowledgeCategoryConfiguration());
        builder.ApplyConfiguration(new KnowledgeSupplementConfiguration());
        builder.ApplyConfiguration(new DocumentConfiguration());
        builder.ApplyConfiguration(new DocumentChunkConfiguration());
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

        if (stagedIntegrationEventFactories.Count == 0)
        {
            var result = await base.SaveChangesAsync(cancellationToken);

            foreach (var entity in domainEventEntities)
            {
                entity.ClearDomainEvents();
            }

            return result;
        }

        var hasCurrentTransaction = Database.CurrentTransaction is not null;
        if (hasCurrentTransaction)
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            OutboxMessages.AddRange(
                MaterializeStagedIntegrationEvents()
                    .Select(OutboxMessage.FromIntegrationEvent));
            result += await base.SaveChangesAsync(cancellationToken);

            foreach (var entity in domainEventEntities)
            {
                entity.ClearDomainEvents();
            }

            return result;
        }

        var stagedFactories = stagedIntegrationEventFactories.ToArray();
        var strategy = Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
                var committed = false;
                var stagedOutboxMessages = Array.Empty<OutboxMessage>();

                try
                {
                    var result = await base.SaveChangesAsync(cancellationToken);
                    stagedOutboxMessages = stagedFactories
                        .Select(factory => OutboxMessage.FromIntegrationEvent(factory()))
                        .ToArray();
                    OutboxMessages.AddRange(stagedOutboxMessages);

                    result += await base.SaveChangesAsync(cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    committed = true;
                    stagedIntegrationEventFactories.Clear();

                    foreach (var entity in domainEventEntities)
                    {
                        entity.ClearDomainEvents();
                    }

                    return result;
                }
                finally
                {
                    if (!committed && transaction.GetDbTransaction().Connection is not null)
                    {
                        foreach (var entry in ChangeTracker.Entries<OutboxMessage>()
                                     .Where(entry => stagedOutboxMessages.Contains(entry.Entity)))
                        {
                            entry.State = EntityState.Detached;
                        }

                        await transaction.RollbackAsync(cancellationToken);
                    }
                }
            });
        }
        catch
        {
            stagedIntegrationEventFactories.Clear();
            throw;
        }
    }

    private IReadOnlyList<object> MaterializeStagedIntegrationEvents()
    {
        var messages = stagedIntegrationEventFactories.Select(factory => factory()).ToArray();
        stagedIntegrationEventFactories.Clear();
        return messages;
    }
}

public sealed class RagDbContextFactory : IDesignTimeDbContextFactory<RagDbContext>
{
    public RagDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ai-copilot")
                               ?? Environment.GetEnvironmentVariable("AICOPILOT__DESIGNTIME__CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "Design-time migration requires ConnectionStrings__ai-copilot or AICOPILOT__DESIGNTIME__CONNECTION.");

        var optionsBuilder = new DbContextOptionsBuilder<RagDbContext>();
        optionsBuilder.UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.Rag);

        return new RagDbContext(optionsBuilder.Options);
    }
}
