using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.EntityFrameworkCore.Configuration.McpServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class McpServerDbContext(DbContextOptions<McpServerDbContext> options) : DbContext(options)
{
    public DbSet<McpServerInfo> McpServerInfos => Set<McpServerInfo>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("mcp");
        builder.ApplyConfiguration(new McpServerConfiguration());
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
