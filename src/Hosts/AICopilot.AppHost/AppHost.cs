using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgresdb = builder.AddPostgres("postgres")
    .WithDataVolume("postgres-aicopilots")
    .WithPgWeb(pgWeb => pgWeb.WithHostPort(5050))
    .AddDatabase("ai-copilot");

var rabbitmq = builder.AddRabbitMQ("eventbus")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

var migration = builder.AddProject<AICopilot_MigrationWorkApp>("aicopilot-migration")
    .WithReference(postgresdb)
    .WaitFor(postgresdb);

builder.AddProject<AICopilot_HttpApi>("aicopilot-httpapi")
    .WithUrl("swagger")
    .WaitFor(postgresdb)
    .WaitFor(rabbitmq)
    .WithReference(postgresdb)
    .WithReference(rabbitmq)
    .WithReference(migration)
    .WaitForCompletion(migration);

builder.AddProject<AICopilot_RagWorker>("rag-worker")
    .WithReference(postgresdb) // 注入数据库连接
    .WithReference(rabbitmq)   // 注入 RabbitMQ 连接
    .WaitFor(postgresdb)       // 等待数据库启动
    .WaitFor(rabbitmq);        // 等待 MQ 启动

builder.Build().Run();