using System.Data;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.BackendTests;

[Trait("Suite", "AICopilotM5EnterpriseDataSourcePlatformization")]
public sealed class AICopilotM5EnterpriseDataSourcePlatformizationTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task SourceSelection_ShouldApplyAuthorizationAndChatAgentSelectionFlags()
    {
        var both = CreateSimulationDatabase("sim-both");
        var chatOnly = CreateSimulationDatabase("sim-chat", isSelectableInAgent: false);
        var agentOnly = CreateSimulationDatabase("sim-agent", isSelectableInChat: false);
        var disabled = CreateSimulationDatabase("sim-disabled", isEnabled: false);
        var ungranted = CreateSimulationDatabase("sim-ungranted");
        var writable = CreateDatabase("legacy-writable", BusinessDataExternalSystemType.Unknown, isReadOnly: false);
        var grants = new[]
        {
            GrantToRole(both, canQuery: true),
            GrantToRole(chatOnly, canQuery: true),
            GrantToRole(agentOnly, canQuery: true),
            GrantToRole(disabled, canQuery: true),
            GrantToRole(writable, canQuery: true)
        };
        var service = CreateReadService([both, chatOnly, agentOnly, disabled, ungranted, writable], grants);

        var chatSources = await service.ListSelectableAsync(DataSourceSelectionMode.Chat);
        var agentSources = await service.ListSelectableAsync(DataSourceSelectionMode.Agent);

        chatSources.Select(item => item.Name).Should().BeEquivalentTo("sim-both", "sim-chat");
        agentSources.Select(item => item.Name).Should().BeEquivalentTo("sim-both", "sim-agent");
        agentSources.Should().OnlyContain(item => item.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness);
    }

    [Fact]
    public async Task Grants_ShouldAuthorizeUserRoleAndDepartmentTargets()
    {
        var userSource = CreateSimulationDatabase("sim-user");
        var roleSource = CreateSimulationDatabase("sim-role");
        var departmentSource = CreateSimulationDatabase("sim-department");
        var access = new BusinessDatabaseAccessService(
            new InMemoryReadRepository<DataSourcePermissionGrant>(
            [
                new DataSourcePermissionGrant(userSource.Id, DataSourcePermissionGrantTargetType.User, UserId.ToString("D"), true, false),
                new DataSourcePermissionGrant(roleSource.Id, DataSourcePermissionGrantTargetType.Role, "Analyst", true, false),
                new DataSourcePermissionGrant(departmentSource.Id, DataSourcePermissionGrantTargetType.Department, "Quality", true, false)
            ]),
            new TestCurrentUser(UserId, role: "Analyst", cloudDepartmentName: "Quality"));

        (await access.CanQueryAsync(userSource)).Should().BeTrue();
        (await access.CanQueryAsync(roleSource)).Should().BeTrue();
        (await access.CanQueryAsync(departmentSource)).Should().BeTrue();
    }

    [Theory]
    [InlineData("SELECT * FROM employees", "Wildcard")]
    [InlineData("SELECT employee_id FROM recipe_versions", "not allowed")]
    [InlineData("SELECT password FROM employees", "Sensitive")]
    [InlineData("DROP TABLE employees", "Only SELECT")]
    [InlineData("SELECT employee_id FROM employees; SELECT employee_id FROM employees", "Multiple")]
    [InlineData("SELECT table_name FROM information_schema.tables", "System catalog")]
    public async Task QueryExecution_ShouldRejectUngovernedSql(string sql, string expectedMessage)
    {
        var database = CreateSimulationDatabase("sim-secure");
        var connector = new RecordingDatabaseConnector();
        var executor = CreateExecutor(database, [GrantToRole(database, canQuery: true)], connector, new CapturingAuditLogWriter());

        var result = await executor.ExecuteAsync(
            database.Id,
            sql,
            limit: 10,
            requireSimulationBusiness: false,
            safetySchema: null,
            auditAction: "DataSource.Query",
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Select(error => error.ToString() ?? string.Empty)
            .Should().Contain(error => error.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
        connector.ExecuteCount.Should().Be(0);
    }

    [Fact]
    public async Task QueryExecution_ShouldReturnSanitizedBoundedPreviewAndSafeAuditOnly()
    {
        var database = CreateSimulationDatabase("sim-safe-output");
        var audit = new CapturingAuditLogWriter();
        var connector = new RecordingDatabaseConnector
        {
            Result = new DatabaseQueryResult(
                Enumerable.Range(1, 60)
                    .Select(index => new Dictionary<string, object?>
                    {
                        ["employee_id"] = $"E{index:0000}",
                        ["api_key"] = "provider-secret-token-value",
                        ["note"] = "password=hidden"
                    })
                    .ToList(),
                ReturnedRowCount: 60,
                IsTruncated: true,
                ElapsedMilliseconds: 9)
        };
        var executor = CreateExecutor(database, [GrantToRole(database, canQuery: true)], connector, audit);

        var result = await executor.ExecuteAsync(
            database.Id,
            "SELECT employee_id FROM employees",
            limit: 100,
            requireSimulationBusiness: false,
            safetySchema: null,
            auditAction: "DataSource.Query",
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Ok);
        result.Value!.Rows.Should().HaveCount(50);
        result.Value.Governance!.WarningCodes.Should()
            .Contain("SANITIZED_PREVIEW")
            .And.Contain("BOUNDED_PREVIEW_APPLIED")
            .And.Contain("SENSITIVE_VALUE_REDACTED");
        result.Value.Rows.SelectMany(row => row.Keys).Should().NotContain(key => key.Contains("api_key", StringComparison.OrdinalIgnoreCase));
        result.Value.Rows.SelectMany(row => row.Values.Select(value => value?.ToString() ?? string.Empty))
            .Should().NotContain(value => value.Contains("provider-secret", StringComparison.OrdinalIgnoreCase) ||
                                          value.Contains("password=hidden", StringComparison.OrdinalIgnoreCase));
        var auditRequest = audit.Requests.Should().ContainSingle().Subject;
        auditRequest.Metadata!.Should().ContainKey("queryHash");
        auditRequest.Metadata.Should().ContainKey("warningCode");
        auditRequest.Summary.Should().NotContain("SELECT employee_id");
        auditRequest.Summary.Should().NotContain("provider-secret");
        auditRequest.Metadata.Values.Should().NotContain(value => value.Contains("SELECT employee_id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TextToSql_ShouldRejectFreeSqlPreviewExecution()
    {
        var database = CreateSimulationDatabase("sim-text-to-sql");
        var connector = new RecordingDatabaseConnector();
        var runtime = CreateTextToSqlRuntime(database, [GrantToRole(database, canQuery: true)], connector, new CapturingAuditLogWriter());

        var result = await runtime.ExecuteAsync(
            new BusinessTextToSqlExecuteRequest(
                DataSourceId: database.Id,
                SqlPreview: "SELECT employee_id FROM employees",
                RequestedLimit: 10));

        result.Status.Should().Be(ResultStatus.Invalid);
        connector.ExecuteCount.Should().Be(0);
    }

    [Fact]
    public async Task TextToSqlDraft_ShouldExposeHashOnlyAndExecuteThroughDraftId()
    {
        var database = CreateSimulationDatabase("sim-text-to-sql-draft");
        var connector = new RecordingDatabaseConnector();
        var runtime = CreateTextToSqlRuntime(database, [GrantToRole(database, canQuery: true)], connector, new CapturingAuditLogWriter());

        var draft = await runtime.GenerateDraftAsync(
            new BusinessTextToSqlDraftRequest(
                database.Id,
                "show employee attendance",
                RequestedLimit: 10));
        var executed = await runtime.ExecuteAsync(
            new BusinessTextToSqlExecuteRequest(DraftId: draft.Value!.DraftId, RequestedLimit: 10));

        draft.Status.Should().Be(ResultStatus.Ok);
        draft.Value!.SqlHash.Should().HaveLength(64);
        draft.Value.SqlPreview.Should().Be("SQL_PREVIEW_REDACTED_USE_DRAFT_ID");
        draft.Value.SqlPreview.ToUpperInvariant().Should().NotContain("SELECT");
        executed.Status.Should().Be(ResultStatus.Ok);
        connector.ExecuteCount.Should().Be(1);
    }

    [Fact]
    public async Task CloudReadOnlySource_ShouldRemainBlockedUntilGovernedSchemaExists()
    {
        var cloud = CreateDatabase(
            "cloud-readonly",
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true);
        var connector = new RecordingDatabaseConnector();
        var executor = CreateExecutor(cloud, [GrantToRole(cloud, canQuery: true)], connector, new CapturingAuditLogWriter());

        var result = await executor.ExecuteAsync(
            cloud.Id,
            "SELECT device_id FROM devices",
            limit: 10,
            requireSimulationBusiness: false,
            safetySchema: null,
            auditAction: "DataSource.Query",
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Select(error => error.ToString() ?? string.Empty)
            .Should().Contain(error => error.Contains("Governed semantic schema", StringComparison.OrdinalIgnoreCase));
        connector.ExecuteCount.Should().Be(0);
    }

    private static BusinessDatabaseReadService CreateReadService(
        IReadOnlyCollection<BusinessDatabase> databases,
        IReadOnlyCollection<DataSourcePermissionGrant> grants)
    {
        return new BusinessDatabaseReadService(
            new InMemoryReadRepository<BusinessDatabase>(databases),
            new BusinessDatabaseAccessService(
                new InMemoryReadRepository<DataSourcePermissionGrant>(grants),
                new TestCurrentUser(UserId, role: "Analyst")));
    }

    private static BusinessReadonlyQueryExecutor CreateExecutor(
        BusinessDatabase database,
        IReadOnlyCollection<DataSourcePermissionGrant> grants,
        IDatabaseConnector connector,
        IAuditLogWriter audit)
    {
        return new BusinessReadonlyQueryExecutor(
            new InMemoryReadRepository<BusinessDatabase>([database]),
            connector,
            new BusinessDatabaseAccessService(
                new InMemoryReadRepository<DataSourcePermissionGrant>(grants),
                new TestCurrentUser(UserId, role: "Analyst")),
            audit);
    }

    private static BusinessTextToSqlRuntime CreateTextToSqlRuntime(
        BusinessDatabase database,
        IReadOnlyCollection<DataSourcePermissionGrant> grants,
        IDatabaseConnector connector,
        IAuditLogWriter audit)
    {
        var access = new BusinessDatabaseAccessService(
            new InMemoryReadRepository<DataSourcePermissionGrant>(grants),
            new TestCurrentUser(UserId, role: "Analyst"));
        var executor = new BusinessReadonlyQueryExecutor(
            new InMemoryReadRepository<BusinessDatabase>([database]),
            connector,
            access,
            audit);
        return new BusinessTextToSqlRuntime(
            new InMemoryReadRepository<BusinessDatabase>([database]),
            executor,
            new BusinessTextToSqlDraftStore(),
            access,
            audit);
    }

    private static BusinessDatabase CreateSimulationDatabase(
        string name,
        bool isEnabled = true,
        bool isSelectableInChat = true,
        bool isSelectableInAgent = true)
    {
        return CreateDatabase(
            name,
            BusinessDataExternalSystemType.SimulationBusiness,
            isEnabled: isEnabled,
            isSelectableInChat: isSelectableInChat,
            isSelectableInAgent: isSelectableInAgent);
    }

    private static BusinessDatabase CreateDatabase(
        string name,
        BusinessDataExternalSystemType externalSystemType,
        bool isEnabled = true,
        bool isReadOnly = true,
        bool readOnlyCredentialVerified = false,
        bool isSelectableInChat = true,
        bool isSelectableInAgent = true)
    {
        return new BusinessDatabase(
            name,
            "governed business data source",
            "Host=localhost;Database=aicopilot_sim_business;Username=readonly;Password=readonly;",
            DbProviderType.PostgreSql,
            isReadOnly,
            externalSystemType,
            readOnlyCredentialVerified,
            isEnabled,
            category: "Simulation",
            tags: ["production"],
            ownerDepartment: "AI Platform",
            businessDomain: "Production",
            sensitivityLevel: "Internal",
            defaultQueryLimit: 50,
            maxQueryLimit: 100,
            isSelectableInChat,
            isSelectableInAgent);
    }

    private static DataSourcePermissionGrant GrantToRole(
        BusinessDatabase database,
        bool canQuery,
        bool canSchemaView = false)
    {
        return new DataSourcePermissionGrant(
            database.Id,
            DataSourcePermissionGrantTargetType.Role,
            "Analyst",
            canQuery,
            canSchemaView);
    }

    private sealed class RecordingDatabaseConnector : IDatabaseConnector
    {
        public int ExecuteCount { get; private set; }

        public DatabaseQueryResult Result { get; set; } = new(
            [new Dictionary<string, object?> { ["employee_id"] = "E0001" }],
            ReturnedRowCount: 1,
            IsTruncated: false,
            ElapsedMilliseconds: 1);

        public IDbConnection GetConnection(BusinessDatabaseConnectionInfo database)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<dynamic>> ExecuteQueryAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult<IEnumerable<dynamic>>(Result.Rows);
        }

        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult(Result);
        }

        public Task<IEnumerable<dynamic>> GetSchemaInfoAsync(
            BusinessDatabaseConnectionInfo database,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<dynamic>>([]);
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
