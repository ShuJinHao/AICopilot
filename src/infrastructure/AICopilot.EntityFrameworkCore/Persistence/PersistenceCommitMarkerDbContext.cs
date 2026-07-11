using AICopilot.EntityFrameworkCore.Configuration.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Persistence;

public sealed class PersistenceCommitMarkerDbContext(
    DbContextOptions<PersistenceCommitMarkerDbContext> options) : DbContext(options)
{
    public DbSet<PersistenceCommitMarker> CommitMarkers => Set<PersistenceCommitMarker>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new PersistenceCommitMarkerConfiguration());
        builder.Entity<PersistenceCommitMarker>().ToTable(
            "commit_markers",
            "persistence",
            tableBuilder => tableBuilder.ExcludeFromMigrations());
    }
}
