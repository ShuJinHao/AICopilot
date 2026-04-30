using System.Reflection;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Configuration.AiGateway;
using AICopilot.EntityFrameworkCore.Configuration.DataAnalysis;
using AICopilot.EntityFrameworkCore.Configuration.McpServer;
using AICopilot.EntityFrameworkCore.Configuration.Rag;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.SharedKernel.Domain;
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
            type => type != typeof(LanguageModelConfiguration)
                    && type != typeof(ConversationTemplateConfiguration)
                    && type != typeof(ApprovalPolicyConfiguration)
                    && type != typeof(SessionConfiguration)
                    && type != typeof(MessageConfiguration)
                    && type != typeof(McpServerConfiguration)
                    && type != typeof(BusinessDatabaseConfiguration)
                    && type != typeof(EmbeddingModelConfiguration)
                    && type != typeof(KnowledgeBaseConfiguration)
                    && type != typeof(DocumentConfiguration)
                    && type != typeof(DocumentChunkConfiguration));
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
