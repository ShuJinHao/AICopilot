using Microsoft.Extensions.Configuration;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var appHostOptions = AppHostOptions.FromConfiguration(builder.Configuration);

if (appHostOptions.EnableDockerComposeEnvironment)
{
    builder.AddDockerComposeEnvironment("compose");
}

var password = builder.AddParameter("pg-password", secret: true);

var postgresdb = builder.AddPostgres("postgres", password: password);

if (appHostOptions.PersistentContainers)
{
    postgresdb = postgresdb.WithDataVolume(appHostOptions.PostgresVolumeName);
}

if (appHostOptions.PostgresHostPort.HasValue)
{
    postgresdb = postgresdb.WithHostPort(appHostOptions.PostgresHostPort.Value);
}

postgresdb = postgresdb.WithBindMount("./Sql", "/docker-entrypoint-initdb.d");

if (appHostOptions.EnablePgWeb)
{
    postgresdb = postgresdb.WithPgWeb(pgAdmin =>
    {
        if (appHostOptions.PgWebHostPort.HasValue)
        {
            pgAdmin.WithHostPort(appHostOptions.PgWebHostPort.Value);
        }
    });
}

var aiCopilotDatabase = postgresdb.AddDatabase("ai-copilot");
var cloudDeviceSemanticSimDatabase = postgresdb.AddDatabase("cloud-device-semantic-sim");

var rabbitmq = builder.AddRabbitMQ("eventbus")
    .WithManagementPlugin();

if (appHostOptions.PersistentContainers)
{
    rabbitmq = rabbitmq.WithLifetime(ContainerLifetime.Persistent);
}

var qdrant = builder.AddQdrant("qdrant");

if (appHostOptions.PersistentContainers)
{
    qdrant = qdrant
        .WithDataVolume(appHostOptions.QdrantVolumeName)
        .WithLifetime(ContainerLifetime.Persistent);
}

var finalAgentContextRedis = builder.AddRedis("final-agent-context-redis");

if (appHostOptions.PersistentContainers)
{
    finalAgentContextRedis = finalAgentContextRedis
        .WithDataVolume(appHostOptions.RedisVolumeName)
        .WithLifetime(ContainerLifetime.Persistent);
}

var migration = builder.AddProject<AICopilot_MigrationWorkApp>("aicopilot-migration")
    .WithReference(aiCopilotDatabase)
    .WithReference(cloudDeviceSemanticSimDatabase)
    .WaitFor(postgresdb);

var httpapi = builder.AddProject<AICopilot_HttpApi>("aicopilot-httpapi")
    .WithUrl("swagger")
    .WaitFor(postgresdb)
    .WaitFor(rabbitmq)
    .WaitFor(qdrant)
    .WaitFor(finalAgentContextRedis)
    .WithReference(aiCopilotDatabase)
    .WithReference(rabbitmq)
    .WithReference(migration)
    .WithReference(qdrant)
    .WithReference(finalAgentContextRedis)
    .WithEnvironment("AiGateway__FinalAgentContextStore__Provider", "Redis")
    .WaitForCompletion(migration);

if (appHostOptions.EnableRagWorker)
{
    builder.AddProject<AICopilot_RagWorker>("rag-worker")
        .WithReference(aiCopilotDatabase)
        .WithReference(rabbitmq)
        .WithReference(qdrant)
        .WithReference(migration)
        .WaitFor(postgresdb)
        .WaitFor(rabbitmq)
        .WaitFor(qdrant)
        .WaitForCompletion(migration);
}

if (appHostOptions.EnableDataWorker)
{
    builder.AddProject<AICopilot_DataWorker>("data-worker")
        .WithReference(aiCopilotDatabase)
        .WithReference(rabbitmq)
        .WaitFor(postgresdb)
        .WaitFor(rabbitmq)
        .WaitForCompletion(migration);
}

if (appHostOptions.EnableWebUi)
{
    builder.AddViteApp("aicopilot-webui", "../../Vues/AICopilot.Web")
        .WithExternalHttpEndpoints()
        .WithReference(httpapi)
        .PublishAsDockerFile();
}

builder.Build().Run();

internal sealed record AppHostOptions(
    bool EnableDockerComposeEnvironment,
    bool EnableWebUi,
    bool EnablePgWeb,
    bool EnableRagWorker,
    bool EnableDataWorker,
    bool PersistentContainers,
    int? PostgresHostPort,
    int? PgWebHostPort,
    string PostgresVolumeName,
    string QdrantVolumeName,
    string RedisVolumeName)
{
    public static AppHostOptions FromConfiguration(IConfiguration configuration)
    {
        var persistentContainers = configuration.GetValue("AppHost:PersistentContainers", true);

        return new AppHostOptions(
            configuration.GetValue("AppHost:EnableDockerComposeEnvironment", true),
            configuration.GetValue("AppHost:EnableWebUi", true),
            configuration.GetValue("AppHost:EnablePgWeb", true),
            configuration.GetValue("AppHost:EnableRagWorker", true),
            configuration.GetValue("AppHost:EnableDataWorker", true),
            persistentContainers,
            configuration.GetValue<int?>("AppHost:PostgresHostPort") ?? (persistentContainers ? 5432 : null),
            configuration.GetValue<int?>("AppHost:PgWebHostPort") ?? (persistentContainers ? 5050 : null),
            configuration["AppHost:PostgresVolumeName"] ?? "postgres-aicopilot",
            configuration["AppHost:QdrantVolumeName"] ?? "qdrant-datas",
            configuration["AppHost:RedisVolumeName"] ?? "redis-aicopilot");
    }
}
