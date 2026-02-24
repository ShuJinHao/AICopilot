using Projects;

var builder = DistributedApplication.CreateBuilder(args);

//  定义一个固定的密码参数 (Secret)
var password = builder.AddParameter("pg-password", secret: true);

var postgresdb = builder.AddPostgres("postgres", password: password)
    .WithHostPort(5432)
    .WithDataVolume("postgres-aicopilot")
    .WithBindMount("./Sql", "/docker-entrypoint-initdb.d")
    .WithPgWeb(pgAdmin => pgAdmin.WithHostPort(5050))
    .AddDatabase("ai-copilot");

var rabbitmq = builder.AddRabbitMQ("eventbus")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume("qdrant-datas")
    .WithLifetime(ContainerLifetime.Persistent);

var migration = builder.AddProject<AICopilot_MigrationWorkApp>("aicopilot-migration")
    .WithReference(postgresdb)
    .WaitFor(postgresdb);

var httpapi = builder.AddProject<AICopilot_HttpApi>("aicopilot-httpapi")
    .WithUrl("swagger")
    .WaitFor(postgresdb)
    .WaitFor(rabbitmq)
    .WaitFor(qdrant)
    .WithReference(postgresdb)
    .WithReference(rabbitmq)
    .WithReference(migration)
    .WithReference(qdrant)
    .WaitForCompletion(migration);

builder.AddProject<AICopilot_RagWorker>("rag-worker")
    .WithReference(postgresdb) // 注入数据库连接
    .WithReference(rabbitmq)   // 注入 RabbitMQ 连接
    .WithReference(qdrant)
    .WaitFor(postgresdb)       // 等待数据库启动
    .WaitFor(rabbitmq)        // 等待 MQ 启动
    .WaitFor(qdrant);

builder.AddViteApp("aicopilot-webui", "../../Vues/AICopilot.Web")
    .WithEndpoint("http", endpointAnnotation =>
    {
        endpointAnnotation.Port = 5173;
    })
    .WaitFor(httpapi)
    .WithReference(httpapi);

builder.Build().Run();