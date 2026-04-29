using AICopilot.EntityFrameworkCore;
using AICopilot.IdentityService.Authorization;
using AICopilot.MigrationWorkApp;
using AICopilot.Services.Contracts;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

builder.AddEfCore();
builder.Services.AddScoped<IPermissionCatalog, PermissionCatalog>();
builder.Services.AddScoped<IIdentityAccessService, IdentityAccessService>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

var host = builder.Build();
host.Run();
