using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore.Configuration.DataAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class DataAnalysisDbContext(DbContextOptions<DataAnalysisDbContext> options) : DbContext(options)
{
    public DbSet<BusinessDatabase> BusinessDatabases => Set<BusinessDatabase>();

    public DbSet<DataSourcePermissionGrant> DataSourcePermissionGrants => Set<DataSourcePermissionGrant>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("dataanalysis");
        builder.ApplyConfiguration(new BusinessDatabaseConfiguration());
        builder.ApplyConfiguration(new DataSourcePermissionGrantConfiguration());
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
        optionsBuilder.UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.DataAnalysis);

        return new DataAnalysisDbContext(optionsBuilder.Options);
    }
}
