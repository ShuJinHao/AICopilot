using AICopilot.Infrastructure;
using AICopilot.RagWorker;
using AICopilot.RagService;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRagWorkerInfrastructure(typeof(Program).Assembly);
builder.AddRagService();
builder.AddRagIndexingService();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
