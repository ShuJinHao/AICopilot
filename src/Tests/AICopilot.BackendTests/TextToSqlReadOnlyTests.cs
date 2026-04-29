using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Dapper;
using AICopilot.Dapper.Security;
using AICopilot.DataAnalysisService.Plugins;
using AICopilot.DataAnalysisService.Services;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Document = AICopilot.Core.Rag.Aggregates.KnowledgeBase.Document;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase38Acceptance")]
[Trait("Suite", "Phase43SafetyQuality")]
public sealed class TextToSqlReadOnlyTests
{
    [Fact]
    public async Task Connector_ShouldRejectWritableOrDisabledBusinessDatabases()
    {
        var connector = new DapperDatabaseConnector(new AstSqlGuardrail(), NullLogger<DapperDatabaseConnector>.Instance);

        var writableDatabase = new BusinessDatabase(
            "writable-db",
            "test",
            "Host=localhost;Database=demo;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: false);

        var disabledDatabase = new BusinessDatabase(
            "disabled-db",
            "test",
            "Host=localhost;Database=demo;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true);
        disabledDatabase.UpdateSettings(isEnabled: false, isReadOnly: true);

        var writableAction = async () => await connector.ExecuteQueryAsync(ToConnectionInfo(writableDatabase), "SELECT 1");
        var disabledAction = async () => await connector.GetSchemaInfoAsync(ToConnectionInfo(disabledDatabase));

        await writableAction.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*只读模式*");
        await disabledAction.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已被禁用*");
    }

    [Fact]
    public async Task Plugin_ShouldRejectWritableBusinessDatabaseBeforeExecution()
    {
        var plugin = CreatePlugin(new RecordingDatabaseConnector());
        var writableDatabase = new BusinessDatabase(
            "writable-db",
            "test",
            "Host=localhost;Database=demo;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: false);

        var serviceProvider = BuildServiceProvider([writableDatabase]);

        var tableNames = await plugin.GetTableNamesAsync(serviceProvider, writableDatabase.Name);
        var queryResult = await plugin.ExecuteSqlQueryAsync(serviceProvider, writableDatabase.Name, "SELECT 1");

        tableNames.Should().Contain("只读模式");
        queryResult.Should().Contain("只读模式");
    }

    [Fact]
    public async Task Plugin_ShouldAllowMetadataAndQueryExecution_OnReadonlyBusinessDatabase()
    {
        var connector = new RecordingDatabaseConnector();
        var plugin = CreatePlugin(connector);
        var semanticDatabase = new BusinessDatabase(
            "readonly-db",
            "test",
            "Host=localhost;Database=readonly;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true);

        var serviceProvider = BuildServiceProvider([semanticDatabase]);

        var tableNames = await plugin.GetTableNamesAsync(serviceProvider, semanticDatabase.Name);
        var schema = await plugin.GetTableSchemaAsync(serviceProvider, semanticDatabase.Name, ["devices"]);
        var queryResult = await plugin.ExecuteSqlQueryAsync(
            serviceProvider,
            semanticDatabase.Name,
            "SELECT device_code AS deviceCode FROM device_master_cloud_sim_view ORDER BY device_code LIMIT 1");

        connector.ExecutedSql.Should().HaveCount(3);
        tableNames.Should().Contain("devices");
        schema.Should().Contain("device_code");
        queryResult.Should().Contain("DEV-001");
    }

    [Fact]
    public async Task Plugin_ShouldSurfaceTruncationMetadata_WhenQueryResultIsTrimmed()
    {
        var connector = new RecordingDatabaseConnector
        {
            MetadataResultOverride = new DatabaseQueryResult(
                Enumerable.Range(1, 200)
                    .Select(index => new Dictionary<string, object?>
                    {
                        ["deviceCode"] = $"DEV-{index:000}"
                    })
                    .ToList(),
                ReturnedRowCount: 250,
                IsTruncated: true,
                ElapsedMilliseconds: 12)
        };

        var plugin = CreatePlugin(connector);
        var semanticDatabase = new BusinessDatabase(
            "readonly-db",
            "test",
            "Host=localhost;Database=readonly;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true);

        var serviceProvider = BuildServiceProvider([semanticDatabase]);

        var result = await plugin.ExecuteSqlQueryAsync(
            serviceProvider,
            semanticDatabase.Name,
            "SELECT device_code AS deviceCode FROM device_master_cloud_sim_view ORDER BY device_code");

        result.Should().Contain("结果已截断");
        result.Should().Contain("250");
        result.Should().Contain("200");
    }

    private static DataAnalysisPlugin CreatePlugin(IDatabaseConnector connector)
    {
        return new DataAnalysisPlugin(
            connector,
            NullLogger<DataAnalysisPlugin>.Instance);
    }

    private static IServiceProvider BuildServiceProvider(IReadOnlyCollection<BusinessDatabase> businessDatabases)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReadRepository<BusinessDatabase>>(new InMemoryReadRepository<BusinessDatabase>(businessDatabases));
        services.AddSingleton<VisualizationContext>();
        services.AddSingleton<IAuditLogWriter, NoOpAuditLogWriter>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingDatabaseConnector : IDatabaseConnector
    {
        public List<string> ExecutedSql { get; } = [];
        public DatabaseQueryResult? MetadataResultOverride { get; set; }

        public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
        {
            throw new NotSupportedException("The test connector does not create real database connections.");
        }

        public Task<IEnumerable<dynamic>> ExecuteQueryAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            ExecutedSql.Add(sql);

            if (sql.Contains("information_schema.tables", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IEnumerable<dynamic>>(
                [
                    new Dictionary<string, object>
                    {
                        ["TableName"] = "devices",
                        ["Description"] = "device master"
                    }
                ]);
            }

            if (sql.Contains("information_schema.columns", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IEnumerable<dynamic>>(
                [
                    new Dictionary<string, object>
                    {
                        ["ColumnName"] = "device_code",
                        ["DataType"] = "text",
                        ["IsPrimaryKey"] = 0,
                        ["Description"] = "device code"
                    }
                ]);
            }

            return Task.FromResult<IEnumerable<dynamic>>(
            [
                new Dictionary<string, object>
                {
                    ["deviceCode"] = "DEV-001"
                }
            ]);
        }

        public async Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (MetadataResultOverride != null)
            {
                ExecutedSql.Add(sql);
                return MetadataResultOverride;
            }

            var rows = (await ExecuteQueryAsync(database, sql, parameters, cancellationToken))
                .Select(row => row is IDictionary<string, object> dictionary
                    ? dictionary.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object?>())
                .ToList();

            return new DatabaseQueryResult(rows, rows.Count, false, 0);
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            return ExecuteQueryAsync(database, "SELECT table_name FROM information_schema.tables", cancellationToken: cancellationToken);
        }
    }

    private static BusinessDatabaseConnectionInfo ToConnectionInfo(BusinessDatabase database)
    {
        return new BusinessDatabaseConnectionInfo(
            database.Id,
            database.Name,
            database.Description,
            database.ConnectionString,
            database.Provider switch
            {
                DbProviderType.PostgreSql => DatabaseProviderType.PostgreSql,
                DbProviderType.SqlServer => DatabaseProviderType.SqlServer,
                DbProviderType.MySql => DatabaseProviderType.MySql,
                _ => throw new ArgumentOutOfRangeException(nameof(database.Provider), database.Provider, "Unsupported database provider.")
            },
            database.IsEnabled,
            database.IsReadOnly);
    }

    private sealed class NoOpAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }
}
