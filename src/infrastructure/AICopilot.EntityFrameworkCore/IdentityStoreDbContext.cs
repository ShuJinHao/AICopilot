using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Configuration.Audit;
using AICopilot.EntityFrameworkCore.Configuration.Identity;
using AICopilot.EntityFrameworkCore.ExternalIdentities;
using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class IdentityStoreDbContext(DbContextOptions<IdentityStoreDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    private int persistedRowsInCurrentAttempt;

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    public DbSet<ExternalIdentityBinding> ExternalIdentityBindings => Set<ExternalIdentityBinding>();

    internal bool HasPersistedChangesInCurrentAttempt => persistedRowsInCurrentAttempt > 0;

    internal void BeginPersistenceAttempt(bool clearTrackedState)
    {
        if (clearTrackedState)
        {
            ChangeTracker.Clear();
        }

        persistedRowsInCurrentAttempt = 0;
    }

    public override int SaveChanges()
        => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var affectedRows = base.SaveChanges(acceptAllChangesOnSuccess);
        persistedRowsInCurrentAttempt += affectedRows;
        return affectedRows;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var affectedRows = await base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
        persistedRowsInCurrentAttempt += affectedRows;
        return affectedRows;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentitySchema(builder);
        builder.ApplyConfiguration(new ExternalIdentityBindingConfiguration());
        builder.ApplyConfiguration(new AuditLogEntryConfiguration());
        builder.Entity<AuditLogEntry>()
            .ToTable("audit_logs", table => table.ExcludeFromMigrations());
    }

    private static void ConfigureIdentitySchema(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("AspNetUsers", "identity");
        builder.Entity<IdentityRole<Guid>>().ToTable("AspNetRoles", "identity");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("AspNetUserClaims", "identity");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("AspNetUserRoles", "identity");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("AspNetUserLogins", "identity");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("AspNetRoleClaims", "identity");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("AspNetUserTokens", "identity");
    }
}

public sealed class IdentityStoreDbContextFactory : IDesignTimeDbContextFactory<IdentityStoreDbContext>
{
    public IdentityStoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ai-copilot")
                               ?? Environment.GetEnvironmentVariable("AICOPILOT__DESIGNTIME__CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "Design-time migration requires ConnectionStrings__ai-copilot or AICOPILOT__DESIGNTIME__CONNECTION.");

        var optionsBuilder = new DbContextOptionsBuilder<IdentityStoreDbContext>();
        optionsBuilder.UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.IdentityStore);

        return new IdentityStoreDbContext(optionsBuilder.Options);
    }
}
