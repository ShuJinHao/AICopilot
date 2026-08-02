using System.Text.Json;
using AICopilot.AiGatewayService.BusinessQueries;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.UnitTests;

public sealed class BusinessQueryExecutorTests
{
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
    public async Task ExecuteAsync_EligibleTypedFailure_ShouldRunSameSourceFallbackAfterProvider(
        BusinessQueryOutcome outcome)
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

        result.Status.Should().Be(BusinessQueryExecutionStatus.Empty);
        result.Provider.Should().Be("business-text-to-sql:v1");
        result.SourceKey.Should().Be(context.SourceKey);
        calls.Should().Equal("typed", "fallback");
        fallback.BoundContext.Should().NotBeNull();
        fallback.BoundContext!.SessionId.Should().Be(context.SessionId);
        fallback.BoundContext.DataSourceId.Should().Be(TestDataSourceId);
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

    private static readonly Guid TestDataSourceId =
        Guid.Parse("c08e6cff-9f99-4c4d-95ab-d0da25fa43bd");

    private static BusinessQueryExecutor CreateExecutor(
        IBusinessQueryProvider provider,
        IBusinessTextToSqlFallbackRunner fallbackRunner)
    {
        return new BusinessQueryExecutor(
            new UnexpectedPlanner(),
            NullLogger<BusinessQueryExecutor>.Instance,
            new FixedProviderRegistry(provider),
            new FixedProfileRegistry(),
            new RecordingContextStore(),
            new FixedDatabaseReadService(),
            fallbackRunner);
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
        IReadOnlyList<Dictionary<string, object?>>? rows = null)
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
                "safe"));
        }
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
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BusinessDatabaseDescriptor>>([Descriptor]);

        public Task<BusinessDatabaseConnectionInfo?> GetByNameAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BusinessDatabaseConnectionInfo?>(new BusinessDatabaseConnectionInfo(
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
