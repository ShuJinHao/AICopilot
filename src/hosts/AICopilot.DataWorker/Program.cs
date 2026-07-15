using AICopilot.DataWorker;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);
builder.AddDataWorkerRuntime();

var host = builder.Build();
host.Run();
