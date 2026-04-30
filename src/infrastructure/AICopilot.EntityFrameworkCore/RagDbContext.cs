using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.EntityFrameworkCore.Configuration.Rag;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<EmbeddingModel> EmbeddingModels => Set<EmbeddingModel>();

    public DbSet<KnowledgeBase> KnowledgeBases => Set<KnowledgeBase>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("rag");
        builder.ApplyConfiguration(new EmbeddingModelConfiguration());
        builder.ApplyConfiguration(new KnowledgeBaseConfiguration());
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

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var entity in domainEventEntities)
        {
            entity.ClearDomainEvents();
        }

        return result;
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
