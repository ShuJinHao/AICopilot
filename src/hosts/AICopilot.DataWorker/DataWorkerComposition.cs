using AICopilot.AiGatewayService;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.DataAnalysisService;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.IdentityService;
using AICopilot.Infrastructure;
using AICopilot.RagService;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting;

namespace AICopilot.DataWorker;

public static class DataWorkerComposition
{
    public static HostApplicationBuilder AddDataWorkerRuntime(
        this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceDefaults();
        builder.AddInfrastructures();
        builder.Services.AddAICopilotMediatRPipeline();
        builder.AddDataAnalysisService();
        builder.AddIdentityService();
        builder.AddAiGatewayService();
        builder.AddRagService();
        builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();
        builder.Services.AddHostedService<OutboxDispatcher>();
        builder.Services.AddHostedService<AgentTaskRunQueueWorker>();
        builder.Services.AddScoped<PersistenceFileMaintenanceService>();
        builder.Services.AddOptions<PersistenceMaintenanceOptions>()
            .BindConfiguration(PersistenceMaintenanceOptions.SectionName)
            .Validate(
                options => options.IntervalSeconds is >= 10 and <= 86400,
                "PersistenceMaintenance:IntervalSeconds must be between 10 and 86400.")
            .Validate(
                options => options.ReconciliationDelayMinutes is >= 1 and <= 1440,
                "PersistenceMaintenance:ReconciliationDelayMinutes must be between 1 and 1440.")
            .Validate(
                options => options.MarkerRetentionDays is >= 1 and <= 3650,
                "PersistenceMaintenance:MarkerRetentionDays must be between 1 and 3650.")
            .Validate(
                options => TimeSpan.FromDays(options.MarkerRetentionDays) >
                           TimeSpan.FromMinutes(options.ReconciliationDelayMinutes),
                "PersistenceMaintenance marker retention must be longer than the reconciliation delay.")
            .Validate(
                options => options.BatchSize is >= 1 and <= 1000,
                "PersistenceMaintenance:BatchSize must be between 1 and 1000.")
            .ValidateOnStart();
        builder.Services.AddHostedService<PersistenceMaintenanceWorker>();

        return builder;
    }
}

internal sealed class WorkerCurrentUser : ICurrentUser
{
    public Guid? Id => null;

    public string? UserName => "data-worker";

    public string? Role => null;

    public string? IdentityProvider => "Worker";

    public string? CloudTenantId => null;

    public string? CloudEmployeeNo => null;

    public string? CloudDepartmentId => null;

    public string? CloudDepartmentName => null;

    public string? CloudStatusVersion => null;

    public bool IsAuthenticated => false;
}
