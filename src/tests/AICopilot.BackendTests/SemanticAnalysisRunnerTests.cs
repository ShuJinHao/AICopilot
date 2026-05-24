using System.Data;
using System.Text.Json;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

public sealed class SemanticAnalysisRunnerTests
{
    [Fact]
    public async Task RunAsync_ShouldBlockRecipeDataReadBeforeBusinessDatabaseAccess()
    {
        var databaseReadService = new RecordingBusinessDatabaseReadService();
        var databaseConnector = new RecordingDatabaseConnector();
        var runner = new SemanticAnalysisRunner(
            new ThrowingCloudAiReadClient(),
            databaseReadService,
            databaseConnector,
            new StubSemanticQueryPlanner(CreateRecipePlan()),
            new ThrowingSemanticPhysicalMappingProvider(),
            new ThrowingSemanticSqlGenerator(),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            NullLogger<SemanticAnalysisRunner>.Instance);

        var result = await runner.RunAsync(
            new IntentResult
            {
                Intent = "Analysis.Recipe.Detail",
                Query = """{"filters":[{"field":"recipeName","operator":"eq","value":"Recipe-Cut-01"}]}"""
            },
            CancellationToken.None);

        result.Should().Contain("当前 AI 不读取云端配方主数据或配方版本数据");
        result.Should().Contain("不能查询具体配方");
        databaseReadService.WasCalled.Should().BeFalse();
        databaseConnector.WasCalled.Should().BeFalse();
    }

    private static SemanticQueryPlan CreateRecipePlan()
    {
        return new SemanticQueryPlan(
            "Analysis.Recipe.Detail",
            SemanticQueryTarget.Recipe,
            SemanticQueryKind.Detail,
            "查看配方 Recipe-Cut-01 详情",
            new SemanticProjection(["recipeName", "version"]),
            [new SemanticFilter("recipeName", SemanticFilterOperator.Equal, "Recipe-Cut-01")],
            null,
            new SemanticSort("updatedAt", SemanticSortDirection.Desc),
            1);
    }

    private sealed class StubSemanticQueryPlanner(SemanticQueryPlan plan) : ISemanticQueryPlanner
    {
        public SemanticPlanningResult Plan(string intent, string? query)
        {
            return SemanticPlanningResult.Success(plan);
        }
    }

    private sealed class ThrowingCloudAiReadClient : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<CloudAiReadPassStationRecordDto>> GetPassStationRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Cloud AiRead must not be called for recipe data.");
        }
    }

    private sealed class RecordingBusinessDatabaseReadService : IBusinessDatabaseReadService
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([]);
        }

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
            DataSourceSelectionMode selectionMode,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([]);
        }

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<BusinessDatabaseConnectionInfo?>(null);
        }
    }

    private sealed class RecordingDatabaseConnector : IDatabaseConnector
    {
        public bool WasCalled { get; private set; }

        public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
        {
            WasCalled = true;
            throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }

        public Task<IEnumerable<dynamic>> ExecuteQueryAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }

        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            throw new InvalidOperationException("Database connector must not be called for recipe data.");
        }
    }

    private sealed class ThrowingSemanticPhysicalMappingProvider : ISemanticPhysicalMappingProvider
    {
        public bool TryGetMapping(SemanticQueryTarget target, out SemanticPhysicalMapping mapping)
        {
            throw new InvalidOperationException("Semantic mapping provider must not be called for recipe data.");
        }
    }

    private sealed class ThrowingSemanticSqlGenerator : ISemanticSqlGenerator
    {
        public GeneratedSemanticSql Generate(SemanticQueryPlan plan, SemanticPhysicalMapping mapping)
        {
            throw new InvalidOperationException("Semantic SQL generator must not be called for recipe data.");
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
