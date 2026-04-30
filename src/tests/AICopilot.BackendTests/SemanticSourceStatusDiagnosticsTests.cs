using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using System.Data;
using Document = AICopilot.Core.Rag.Aggregates.KnowledgeBase.Document;

namespace AICopilot.BackendTests;

public sealed class SemanticSourceStatusDiagnosticsTests
{
    [Fact]
    public async Task Inspector_ShouldTreatQueryableSourceAsReady()
    {
        var database = CreateReadonlyDatabase();
        var mapping = CreateDeviceMapping();
        var connector = new SemanticInspectionDatabaseConnector();
        var inspector = new SemanticSourceInspector(connector);

        var inspection = await inspector.InspectAsync(ToConnectionInfo(database), mapping);

        inspection.SourceExists.Should().BeTrue();
        inspection.MissingRequiredFields.Should().BeEmpty();
        connector.ExecutedSql.Should().Contain(sql => sql.Contains("FROM vw_device_readonly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspector_ShouldReturnSourceNotFound_WhenSourceProbeFails()
    {
        var database = CreateReadonlyDatabase();
        var mapping = CreateDeviceMapping(sourceName: "missing_device_source");
        var connector = new SemanticInspectionDatabaseConnector
        {
            MissingSources = ["missing_device_source"]
        };
        var inspector = new SemanticSourceInspector(connector);

        var inspection = await inspector.InspectAsync(ToConnectionInfo(database), mapping);

        inspection.SourceExists.Should().BeFalse();
        inspection.MissingRequiredFields.Should().BeEmpty();
    }

    [Fact]
    public async Task Inspector_ShouldReturnMissingFields_WhenReadonlyContractIsIncomplete()
    {
        var database = CreateReadonlyDatabase();
        var mapping = CreateDeviceMapping(lineNameExpression: "missing_line_name");
        var connector = new SemanticInspectionDatabaseConnector
        {
            MissingExpressions = ["missing_line_name"]
        };
        var inspector = new SemanticSourceInspector(connector);

        var inspection = await inspector.InspectAsync(ToConnectionInfo(database), mapping);

        inspection.SourceExists.Should().BeTrue();
        inspection.MissingRequiredFields.Should().Equal("lineName");
    }

    [Fact]
    public async Task QueryHandler_ShouldReturnDatabaseNotFound_WhenMappedDatabaseIsMissing()
    {
        var handler = new GetSemanticSourceStatusQueryHandler(
            new SampleSemanticPhysicalMappingProvider(),
            new InMemoryReadRepository<BusinessDatabase>(),
            new StubSemanticSourceInspector(new SemanticSourceInspection(true, [])));

        var result = await handler.Handle(new GetSemanticSourceStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Should().Contain(item =>
            item.Target == nameof(SemanticQueryTarget.Device) &&
            item.Status == SemanticSourceStatusValues.DatabaseNotFound &&
            item.DatabaseName == "SemanticDb");
    }

    [Fact]
    public async Task QueryHandler_ShouldReturnNotReadOnly_WhenBusinessDatabaseIsWritable()
    {
        var database = CreateReadonlyDatabase(name: "SemanticDb");
        database.UpdateSettings(isEnabled: true, isReadOnly: false);

        var handler = new GetSemanticSourceStatusQueryHandler(
            new SampleSemanticPhysicalMappingProvider(),
            new InMemoryReadRepository<BusinessDatabase>([database]),
            new StubSemanticSourceInspector(new SemanticSourceInspection(true, [])));

        var result = await handler.Handle(new GetSemanticSourceStatusQuery(), CancellationToken.None);

        result.Value!.Should().Contain(item =>
            item.Target == nameof(SemanticQueryTarget.Device) &&
            item.Status == SemanticSourceStatusValues.NotReadOnly &&
            item.IsReadOnly == false);
    }

    [Fact]
    public async Task QueryHandler_ShouldReturnProviderMismatch_WhenBusinessDatabaseProviderDiffersFromMapping()
    {
        var database = new BusinessDatabase(
            "SemanticDb",
            "test",
            "Server=(local);Database=demo;Trusted_Connection=True;",
            DbProviderType.SqlServer,
            isReadOnly: true);

        var inspector = new StubSemanticSourceInspector(new SemanticSourceInspection(true, []));
        var handler = new GetSemanticSourceStatusQueryHandler(
            new SampleSemanticPhysicalMappingProvider(),
            new InMemoryReadRepository<BusinessDatabase>([database]),
            inspector);

        var result = await handler.Handle(new GetSemanticSourceStatusQuery(), CancellationToken.None);

        result.Value!.Should().Contain(item =>
            item.Target == nameof(SemanticQueryTarget.Device) &&
            item.Status == SemanticSourceStatusValues.ProviderMismatch &&
            item.ProviderMatched == false);
        inspector.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task QueryHandler_ShouldReturnFieldMismatch_WhenInspectorReportsMissingFields()
    {
        var database = CreateReadonlyDatabase(name: "SemanticDb");
        var missingFields = new[] { "lineName" };
        var inspector = new StubSemanticSourceInspector(new SemanticSourceInspection(true, missingFields));
        var handler = new GetSemanticSourceStatusQueryHandler(
            new SampleSemanticPhysicalMappingProvider(),
            new InMemoryReadRepository<BusinessDatabase>([database]),
            inspector);

        var result = await handler.Handle(new GetSemanticSourceStatusQuery(), CancellationToken.None);

        result.Value!.Should().Contain(item =>
            item.Target == nameof(SemanticQueryTarget.Device) &&
            item.Status == SemanticSourceStatusValues.FieldMismatch &&
            item.SourceExists &&
            item.ProviderMatched &&
            item.MissingRequiredFields.SequenceEqual(missingFields));
    }

    private static BusinessDatabase CreateReadonlyDatabase(string name = "DeviceSemanticReadonly")
    {
        return new BusinessDatabase(
            name,
            "test",
            "Host=localhost;Database=demo;Username=test;Password=test;",
            DbProviderType.PostgreSql,
            isReadOnly: true);
    }

    private static SemanticPhysicalMapping CreateDeviceMapping(
        string sourceName = "vw_device_readonly",
        string lineNameExpression = "line_name")
    {
        return new SemanticPhysicalMapping(
            SemanticQueryTarget.Device,
            DatabaseProviderType.PostgreSql,
            sourceName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deviceId"] = "device_id",
                ["deviceCode"] = "device_code",
                ["deviceName"] = "device_name",
                ["status"] = "status",
                ["lineName"] = lineNameExpression,
                ["updatedAt"] = "updated_at"
            },
            allowedProjectionFields: SemanticSourceContractCatalog.GetRequiredFields(SemanticQueryTarget.Device),
            allowedFilterFields: ["deviceCode", "deviceName", "status", "lineName"],
            allowedSortFields: ["deviceCode", "updatedAt"],
            databaseName: "DeviceSemanticReadonly");
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

    private sealed class StubSemanticSourceInspector(SemanticSourceInspection inspection) : ISemanticSourceInspector
    {
        public bool WasCalled { get; private set; }

        public Task<SemanticSourceInspection> InspectAsync(
            BusinessDatabaseConnectionInfo database,
            SemanticPhysicalMapping mapping,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(inspection);
        }
    }

    private sealed class SemanticInspectionDatabaseConnector : IDatabaseConnector
    {
        public List<string> ExecutedSql { get; } = [];

        public HashSet<string> MissingSources { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> MissingExpressions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

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

            foreach (var source in MissingSources)
            {
                if (sql.Contains($"FROM {source}", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Source '{source}' does not exist.");
                }
            }

            foreach (var expression in MissingExpressions)
            {
                if (sql.Contains(expression, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Expression '{expression}' does not exist.");
                }
            }

            return Task.FromResult<IEnumerable<dynamic>>([]);
        }

        public async Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await ExecuteQueryAsync(database, sql, parameters, cancellationToken);
            return new DatabaseQueryResult([], 0, false, 0);
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<dynamic>>([]);
        }
    }

}
