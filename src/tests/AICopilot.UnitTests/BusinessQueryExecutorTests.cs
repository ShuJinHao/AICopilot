using System.Text.Json;
using AICopilot.AiGatewayService.BusinessQueries;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.UnitTests;

public sealed class BusinessQueryExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CapacityWithOnlyPlcCode_ShouldNeedClarificationBeforeTypedProvider()
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.Device);
        var provider = new RecordingProvider(
            context,
            BusinessQueryOutcome.Success,
            calls);
        var definitions = new SemanticDefinitionCatalog();
        var executor = CreateExecutor(
            provider,
            new RecordingFallbackRunner(calls),
            new SemanticQueryPlanner(
                new SemanticQuerySchemaRegistry(definitions),
                definitions));

        var result = await executor.ExecuteAsync(
            Guid.NewGuid(),
            "Analysis.Capacity.ByDevice",
            """{"filters":[{"field":"plcCode","operator":"eq","value":"P2-CP05"}]}""",
            confirmedQuery: null,
            CancellationToken.None);

        result.Status.Should().Be(BusinessQueryExecutionStatus.NeedsConfirmation);
        result.FailureCode.Should().Be(CloudAiReadProblemCodes.MissingRequiredParameter);
        result.SafeMessage.Should().Contain("设备身份");
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_TypedSuccess_ShouldSkipFallbackAndReturnTrustedCanonicalWidgets()
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.DeviceLog);
        var provider = new RecordingProvider(
            context,
            BusinessQueryOutcome.Success,
            calls,
            [
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["logId"] = "log-1",
                    ["deviceId"] = "device-1",
                    ["deviceName"] = "Cutter A",
                    ["level"] = "ERROR",
                    ["message"] = "Motor overload",
                    ["occurredAt"] = "2026-08-02T01:00:00Z"
                }
            ]);
        var fallback = new RecordingFallbackRunner(calls);
        var executor = CreateExecutor(provider, fallback);

        var result = await executor.ExecuteAsync(
            context.SessionId,
            context.SemanticPlan!.Intent,
            context.Question,
            context,
            CancellationToken.None);

        result.Status.Should().Be(BusinessQueryExecutionStatus.Succeeded);
        result.Provider.Should().Be(provider.ProviderCode);
        calls.Should().Equal("typed");
        result.Widgets.Should().NotBeEmpty();
        using var widget = JsonDocument.Parse(result.Widgets[0]);
        widget.RootElement.TryGetProperty("id", out _).Should().BeTrue();
        widget.RootElement.TryGetProperty("type", out _).Should().BeTrue();
        widget.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        widget.RootElement.TryGetProperty("visual_decision", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(BusinessQueryOutcome.Unsupported)]
    [InlineData(BusinessQueryOutcome.Unavailable)]
    public async Task ExecuteAsync_RealCloudTypedFailure_ShouldFailBeforeFallbackOrConnector(
        BusinessQueryOutcome outcome)
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.Device);
        var provider = new RecordingProvider(context, outcome, calls);
        var fallback = new RecordingFallbackRunner(calls);
        var database = new FixedDatabaseReadService();
        var executor = CreateExecutor(
            provider,
            fallback,
            databaseReadService: database);

        var result = await executor.ExecuteAsync(
            context.SessionId,
            context.SemanticPlan!.Intent,
            context.Question,
            context,
            CancellationToken.None);

        result.Status.Should().Be(BusinessQueryExecutionStatus.Failed);
        result.FailureCode.Should().Be(AppProblemCodes.CloudReadonlyIntentUnsupported);
        result.SafeMessage.Should().Contain("real_cloud_text_to_sql_temporarily_closed");
        calls.Should().Equal("typed");
        fallback.BoundContext.Should().BeNull();
        database.ListSelectableCalls.Should().Be(0);
        database.GetByNameCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(BusinessQueryOutcome.Empty, BusinessQueryExecutionStatus.Empty)]
    [InlineData(BusinessQueryOutcome.NeedClarification, BusinessQueryExecutionStatus.NeedsConfirmation)]
    [InlineData(BusinessQueryOutcome.Unauthorized, BusinessQueryExecutionStatus.Failed)]
    public async Task ExecuteAsync_TerminalTypedOutcome_ShouldNeverRunFallback(
        BusinessQueryOutcome outcome,
        BusinessQueryExecutionStatus expectedStatus)
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.Device);
        var provider = new RecordingProvider(context, outcome, calls);
        var fallback = new RecordingFallbackRunner(calls);
        var executor = CreateExecutor(provider, fallback);

        var result = await executor.ExecuteAsync(
            context.SessionId,
            context.SemanticPlan!.Intent,
            context.Question,
            context,
            CancellationToken.None);

        result.Status.Should().Be(expectedStatus);
        calls.Should().Equal("typed");
        fallback.BoundContext.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ConfirmedContextFromAnotherSession_ShouldFailBeforeAnyProviderCall()
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.Device);
        var provider = new RecordingProvider(
            context,
            BusinessQueryOutcome.Unsupported,
            calls);
        var fallback = new RecordingFallbackRunner(calls);
        var executor = CreateExecutor(provider, fallback);

        var result = await executor.ExecuteAsync(
            Guid.NewGuid(),
            context.SemanticPlan!.Intent,
            context.Question,
            context,
            CancellationToken.None);

        result.Status.Should().Be(BusinessQueryExecutionStatus.Failed);
        result.SafeMessage.Should().Contain("当前会话");
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ProductionScopeDelegationFailure_ShouldPreserveReloginCode()
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.ProductionRecord);
        var provider = new RecordingProvider(context, BusinessQueryOutcome.Success, calls);
        var definitions = new SemanticDefinitionCatalog();
        var executor = CreateExecutor(
            provider,
            new RecordingFallbackRunner(calls),
            new SemanticQueryPlanner(
                new SemanticQuerySchemaRegistry(definitions),
                definitions),
            cloudAiReadClient: new FailingCloudAiReadClient(
                sealFailureCode: CloudAiReadProblemCodes.DelegationRequired));

        var result = await executor.ExecuteAsync(
            Guid.NewGuid(),
            "Analysis.ProductionData.ByDevice",
            """{"filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"},{"field":"preset","operator":"eq","value":"today"}]}""",
            confirmedQuery: null,
            CancellationToken.None);

        result.Status.Should().Be(BusinessQueryExecutionStatus.Failed);
        result.FailureCode.Should().Be(CloudAiReadProblemCodes.DelegationRequired);
        result.SafeMessage.Should().Contain("重新通过 Cloud 登录");
        calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CloudAiReadProblemCodes.Unauthorized, "重新通过 Cloud 登录")]
    [InlineData(CloudAiReadProblemCodes.Forbidden, "权限或设备范围不足")]
    [InlineData(CloudAiReadProblemCodes.RequestBlocked, "权限或设备范围不足")]
    public async Task ExecuteAsync_ProductionScopeAuthorizationFailure_ShouldPreservePreciseCode(
        string failureCode,
        string expectedMessage)
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.ProductionRecord);
        var provider = new RecordingProvider(context, BusinessQueryOutcome.Success, calls);
        var definitions = new SemanticDefinitionCatalog();
        var executor = CreateExecutor(
            provider,
            new RecordingFallbackRunner(calls),
            new SemanticQueryPlanner(
                new SemanticQuerySchemaRegistry(definitions),
                definitions),
            cloudAiReadClient: new FailingCloudAiReadClient(sealFailureCode: failureCode));

        var result = await executor.ExecuteAsync(
            Guid.NewGuid(),
            "Analysis.ProductionData.ByDevice",
            """{"filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"},{"field":"preset","operator":"eq","value":"today"}]}""",
            confirmedQuery: null,
            CancellationToken.None);

        result.Status.Should().Be(BusinessQueryExecutionStatus.Failed);
        result.FailureCode.Should().Be(failureCode);
        result.SafeMessage.Should().Contain(expectedMessage);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ProviderDelegationFailure_ShouldPreserveReloginCodeWithoutFallback()
    {
        var calls = new List<string>();
        var context = CreateConfirmedContext(BusinessDataCapability.Device);
        var provider = new CloudAiReadBusinessQueryProvider(
            new FailingCloudAiReadClient(
                queryFailureCode: CloudAiReadProblemCodes.DelegationRequired));
        var fallback = new RecordingFallbackRunner(calls);
        var executor = CreateExecutor(provider, fallback);

        var result = await executor.ExecuteAsync(
            context.SessionId,
            context.SemanticPlan!.Intent,
            context.Question,
            context,
            CancellationToken.None);

        result.Status.Should().Be(BusinessQueryExecutionStatus.Failed);
        result.FailureCode.Should().Be(CloudAiReadProblemCodes.DelegationRequired);
        result.SafeMessage.Should().Contain("重新通过 Cloud 登录");
        calls.Should().BeEmpty();
        fallback.BoundContext.Should().BeNull();
    }

    private static readonly Guid TestDataSourceId =
        Guid.Parse("c08e6cff-9f99-4c4d-95ab-d0da25fa43bd");

    private static BusinessQueryExecutor CreateExecutor(
        IBusinessQueryProvider provider,
        IBusinessTextToSqlFallbackRunner fallbackRunner,
        ISemanticQueryPlanner? planner = null,
        IBusinessDatabaseReadService? databaseReadService = null,
        ICloudAiReadClient? cloudAiReadClient = null)
    {
        return new BusinessQueryExecutor(
            planner ?? new UnexpectedPlanner(),
            NullLogger<BusinessQueryExecutor>.Instance,
            new FixedProviderRegistry(provider),
            new FixedProfileRegistry(),
            new RecordingContextStore(),
            databaseReadService ?? new FixedDatabaseReadService(),
            fallbackRunner,
            cloudAiReadClient);
    }

    private static BusinessQueryContext CreateConfirmedContext(
        BusinessDataCapability capability)
    {
        var target = capability switch
        {
            BusinessDataCapability.DeviceLog => SemanticQueryTarget.DeviceLog,
            _ => SemanticQueryTarget.Device
        };
        var plan = new SemanticQueryPlan(
            $"Analysis.{target}.List",
            target,
            SemanticQueryKind.List,
            "test query",
            new SemanticProjection(target == SemanticQueryTarget.DeviceLog
                ? ["deviceName", "level", "message", "occurredAt"]
                : ["deviceCode", "deviceName"]),
            [],
            null,
            null,
            20);
        return new BusinessQueryContext(
                Guid.NewGuid(),
                StandardBusinessDataSourceProfiles.CloudReadOnly.Code,
                TestDataSourceId,
                DataSourceExternalSystemType.CloudReadOnly,
                capability,
                "test query",
                SourceExplicitlySelected: true,
                BusinessQueryConfirmation.Complete,
                plan)
            .Confirm(new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));
    }

    private sealed class RecordingProvider(
        BusinessQueryContext expectedContext,
        BusinessQueryOutcome outcome,
        List<string> calls,
        IReadOnlyList<Dictionary<string, object?>>? rows = null,
        string? failureCode = null)
        : IBusinessQueryProvider
    {
        public string ProviderCode => "typed-provider";

        public string SourceKey => expectedContext.SourceKey;

        public DataSourceExternalSystemType SourceType => expectedContext.SourceType;

        public IReadOnlySet<BusinessDataCapability> Capabilities { get; } =
            new HashSet<BusinessDataCapability> { expectedContext.Capability };

        public IReadOnlyDictionary<BusinessDataCapability, BusinessQueryResultContract>
            ResultContracts { get; } =
            new Dictionary<BusinessDataCapability, BusinessQueryResultContract>
            {
                [expectedContext.Capability] = new(
                    new HashSet<string>(
                    [
                        "logId", "deviceId", "deviceCode", "deviceName", "processId",
                        "level", "message", "occurredAt"
                    ], StringComparer.OrdinalIgnoreCase),
                    StandardBusinessDataSourceProfiles.CloudReadOnly.QuerySecurity
                        .BlockedIdentifierFragments)
            };

        public Task<BusinessQueryProviderResult> QueryAsync(
            BusinessQueryContext context,
            CancellationToken cancellationToken = default)
        {
            calls.Add("typed");
            var resultRows = rows ?? [];
            return Task.FromResult(new BusinessQueryProviderResult(
                outcome,
                ProviderCode,
                context.SourceKey,
                context.DataSourceId,
                context.SourceType,
                context.Capability,
                resultRows,
                resultRows.Count,
                false,
                "/api/ai-read/test",
                "Cloud AiRead",
                new DateTimeOffset(2026, 8, 2, 1, 0, 0, TimeSpan.Zero),
                "safe",
                failureCode));
        }
    }

    private sealed class FailingCloudAiReadClient(
        string? sealFailureCode = null,
        string? queryFailureCode = null) : ICloudAiReadClient
    {
        public bool IsEnabled => true;

        public Task<SemanticQueryPlan> SealProductionScopeAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default) => sealFailureCode is null
            ? Task.FromResult(plan)
            : Task.FromException<SemanticQueryPlan>(new CloudAiReadException(
                sealFailureCode,
                "scope sealing failed"));

        public Task<CloudAiReadResult<object>> QuerySemanticAsync(
            SemanticQueryPlan plan,
            CancellationToken cancellationToken = default) => queryFailureCode is null
            ? Unexpected<object>()
            : Task.FromException<CloudAiReadResult<object>>(new CloudAiReadException(
                queryFailureCode,
                "query failed"));

        public Task<CloudAiReadResult<CloudAiReadDeviceDto>> GetDevicesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadDeviceDto>();

        public Task<CloudAiReadResult<CloudAiReadProcessDto>> GetProcessesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadProcessDto>();

        public Task<CloudAiReadResult<CloudAiReadClientReleaseVersionDto>> GetClientReleasesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadClientReleaseVersionDto>();

        public Task<CloudAiReadResult<CloudAiReadDeviceClientStateDto>> GetDeviceClientStatesAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadDeviceClientStateDto>();

        public Task<CloudAiReadResult<CloudAiReadCapacitySummaryDto>> GetCapacitySummaryAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadCapacitySummaryDto>();

        public Task<CloudAiReadResult<CloudAiReadCapacityHourlyDto>> GetCapacityHourlyAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadCapacityHourlyDto>();

        public Task<CloudAiReadResult<CloudAiReadDeviceLogDto>> GetDeviceLogsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadDeviceLogDto>();

        public Task<CloudAiReadResult<CloudAiReadProductionRecordDto>> GetProductionRecordsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadProductionRecordDto>();

        public Task<CloudAiReadResult<CloudAiReadDevicePlcDto>> GetDevicePlcsAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadDevicePlcDto>();

        public Task<CloudAiReadResult<CloudAiReadDataSchemaDto>> GetDataSchemasAsync(
            CloudAiReadQuery query,
            CancellationToken cancellationToken = default) => Unexpected<CloudAiReadDataSchemaDto>();

        private static Task<CloudAiReadResult<T>> Unexpected<T>() =>
            Task.FromException<CloudAiReadResult<T>>(
                new InvalidOperationException("Unexpected typed Cloud call."));
    }

    private sealed class FixedProviderRegistry(IBusinessQueryProvider provider)
        : IBusinessQueryProviderRegistry
    {
        public IBusinessQueryProvider ResolveRequired(BusinessQueryContext context) => provider;
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
            DataSourceExternalSystemType expectedSourceType) =>
            TryGet(sourceKey, expectedSourceType, out var profile)
                ? profile
                : throw new InvalidOperationException("Profile not registered.");
    }

    private sealed class RecordingContextStore : IBusinessQueryContextStore
    {
        public BusinessQueryContext Resolve(BusinessQueryContext requested) => requested;

        public void Remember(BusinessQueryContext context)
        {
        }

        public BusinessQueryConfirmationChallenge BeginConfirmation(BusinessQueryContext requested) =>
            throw new InvalidOperationException("Confirmation was not expected.");

        public bool TryConfirmPending(
            Guid sessionId,
            string userMessage,
            out BusinessQueryContext confirmed)
        {
            confirmed = null!;
            return false;
        }
    }

    private sealed class FixedDatabaseReadService : IBusinessDatabaseReadService
    {
        public int ListSelectableCalls { get; private set; }

        public int GetByNameCalls { get; private set; }

        private static readonly BusinessDatabaseDescriptor Descriptor = new(
            TestDataSourceId,
            "Cloud readonly",
            "Cloud readonly",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true,
            DataSourceExternalSystemType.CloudReadOnly,
            ReadOnlyCredentialVerified: true);

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListEnabledAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([Descriptor]);

        public Task<IReadOnlyList<BusinessDatabaseDescriptor>> ListSelectableAsync(
            DataSourceSelectionMode selectionMode,
            CancellationToken cancellationToken = default)
        {
            ListSelectableCalls++;
            return Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([Descriptor]);
        }

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            GetByNameCalls++;
            return Task.FromResult<BusinessDatabaseConnectionInfo?>(new BusinessDatabaseConnectionInfo(
                TestDataSourceId,
                Descriptor.Name,
                Descriptor.Description,
                "Host=readonly.invalid",
                DatabaseProviderType.PostgreSql,
                IsEnabled: true,
                IsReadOnly: true,
                DataSourceExternalSystemType.CloudReadOnly,
                ReadOnlyCredentialVerified: true));
        }
    }

    private sealed class RecordingFallbackRunner(List<string> calls)
        : IBusinessTextToSqlFallbackRunner
    {
        public BusinessQueryContext? BoundContext { get; private set; }

        public Task<BusinessTextToSqlFallbackResult> RunAsync(
            BusinessQueryContext context,
            BusinessDatabaseConnectionInfo database,
            string? question,
            int? requestedLimit,
            CancellationToken cancellationToken)
        {
            calls.Add("fallback");
            BoundContext = context;
            return Task.FromResult(new BusinessTextToSqlFallbackResult(
                true,
                "safe fallback context",
                [],
                0,
                false,
                "query-hash",
                [],
                "safe"));
        }
    }

    private sealed class UnexpectedPlanner : ISemanticQueryPlanner
    {
        public SemanticPlanningResult Plan(string intent, string? query) =>
            throw new InvalidOperationException("Confirmed execution must not invoke the planner.");
    }
}
