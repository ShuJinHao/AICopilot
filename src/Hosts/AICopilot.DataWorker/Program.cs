using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EventBus;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddEfCore();
builder.AddEventBus();
builder.Services.AddHostedService<OutboxDispatcher>();

var host = builder.Build();
host.Run();
