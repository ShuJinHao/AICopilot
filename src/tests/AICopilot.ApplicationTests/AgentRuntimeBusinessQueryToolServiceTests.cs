using System.Data;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.ApplicationTests;

public sealed class AgentRuntimeBusinessQueryToolServiceTests
{
    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldUseCloudReadOnlyFallback_WhenPlanSelectsCloudSource()
    {
        var database = CreateCloudReadOnlyDatabase();
        var readService = new RecordingBusinessDatabaseReadService(database);
        var generator = new FixedTextToSqlGenerator("SELECT d.client_code FROM devices d LIMIT 10");
        var runner = new CloudReadOnlyTextToSqlFallbackRunner(
            generator,
            new RecordingConnector(new DatabaseQueryResult(
                [
                    new Dictionary<string, object?>
                    {
                        ["client_code"] = "DEV-001"
                    }
                ],
                ReturnedRowCount: 1,
                IsTruncated: false,
                ElapsedMilliseconds: 2)),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()));
        var service = new AgentRuntimeBusinessQueryToolService(
            readService,
            businessTextToSqlRuntime: null,
            runner);
        var state = new AgentTaskRunState();

        var output = await service.QueryBusinessDatabaseReadonlyP1Async(
            CreatePlan(database.Id),
            state,
            CancellationToken.None);

        readService.SelectionMode.Should().Be(DataSourceSelectionMode.Agent);
        generator.Requests.Should().ContainSingle();
        state.CloudReadonlySourceMode.Should().Be(DataSourceExternalSystemType.CloudReadOnly.ToString());
        state.CloudReadonlyIsSimulation.Should().BeFalse();
        state.CloudReadonlyRowCount.Should().Be(1);
        state.CloudReadonlySourcePath.Should().Be("BusinessDataSourceCenter/CloudReadOnlyTextToSql");
        state.BusinessQueryResults.Should().ContainSingle()
            .Which.SourceMode.Should().Be(DataSourceExternalSystemType.CloudReadOnly.ToString());

        var json = JsonSerializer.Serialize(output, JsonSerializerOptions.Web);
        json.Should().Contain("CloudReadOnly");
        json.Should().Contain("DEV-001");
        json.Should().NotContain("SELECT");
    }

    private static AgentTaskPlanDocument CreatePlan(Guid dataSourceId)
    {
        return new AgentTaskPlanDocument(
            1,
            "agent_planner",
            "查看设备列表",
            AgentTaskType.DataAnalysis.ToString(),
            AgentTaskRiskLevel.Low.ToString(),
            [],
            [],
            null,
            [],
            new AgentTaskPlanRuntimeSettingsDocument(30, 12000),
            DataSourceIds: [dataSourceId],
            QueryMode: "TextToSql");
    }

    private static BusinessDatabaseConnectionInfo CreateCloudReadOnlyDatabase()
    {
        return new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "CloudPlatformReadonly",
            "Cloud Platform readonly business data",
            "Host=localhost;Database=cloud;Username=readonly;Password=fake-test-only",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: true,
            DefaultQueryLimit: 10,
            MaxQueryLimit: 100,
            IsSelectableInAgent: true);
    }

    private sealed class RecordingBusinessDatabaseReadService(BusinessDatabaseConnectionInfo database)
        : IBusinessDatabaseReadService
    {
        public DataSourceSelectionMode? SelectionMode { get; private set; }

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([ToDescriptor(database)]);
        }

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
            DataSourceSelectionMode selectionMode,
            CancellationToken cancellationToken = default)
        {
            SelectionMode = selectionMode;
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([ToDescriptor(database)]);
        }

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<BusinessDatabaseConnectionInfo?>(
                string.Equals(name, database.Name, StringComparison.OrdinalIgnoreCase)
                    ? database
                    : null);
        }

        private static BusinessDatabaseDescriptor ToDescriptor(BusinessDatabaseConnectionInfo database)
        {
            return new BusinessDatabaseDescriptor(
                database.Id,
                database.Name,
                database.Description,
                database.Provider,
                database.IsEnabled,
                database.IsReadOnly,
                database.ExternalSystemType,
                database.ReadOnlyCredentialVerified,
                database.Category,
                database.Tags,
                database.OwnerDepartment,
                database.BusinessDomain,
                database.SensitivityLevel,
                database.DefaultQueryLimit,
                database.MaxQueryLimit,
                database.IsSelectableInChat,
                database.IsSelectableInAgent);
        }
    }

    private sealed class FixedTextToSqlGenerator(string sql) : ICloudReadOnlyTextToSqlGenerator
    {
        public List<CloudReadOnlyTextToSqlGenerationRequest> Requests { get; } = [];

        public Task<CloudReadOnlyTextToSqlGenerationResult> GenerateAsync(
            CloudReadOnlyTextToSqlGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(CloudReadOnlyTextToSqlGenerationResult.Success(sql, "fixed sql"));
        }
    }

    private sealed class RecordingConnector(DatabaseQueryResult result) : IDatabaseConnector
    {
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
            throw new NotSupportedException("The test connector only supports metadata query execution.");
        }

        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The test connector does not read schema.");
        }
    }

    private sealed class NoopAuditLogWriter : IAuditLogWriter
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
