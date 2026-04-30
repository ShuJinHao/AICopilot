using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Configuration.Audit;
using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class IdentityStoreDbContext(DbContextOptions<IdentityStoreDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentitySchema(builder);
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
