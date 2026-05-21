using AICopilot.Infrastructure;
using AICopilot.RagWorker;
using AICopilot.RagService;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRagWorkerInfrastructure(typeof(Program).Assembly);
builder.AddRagService();
builder.AddRagIndexingService();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

internal sealed class WorkerCurrentUser : ICurrentUser
{
    public Guid? Id => null;

    public string? UserName => "rag-worker";

    public string? Role => null;

    public string? IdentityProvider => "Worker";

    public string? CloudTenantId => null;

    public string? CloudEmployeeNo => null;

    public string? CloudDepartmentId => null;

    public string? CloudDepartmentName => null;

    public string? CloudStatusVersion => null;

    public bool IsAuthenticated => false;
}
