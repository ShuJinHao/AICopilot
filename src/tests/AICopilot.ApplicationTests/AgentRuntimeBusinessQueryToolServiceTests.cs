using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;

namespace AICopilot.ApplicationTests;

public sealed class AgentRuntimeBusinessQueryToolServiceTests
{
    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldUseCloudReadOnlyFallback_WhenPlanSelectsCloudSource()
    {
        var database = CreateCloudReadOnlyDatabase();
        var readService = new RecordingBusinessDatabaseReadService(database);
        var generator = new FixedTextToSqlGenerator(
            "SELECT d.client_code FROM public.devices d LIMIT 10");
        var provider = new StubBusinessQueryProvider(
            BusinessQueryOutcome.Unsupported,
            "The structured provider does not support this query shape.");
        var runner = new BusinessTextToSqlFallbackRunner(
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
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            new FixedProfileRegistry());
        var service = new AgentRuntimeBusinessQueryToolService(
            readService,
            businessTextToSqlRuntime: null,
            runner,
            new FixedProfileRegistry(),
            new BusinessQueryProviderRegistry([provider], new FixedProfileRegistry()),
            new RecordingBusinessQueryContextStore());
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
        state.CloudReadonlySourcePath.Should().Be("BusinessDataSourceCenter/GovernedTextToSql");
        state.BusinessQueryResults.Should().ContainSingle()
            .Which.SourceMode.Should().Be(DataSourceExternalSystemType.CloudReadOnly.ToString());
        state.CloudReadonlyRows.Should().ContainSingle()
            .Which["client_code"].Should().Be("DEV-001");

        var json = JsonSerializer.Serialize(output, JsonSerializerOptions.Web);
        using var returnedSummary = JsonDocument.Parse(json);
        returnedSummary.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(
                "status",
                "resultType",
                "sourceMode",
                "isSimulation",
                "rowCount",
                "isTruncated",
                "resultHash");
        json.Should().Contain("CloudReadOnly");
        json.Should().NotContain("DEV-001");
        json.Should().NotContain("client_code");
        json.Should().NotContain("SELECT");
    }

    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldNotFallback_WhenConfirmedPlanDidNotSelectTextToSql()
    {
        var database = CreateCloudReadOnlyDatabase();
        var readService = new RecordingBusinessDatabaseReadService(database);
        var generator = new FixedTextToSqlGenerator(
            "SELECT d.client_code FROM public.devices d LIMIT 10");
        var runner = new BusinessTextToSqlFallbackRunner(
            generator,
            new RecordingConnector(new DatabaseQueryResult(
                [],
                ReturnedRowCount: 0,
                IsTruncated: false,
                ElapsedMilliseconds: 1)),
            new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
            new FixedProfileRegistry());
        var service = new AgentRuntimeBusinessQueryToolService(
            readService,
            businessTextToSqlRuntime: null,
            runner,
            new FixedProfileRegistry(),
            new BusinessQueryProviderRegistry([], new FixedProfileRegistry()),
            new RecordingBusinessQueryContextStore());

        var action = () => service.QueryBusinessDatabaseReadonlyP1Async(
            CreatePlan(database.Id, queryMode: "PluginOnly"),
            new AgentTaskRunState(),
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*No governed business query provider is registered*");
        generator.Requests.Should().BeEmpty(
            "an unregistered provider must fail closed before any fallback decision");
    }

    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldRequestClarification_WhenTypedIntentIsMissing()
    {
        var database = CreateCloudReadOnlyDatabase();
        var generator = new FixedTextToSqlGenerator(
            "SELECT d.client_code FROM public.devices d LIMIT 10");
        var contextStore = new RecordingBusinessQueryContextStore();
        var service = CreateService(
            database,
            generator,
            new BusinessQueryProviderRegistry([], new FixedProfileRegistry()),
            contextStore);

        var output = await service.QueryBusinessDatabaseReadonlyP1Async(
            CreatePlan(database.Id, cloudReadonlyIntents: []),
            new AgentTaskRunState(),
            CancellationToken.None);

        contextStore.Remembered.Should().BeEmpty();
        generator.Requests.Should().BeEmpty();
        var json = JsonSerializer.Serialize(output, JsonSerializerOptions.Web);
        json.Should().Contain("\"status\":\"needs-clarification\"");
        json.Should().Contain("\"outcome\":\"NeedClarification\"");
        json.Should().Contain("\"missingFields\":[\"capability\",\"businessObject\",\"timeRange\",\"filters\"]");
        ValidateRuntimeOutput(output).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldRequestClarification_WhenSemanticConfidenceIsLow()
    {
        var database = CreateCloudReadOnlyDatabase();
        var generator = new FixedTextToSqlGenerator(
            "SELECT d.client_code FROM public.devices d LIMIT 10");
        var provider = new StubBusinessQueryProvider(BusinessQueryOutcome.Success, "unused");
        var contextStore = new RecordingBusinessQueryContextStore();
        var service = CreateService(
            database,
            generator,
            new BusinessQueryProviderRegistry([provider], new FixedProfileRegistry()),
            contextStore);

        var output = await service.QueryBusinessDatabaseReadonlyP1Async(
            CreatePlan(
                database.Id,
                cloudReadonlyIntents: [CreateDeviceLogIntent(confidence: 0.42)]),
            new AgentTaskRunState(),
            CancellationToken.None);

        provider.Contexts.Should().BeEmpty();
        contextStore.Remembered.Should().BeEmpty();
        generator.Requests.Should().BeEmpty();
        var json = JsonSerializer.Serialize(output, JsonSerializerOptions.Web);
        json.Should().Contain("\"status\":\"needs-clarification\"");
        json.Should().Contain("\"outcome\":\"NeedClarification\"");
        json.Should().Contain("\"missingFields\":[\"capability\",\"businessObject\",\"timeRange\",\"filters\"]");
    }

    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldReturnStructuredClarification_WhenProviderRequestsIt()
    {
        var database = CreateCloudReadOnlyDatabase();
        var generator = new FixedTextToSqlGenerator(
            "SELECT d.client_code FROM public.devices d LIMIT 10");
        var provider = new StubBusinessQueryProvider(
            BusinessQueryOutcome.NeedClarification,
            "Please provide a device and a time range.");
        var contextStore = new RecordingBusinessQueryContextStore();
        var service = CreateService(
            database,
            generator,
            new BusinessQueryProviderRegistry([provider], new FixedProfileRegistry()),
            contextStore);

        var output = await service.QueryBusinessDatabaseReadonlyP1Async(
            CreatePlan(
                database.Id,
                cloudReadonlyIntents: [CreateDeviceIntent()]),
            new AgentTaskRunState(),
            CancellationToken.None);

        provider.Contexts.Should().ContainSingle();
        contextStore.Remembered.Should().BeEmpty();
        generator.Requests.Should().BeEmpty();
        var json = JsonSerializer.Serialize(output, JsonSerializerOptions.Web);
        json.Should().Contain("\"status\":\"needs-clarification\"");
        json.Should().Contain("\"outcome\":\"NeedClarification\"");
        json.Should().Contain("Please provide a device and a time range.");
        ValidateRuntimeOutput(output).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldTerminateWithoutFallback_WhenProviderIsUnauthorized()
    {
        var database = CreateCloudReadOnlyDatabase();
        var generator = new FixedTextToSqlGenerator(
            "SELECT d.client_code FROM public.devices d LIMIT 10");
        var provider = new StubBusinessQueryProvider(
            BusinessQueryOutcome.Unauthorized,
            "The confirmed source denied this query.");
        var service = CreateService(
            database,
            generator,
            new BusinessQueryProviderRegistry([provider], new FixedProfileRegistry()),
            new RecordingBusinessQueryContextStore());

        var action = () => service.QueryBusinessDatabaseReadonlyP1Async(
            CreatePlan(database.Id),
            new AgentTaskRunState(),
            CancellationToken.None);

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        generator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryBusinessDatabaseReadonlyP1Async_ShouldUseStructuredProviderWithoutDatabaseLookupOrFallbackRuntime()
    {
        var database = CreateCloudReadOnlyDatabase();
        var readService = new RecordingBusinessDatabaseReadService(database);
        var provider = new StubBusinessQueryProvider(
            BusinessQueryOutcome.Success,
            "Structured provider completed.");
        var service = new AgentRuntimeBusinessQueryToolService(
            readService,
            businessTextToSqlRuntime: null,
            businessTextToSqlFallbackRunner: null,
            profileRegistry: new FixedProfileRegistry(),
            providerRegistry: new BusinessQueryProviderRegistry([provider], new FixedProfileRegistry()),
            queryContextStore: new RecordingBusinessQueryContextStore());
        var state = new AgentTaskRunState();

        var output = await service.QueryBusinessDatabaseReadonlyP1Async(
            CreatePlan(
                database.Id,
                queryMode: "PluginOnly",
                businessDomains: [BusinessDataCapability.Device.ToString()]),
            state,
            CancellationToken.None);

        provider.Contexts.Should().ContainSingle()
            .Which.Capability.Should().Be(BusinessDataCapability.Device);
        readService.GetByNameCallCount.Should().Be(0,
            "a successful structured plugin must not depend on database credentials or a Text-to-SQL runtime");
        state.CloudReadonlySourcePath.Should().Be("stub-cloud-provider");
        state.CloudReadonlyRowCount.Should().Be(1);
        JsonSerializer.Serialize(output, JsonSerializerOptions.Web)
            .Should().Contain("\"outcome\":\"Success\"");
    }

    private static AgentRuntimeBusinessQueryToolService CreateService(
        BusinessDatabaseConnectionInfo database,
        FixedTextToSqlGenerator generator,
        IBusinessQueryProviderRegistry providerRegistry,
        IBusinessQueryContextStore contextStore)
    {
        return new AgentRuntimeBusinessQueryToolService(
            new RecordingBusinessDatabaseReadService(database),
            businessTextToSqlRuntime: null,
            new BusinessTextToSqlFallbackRunner(
                generator,
                new RecordingConnector(new DatabaseQueryResult([], 0, false, 1)),
                new DataAnalysisAuditRecorder(new NoopAuditLogWriter()),
                new FixedProfileRegistry()),
            profileRegistry: new FixedProfileRegistry(),
            providerRegistry: providerRegistry,
            queryContextStore: contextStore);
    }

    private static ToolOutputValidationResult ValidateRuntimeOutput(object output)
    {
        var seed = BuiltInToolRegistrations.FindAgentRuntimeTool(
            "query_business_database_readonly")!;
        var registration = new ToolRegistration(
            seed.ToolCode,
            seed.DisplayName,
            seed.Description,
            seed.ProviderType,
            seed.TargetType,
            seed.TargetName,
            seed.InputSchemaJson,
            seed.OutputSchemaJson,
            seed.RiskLevel,
            seed.RequiredPermission,
            seed.RequiresApproval,
            seed.IsEnabled,
            seed.TimeoutSeconds,
            seed.AuditLevel,
            DateTimeOffset.UtcNow,
            seed.Category,
            seed.BusinessDomains,
            seed.DataBoundary,
            seed.IsVisibleToPlanner,
            seed.IsExecutableByAgent,
            seed.SchemaVersion,
            seed.CatalogVersion,
            seed.ApprovalPolicy);
        return AgentToolRuntimeOutputGate.Validate(
            registration,
            AgentToolExecutionResult.From(output));
    }

    private static AgentTaskPlanDocument CreatePlan(
        Guid dataSourceId,
        string queryMode = "TextToSql",
        IReadOnlyCollection<AgentTaskPlanCloudReadonlyIntentDocument>? cloudReadonlyIntents = null,
        IReadOnlyCollection<string>? businessDomains = null)
    {
        return new AgentTaskPlanDocument(
            1,
            "agent_planner",
            "查看设备列表",
            AgentTaskType.DataAnalysis.ToString(),
            AgentTaskRiskLevel.Low.ToString(),
            [],
            [],
            cloudReadonlyIntents ?? [CreateDeviceIntent()],
            [],
            new AgentTaskPlanRuntimeSettingsDocument(30, 12000),
            DataSourceIds: [dataSourceId],
            BusinessDomains: businessDomains,
            QueryMode: queryMode);
    }

    private static AgentTaskPlanCloudReadonlyIntentDocument CreateDeviceIntent()
    {
        return new AgentTaskPlanCloudReadonlyIntentDocument(
            "cloud-readonly-semantic-plan:v1",
            "Analysis.Device.List",
            string.Empty,
            0.92,
            SemanticQueryTarget.Device,
            SemanticQueryKind.List,
            ["client_code", "device_name"],
            [],
            TimeRange: null,
            Sort: null,
            Limit: 20,
            QueryScope: []);
    }

    private static AgentTaskPlanCloudReadonlyIntentDocument CreateDeviceLogIntent(double confidence)
    {
        return new AgentTaskPlanCloudReadonlyIntentDocument(
            "cloud-readonly-semantic-plan:v1",
            "Analysis.DeviceLog.Range",
            string.Empty,
            confidence,
            SemanticQueryTarget.DeviceLog,
            SemanticQueryKind.Range,
            ["client_code", "level", "log_time"],
            [],
            TimeRange: null,
            Sort: null,
            Limit: 20,
            QueryScope: []);
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

        public int GetByNameCallCount { get; private set; }

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
            GetByNameCallCount++;
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

    private sealed class FixedTextToSqlGenerator(string sql) : IBusinessTextToSqlGenerator
    {
        public List<BusinessTextToSqlGenerationRequest> Requests { get; } = [];

        public Task<BusinessTextToSqlGenerationResult> GenerateAsync(
            BusinessTextToSqlGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(BusinessTextToSqlGenerationResult.Success(sql, "fixed sql"));
        }
    }

    private sealed class StubBusinessQueryProvider(
        BusinessQueryOutcome outcome,
        string safeMessage)
        : IBusinessQueryProvider
    {
        public string ProviderCode => "stub-cloud-provider";

        public string SourceKey => StandardBusinessDataSourceProfiles.CloudReadOnly.Code;

        public DataSourceExternalSystemType SourceType => DataSourceExternalSystemType.CloudReadOnly;

        public IReadOnlySet<BusinessDataCapability> Capabilities { get; } =
            Enum.GetValues<BusinessDataCapability>().ToHashSet();

        public IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract> ResultContracts { get; } =
            Enum.GetValues<BusinessDataCapability>().ToDictionary(
                capability => capability,
                _ => new BusinessQueryResultContract(
                    new HashSet<string>(["deviceCode"], StringComparer.OrdinalIgnoreCase),
                    StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity
                        .BlockedIdentifierFragments));

        public List<BusinessQueryContext> Contexts { get; } = [];

        public Task<BusinessQueryProviderResult> QueryAsync(
            BusinessQueryContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            if (outcome == BusinessQueryOutcome.Success)
            {
                return Task.FromResult(new BusinessQueryProviderResult(
                    outcome,
                    ProviderCode,
                    context.SourceKey,
                    context.DataSourceId,
                    context.SourceType,
                    context.Capability,
                    [new Dictionary<string, object?> { ["deviceCode"] = "DEV-001" }],
                    RowCount: 1,
                    IsTruncated: false,
                    SourcePath: ProviderCode,
                    SourceLabel: "Structured business provider",
                    QueriedAtUtc: DateTimeOffset.UtcNow,
                    SafeMessage: safeMessage));
            }

            return Task.FromResult(BusinessQueryProviderResult.FromOutcome(
                context,
                ProviderCode,
                outcome,
                safeMessage));
        }
    }

    private sealed class RecordingBusinessQueryContextStore : IBusinessQueryContextStore
    {
        public List<BusinessQueryContext> Remembered { get; } = [];

        public BusinessQueryContext Resolve(BusinessQueryContext requested)
        {
            return requested;
        }

        public void Remember(BusinessQueryContext context)
        {
            Remembered.Add(context);
        }

        public BusinessQueryConfirmationChallenge BeginConfirmation(BusinessQueryContext requested) =>
            throw new NotSupportedException();

        public bool TryConfirmPending(Guid taskId, string userMessage, out BusinessQueryContext confirmed)
        {
            confirmed = null!;
            return false;
        }

    }

    private sealed class FixedProfileRegistry : IBusinessDataSourceProfileRegistry
    {
        public IReadOnlyCollection<BusinessDataSourceProfile> GetAll() =>
            [StandardBusinessDataSourceProfiles.CloudReadOnly];

        public bool TryGet(
            string sourceKey,
            DataSourceExternalSystemType expectedSourceType,
            out BusinessDataSourceProfile profile)
        {
            profile = StandardBusinessDataSourceProfiles.CloudReadOnly;
            return expectedSourceType == profile.SourceType &&
                   string.Equals(sourceKey, profile.Code, StringComparison.OrdinalIgnoreCase);
        }

        public BusinessDataSourceProfile GetRequired(
            string sourceKey,
            DataSourceExternalSystemType expectedSourceType)
        {
            return TryGet(sourceKey, expectedSourceType, out var profile)
                ? profile
                : throw new InvalidOperationException("Profile not registered.");
        }
    }

    private sealed class RecordingConnector(DatabaseQueryResult result) : IDatabaseConnector
    {
        public Task<DatabaseQueryResult> ExecuteQueryWithMetadataAsync(
            BusinessDatabaseConnectionInfo database,
            string sql,
            BusinessQuerySecurityProfile securityProfile,
            object? parameters = null,
            DatabaseQueryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
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
