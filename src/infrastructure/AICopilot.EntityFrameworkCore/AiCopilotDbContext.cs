using System.Reflection;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore;

public class AiCopilotDbContext(DbContextOptions<AiCopilotDbContext> options) : DbContext(options)
{
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            ShouldApplyMainContextConfiguration);
    }

    private static bool ShouldApplyMainContextConfiguration(Type configurationType)
    {
        var ns = configurationType.Namespace ?? string.Empty;
        return !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.AiGateway", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.DataAnalysis", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.Identity", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.McpServer", StringComparison.Ordinal)
               && !ns.StartsWith("AICopilot.EntityFrameworkCore.Configuration.Rag", StringComparison.Ordinal);
    }

}
