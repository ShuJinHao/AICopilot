using AICopilot.AiGatewayService;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.DataAnalysisService;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.IdentityService;
using AICopilot.Infrastructure;
using AICopilot.RagService;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructures();
builder.AddDataAnalysisService();
builder.AddIdentityService();
builder.AddAiGatewayService();
builder.AddRagService();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<AgentTaskRunQueueWorker>();

var host = builder.Build();
host.Run();

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
