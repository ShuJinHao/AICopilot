using AICopilot.EntityFrameworkCore;
using AICopilot.IdentityService.Authorization;
using AICopilot.MigrationWorkApp;
using AICopilot.Services.Contracts;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddScoped<Worker>();

builder.AddEfCore();
builder.Services.AddScoped<IPermissionCatalog, PermissionCatalog>();
builder.Services.AddScoped<IIdentityAccessService, IdentityAccessService>();
builder.Services.AddScoped<EnabledAdminInvariantPolicy>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

var host = builder.Build();
await host.StartAsync();

try
{
    await using var scope = host.Services.CreateAsyncScope();
    var worker = scope.ServiceProvider.GetRequiredService<Worker>();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    var invocationId = builder.Configuration["MigrationWorker:InvocationId"] ?? "unbound";
    var catalogFencePreflightOnly = builder.Configuration.GetValue<bool>(
        "MigrationWorker:CatalogFencePreflightOnly");

    return await MigrationWorkAppProcess.RunAsync(
        worker.RunAsync,
        invocationId,
        Console.Out,
        Console.Error,
        lifetime.ApplicationStopping,
        catalogFencePreflightOnly
            ? MigrationWorkAppProcess.CatalogFencePreflightSuccessMarker
            : MigrationWorkAppProcess.SuccessMarker);
}
finally
{
    await host.StopAsync();
}
