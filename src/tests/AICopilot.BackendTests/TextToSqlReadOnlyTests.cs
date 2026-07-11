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
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.DataAnalysisService.Plugins;
using AICopilot.DataAnalysisService.Services;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using System.Data;
using Microsoft.Extensions.Logging;
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
    public async Task Plugin_ShouldReturnStableMessagesForDisabledDatabaseAndRejectedSql()
    {
        var disabledDatabase = new BusinessDatabase(
            "disabled-db",
            "test",
            "Host=localhost;Database=demo;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true);
        disabledDatabase.UpdateSettings(isEnabled: false, isReadOnly: true);

        var disabledResult = await CreatePlugin(new RecordingDatabaseConnector())
            .ExecuteSqlQueryAsync(BuildServiceProvider([disabledDatabase]), disabledDatabase.Name, "SELECT 1");

        disabledResult.Should().Contain("安全警告");
        disabledResult.Should().Contain("已被禁用");

        var rejectingConnector = new RecordingDatabaseConnector
        {
            MetadataExceptionOverride = new InvalidOperationException("Only SELECT queries are allowed.")
        };
        var readonlyDatabase = new BusinessDatabase(
            "readonly-db",
            "test",
            "Host=localhost;Database=readonly;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.SimulationBusiness);

        var rejectedResult = await CreatePlugin(rejectingConnector)
            .ExecuteSqlQueryAsync(BuildServiceProvider([readonlyDatabase]), readonlyDatabase.Name, "DROP TABLE users");

        rejectedResult.Should().Contain("安全警告");
        rejectedResult.Should().Contain("查询被系统拒绝");
        rejectedResult.Should().Contain("仅允许 SELECT");
        rejectedResult.Should().NotContain("Only SELECT");
    }

    [Fact]
    public async Task Plugin_ShouldRedactRejectedSqlDetailsFromResultAndLogs()
    {
        var logger = new CapturingLogger<DataAnalysisPlugin>();
        var rejectingConnector = new RecordingDatabaseConnector
        {
            MetadataExceptionOverride = new InvalidOperationException(
                "Only SELECT queries are allowed. SQL=SELECT * FROM secrets WHERE token='abc'; Host=127.0.0.1;Password=test;")
        };
        var readonlyDatabase = new BusinessDatabase(
            "readonly-db",
            "test",
            "Host=localhost;Database=readonly;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.SimulationBusiness);

        var result = await CreatePlugin(rejectingConnector, logger)
            .ExecuteSqlQueryAsync(
                BuildServiceProvider([readonlyDatabase]),
                readonlyDatabase.Name,
                "DROP TABLE secrets");

        result.Should().Contain("安全警告");
        result.Should().Contain("仅允许 SELECT");
        result.Should().NotContain("Only SELECT queries are allowed");
        result.Should().NotContain("SELECT * FROM secrets");
        result.Should().NotContain("token='abc'");
        result.Should().NotContain("Host=127.0.0.1");
        result.Should().NotContain("Password=test");

        var log = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning).Subject;
        log.Exception.Should().BeNull();
        log.Message.Should().Contain("ReasonCode=non_select");
        log.Message.Should().Contain("OriginalMessage=hidden_by_security_policy");
        log.Message.Should().NotContain("Only SELECT queries are allowed");
        log.Message.Should().NotContain("SELECT * FROM secrets");
        log.Message.Should().NotContain("token='abc'");
        log.Message.Should().NotContain("Host=127.0.0.1");
        log.Message.Should().NotContain("Password=test");
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
            isReadOnly: true,
            BusinessDataExternalSystemType.SimulationBusiness);

        var serviceProvider = BuildServiceProvider([semanticDatabase]);

        var tableNames = await plugin.GetTableNamesAsync(serviceProvider, semanticDatabase.Name);
        var schema = await plugin.GetTableSchemaAsync(serviceProvider, semanticDatabase.Name, ["devices"]);
        var queryResult = await plugin.ExecuteSqlQueryAsync(
            serviceProvider,
            semanticDatabase.Name,
            "SELECT device_code AS deviceCode FROM production_devices ORDER BY device_code LIMIT 1");

        connector.ExecutedSql.Should().HaveCount(3);
        tableNames.Should().Contain("devices");
        schema.Should().Contain("device_code");
        queryResult.Should().Contain("DEV-001");
    }

    [Fact]
    public async Task Plugin_ShouldFilterCloudReadOnlyMetadataDiscovery_ToGovernedSchema()
    {
        var connector = new RecordingDatabaseConnector();
        var plugin = CreatePlugin(connector);
        var cloudReadOnlyDatabase = new BusinessDatabase(
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true);

        var serviceProvider = BuildServiceProvider([cloudReadOnlyDatabase]);

        var tableNames = await plugin.GetTableNamesAsync(serviceProvider, cloudReadOnlyDatabase.Name);
        var schema = await plugin.GetTableSchemaAsync(
            serviceProvider,
            cloudReadOnlyDatabase.Name,
            ["devices", "recipes", "pg_user"]);

        tableNames.Should().Contain("devices");
        tableNames.Should().Contain("device_logs");
        tableNames.Should().NotContain("recipes");
        tableNames.Should().NotContain("pg_user");
        schema.Should().Contain("CREATE TABLE devices");
        schema.Should().Contain("client_code");
        schema.Should().NotContain("device_code");
        schema.Should().NotContain("bootstrap_secret");
        schema.Should().NotContain("password");
        schema.Should().NotContain("CREATE TABLE recipes");
        schema.Should().NotContain("pg_user");
    }


    [Fact]
    public async Task Plugin_ShouldSurfaceTruncationMetadata_WhenQueryResultIsTrimmed()
    {
        var auditLogWriter = new CapturingAuditLogWriter();
        var connector = new RecordingDatabaseConnector
        {
            MetadataResultOverride = new DatabaseQueryResult(
                Enumerable.Range(1, 200)
                    .Select(index => new Dictionary<string, object?>
                    {
                        ["deviceCode"] = $"DEV-{index:000}"
                    })
                    .ToList(),
                ReturnedRowCount: 201,
                IsTruncated: true,
                ElapsedMilliseconds: 12)
        };

        var plugin = CreatePlugin(connector);
        var semanticDatabase = new BusinessDatabase(
            "readonly-db",
            "test",
            "Host=localhost;Database=readonly;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.SimulationBusiness);

        var serviceProvider = BuildServiceProvider([semanticDatabase], auditLogWriter);

        var result = await plugin.ExecuteSqlQueryAsync(
            serviceProvider,
            semanticDatabase.Name,
            "SELECT device_code AS deviceCode FROM production_devices ORDER BY device_code");

        result.Should().Contain("结果已截断");
        result.Should().Contain("至少 201");
        result.Should().Contain("前 50");
        result.Should().NotContain("共返回");
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.Summary.Should().Contain("RowsObserved=201");
        audit.Summary.Should().NotContain("Rows=201");
    }

    [Fact]
    public async Task Connector_ShouldRedactSqlWhenExecutionFails()
    {
        var logger = new CapturingLogger<DapperDatabaseConnector>();
        var connector = new DapperDatabaseConnector(new AstSqlGuardrail(), logger);
        const string sensitiveSql = "SELECT * FROM orders WHERE customer_name = 'secret customer'";
        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "broken-readonly-db",
            "test",
            "Host=127.0.0.1;Port=1;Database=demo;Username=test;Password=test;Timeout=1;Command Timeout=1",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true);

        var act = async () => await connector.ExecuteQueryWithMetadataAsync(database, sensitiveSql);

        await act.Should().ThrowAsync<Exception>();
        var log = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Subject;
        log.Message.Should().Contain("SqlLength=");
        log.Message.Should().Contain("SqlSha256=");
        log.Message.Should().Contain("Provider=PostgreSql");
        log.Message.Should().Contain("ErrorType=");
        log.Exception.Should().BeNull();
        log.Message.Should().NotContain(sensitiveSql);
        log.Message.Should().NotContain("secret customer");
        log.Message.Should().NotContain("Host=127.0.0.1");
        log.Message.Should().NotContain("Password=test");
        log.Message.Should().NotContain("SQL:");
    }

    private static DataAnalysisPlugin CreatePlugin(
        IDatabaseConnector connector,
        ILogger<DataAnalysisPlugin>? logger = null)
    {
        return new DataAnalysisPlugin(
            connector,
            logger ?? NullLogger<DataAnalysisPlugin>.Instance);
    }

    private static IServiceProvider BuildServiceProvider(
        IReadOnlyCollection<BusinessDatabase> businessDatabases,
        IAuditLogWriter? auditLogWriter = null)
    {
        var services = new ServiceCollection();
        var currentUser = new TestCurrentUser(role: "Analyst");
        var grants = businessDatabases
            .Select(database => new DataSourcePermissionGrant(
                database.Id,
                DataSourcePermissionGrantTargetType.User,
                currentUser.Id!.Value.ToString("D"),
                canQuery: true,
                canSchemaView: true))
            .ToArray();
        services.AddSingleton<IReadRepository<BusinessDatabase>>(new InMemoryReadRepository<BusinessDatabase>(businessDatabases));
        services.AddSingleton<IReadRepository<DataSourcePermissionGrant>>(new InMemoryReadRepository<DataSourcePermissionGrant>(grants));
        services.AddSingleton<ICurrentUser>(currentUser);
        services.AddSingleton<BusinessDatabaseAccessService>();
        services.AddSingleton<VisualizationContext>();
        services.AddSingleton(auditLogWriter ?? new NoOpAuditLogWriter());
        return services.BuildServiceProvider();
    }

    private sealed class RecordingDatabaseConnector : IDatabaseConnector
    {
        public List<string> ExecutedSql { get; } = [];
        public DatabaseQueryResult? MetadataResultOverride { get; set; }
        public Exception? MetadataExceptionOverride { get; set; }

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
                    },
                    new Dictionary<string, object>
                    {
                        ["TableName"] = "device_logs",
                        ["Description"] = "device logs"
                    },
                    new Dictionary<string, object>
                    {
                        ["TableName"] = "recipes",
                        ["Description"] = "recipe master"
                    },
                    new Dictionary<string, object>
                    {
                        ["TableName"] = "pg_user",
                        ["Description"] = "system users"
                    }
                ]);
            }

            if (sql.Contains("information_schema.columns", StringComparison.OrdinalIgnoreCase))
            {
                var tableName = ReadTableName(parameters);
                if (string.Equals(tableName, "recipes", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult<IEnumerable<dynamic>>(
                    [
                        new Dictionary<string, object>
                        {
                            ["ColumnName"] = "recipe_name",
                            ["DataType"] = "text",
                            ["IsPrimaryKey"] = 0,
                            ["Description"] = "recipe name"
                        }
                    ]);
                }

                return Task.FromResult<IEnumerable<dynamic>>(
                [
                    new Dictionary<string, object>
                    {
                        ["ColumnName"] = "device_code",
                        ["DataType"] = "text",
                        ["IsPrimaryKey"] = 0,
                        ["Description"] = "device code"
                    },
                    new Dictionary<string, object>
                    {
                        ["ColumnName"] = "client_code",
                        ["DataType"] = "text",
                        ["IsPrimaryKey"] = 0,
                        ["Description"] = "Cloud device client code"
                    },
                    new Dictionary<string, object>
                    {
                        ["ColumnName"] = "bootstrap_secret_hash",
                        ["DataType"] = "text",
                        ["IsPrimaryKey"] = 0,
                        ["Description"] = "secret"
                    },
                    new Dictionary<string, object>
                    {
                        ["ColumnName"] = "password_hash",
                        ["DataType"] = "text",
                        ["IsPrimaryKey"] = 0,
                        ["Description"] = "password"
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
            if (MetadataExceptionOverride != null)
            {
                throw MetadataExceptionOverride;
            }

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

        private static string? ReadTableName(object? parameters)
        {
            return parameters?.GetType().GetProperty("TableName")?.GetValue(parameters)?.ToString();
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

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.Count);
        }
    }

}
