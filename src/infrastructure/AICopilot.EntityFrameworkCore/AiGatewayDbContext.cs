using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Core.AiGateway.Runtime.ModelQuota;
using AICopilot.EntityFrameworkCore.Configuration.AiGateway;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AICopilot.EntityFrameworkCore;

public sealed class AiGatewayDbContext(DbContextOptions<AiGatewayDbContext> options) : DbContext(options)
{
    public DbSet<LanguageModel> LanguageModels => Set<LanguageModel>();

    public DbSet<ConversationTemplate> ConversationTemplates => Set<ConversationTemplate>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<ToolRegistration> ToolRegistrations => Set<ToolRegistration>();

    public DbSet<AgentSessionState> AgentSessionStates => Set<AgentSessionState>();

    public DbSet<ModelQuotaReservation> ModelQuotaReservations => Set<ModelQuotaReservation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("aigateway");
        builder.HasSequence<long>("model_quota_fencing_seq", "aigateway");
        builder.ApplyConfiguration(new LanguageModelConfiguration());
        builder.ApplyConfiguration(new ConversationTemplateConfiguration());
        builder.ApplyConfiguration(new SessionConfiguration());
        builder.ApplyConfiguration(new MessageConfiguration());
        builder.ApplyConfiguration(new ToolRegistrationConfiguration());
        builder.ApplyConfiguration(new AgentSessionStateConfiguration());
        builder.ApplyConfiguration(new ModelQuotaReservationConfiguration());
    }
}

public sealed class AiGatewayDbContextFactory : IDesignTimeDbContextFactory<AiGatewayDbContext>
{
    public AiGatewayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ai-copilot")
                               ?? Environment.GetEnvironmentVariable("AICOPILOT__DESIGNTIME__CONNECTION")
                               ?? throw new InvalidOperationException(
                                   "Design-time migration requires ConnectionStrings__ai-copilot or AICOPILOT__DESIGNTIME__CONNECTION.");

        var optionsBuilder = new DbContextOptionsBuilder<AiGatewayDbContext>();
        optionsBuilder.UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.AiGateway);

        return new AiGatewayDbContext(optionsBuilder.Options);
    }
}
