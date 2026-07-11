using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.EntityFrameworkCore.Configuration.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<EmbeddingModel> EmbeddingModels => Set<EmbeddingModel>();

    public DbSet<KnowledgeBase> KnowledgeBases => Set<KnowledgeBase>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();

    public DbSet<KnowledgeSupplement> KnowledgeSupplements => Set<KnowledgeSupplement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("rag");
        builder.ApplyConfiguration(new EmbeddingModelConfiguration());
        builder.ApplyConfiguration(new KnowledgeBaseConfiguration());
        builder.ApplyConfiguration(new KnowledgeCategoryConfiguration());
        builder.ApplyConfiguration(new KnowledgeSupplementConfiguration());
        builder.ApplyConfiguration(new DocumentConfiguration());
        builder.ApplyConfiguration(new DocumentChunkConfiguration());
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
