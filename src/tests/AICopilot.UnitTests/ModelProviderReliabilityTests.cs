using AICopilot.AiGatewayService.Queries.Runtime;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AICopilot.UnitTests;

public sealed class ModelProviderReliabilityTests
{
    [Fact]
    public void FallbackPolicy_ShouldReturnConfiguredFallbacks_ForLowRiskRequests()
    {
        var policy = new DefaultModelFallbackPolicy(Options.Create(new ModelProviderReliabilityOptions
        {
            EnableFallback = true,
            FallbackProviders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary"] = ["secondary", "primary", ""]
            }
        }));

        var fallbacks = policy.GetFallbackProviders(
            CreateRequest("primary"),
            new ModelProviderExecutionContext(
                "primary",
                HasTools: false,
                HasMcpTools: false,
                HasApprovalTools: false,
                HasSideEffectingTools: false,
                HasDataAnalysisSqlToolChain: false));

        fallbacks.Should().Equal("secondary");
    }

    [Fact]
    public void FallbackPolicy_ShouldRejectFallbacks_ForHighRiskToolChains()
    {
        var policy = new DefaultModelFallbackPolicy(Options.Create(new ModelProviderReliabilityOptions
        {
            EnableFallback = true,
            FallbackProviders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["*"] = ["secondary"]
            }
        }));

        var fallbacks = policy.GetFallbackProviders(
            CreateRequest("primary"),
            new ModelProviderExecutionContext(
                "primary",
                HasTools: true,
                HasMcpTools: true,
                HasApprovalTools: false,
                HasSideEffectingTools: false,
                HasDataAnalysisSqlToolChain: false));

        fallbacks.Should().BeEmpty();
    }

    [Fact]
    public void CircuitBreaker_ShouldOpenAfterConfiguredFailures_AndAllowHalfOpenAttemptAfterDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var breaker = new InMemoryModelCircuitBreaker(
            Options.Create(new ModelProviderReliabilityOptions
            {
                CircuitBreakerFailureThreshold = 2,
                CircuitBreakerOpenSeconds = 30
            }),
            () => now);

        breaker.CanAttempt("primary").Should().BeTrue();

        breaker.RecordFailure("primary", new InvalidOperationException("first"));
        breaker.CanAttempt("primary").Should().BeTrue();

        breaker.RecordFailure("primary", new InvalidOperationException("second"));
        breaker.CanAttempt("primary").Should().BeFalse();

        now = now.AddSeconds(31);
        breaker.CanAttempt("primary").Should().BeTrue();

        breaker.RecordSuccess("primary");
        breaker.CanAttempt("primary").Should().BeTrue();
    }

    [Fact]
    public async Task CircuitBreaker_ShouldOpenAfterConcurrentFailures()
    {
        var now = DateTimeOffset.UtcNow;
        const int failureThreshold = 200;
        var breaker = new InMemoryModelCircuitBreaker(
            Options.Create(new ModelProviderReliabilityOptions
            {
                CircuitBreakerFailureThreshold = failureThreshold,
                CircuitBreakerOpenSeconds = 30
            }),
            () => now);

        using var start = new ManualResetEventSlim();
        var failures = Enumerable
            .Range(0, failureThreshold)
            .Select(index => Task.Run(() =>
            {
                start.Wait();
                breaker.RecordFailure("primary", new InvalidOperationException($"failure-{index}"));
            }));

        start.Set();
        await Task.WhenAll(failures);

        breaker.CanAttempt("primary").Should().BeFalse();
    }

    [Fact]
    public void CostBudgetPolicy_ShouldRejectRequestsThatExceedConfiguredMaxOutputTokens()
    {
        var policy = new ConfiguredModelCostBudgetPolicy(Options.Create(new ModelProviderReliabilityOptions
        {
            MaxOutputTokens = 1000
        }));
        var request = CreateRequest("primary", new AiChatOptions { MaxOutputTokens = 2000 });

        var act = () => policy.EnsureWithinBudget(
            request,
            new ModelProviderExecutionContext(
                "primary",
                HasTools: false,
                HasMcpTools: false,
                HasApprovalTools: false,
                HasSideEffectingTools: false,
                HasDataAnalysisSqlToolChain: false));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxOutputTokens 2000*1000*");
    }

    [Fact]
    public void SnapshotReader_ShouldExposeConfiguredReliabilityOptions()
    {
        var reader = new ModelProviderReliabilitySnapshotReader(Options.Create(new ModelProviderReliabilityOptions
        {
            EnableFallback = true,
            CircuitBreakerFailureThreshold = 4,
            CircuitBreakerOpenSeconds = 120,
            MaxOutputTokens = 3000,
            FallbackProviders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary"] = ["secondary", ""],
                ["*"] = ["backup"]
            }
        }));

        var snapshot = reader.GetSnapshot();

        snapshot.FallbackEnabled.Should().BeTrue();
        snapshot.CircuitBreakerFailureThreshold.Should().Be(4);
        snapshot.CircuitBreakerOpenSeconds.Should().Be(120);
        snapshot.MaxOutputTokens.Should().Be(3000);
        snapshot.FallbackProviders.Should().Contain(route =>
            route.Provider == "primary"
            && route.FallbackProviders.SequenceEqual(new[] { "secondary" }));
        snapshot.FallbackAllowedScopes.Should().Contain("GeneralChat");
        snapshot.FallbackBlockedScopes.Should().Contain("McpToolCall");
        snapshot.FallbackBlockedScopes.Should().Contain("ApprovalResume");
        snapshot.FallbackBlockedScopes.Should().Contain("DataAnalysisSqlToolChain");
    }

    [Fact]
    public void SnapshotReader_ShouldNormalizeInvalidNumericReliabilityOptions()
    {
        var reader = new ModelProviderReliabilitySnapshotReader(Options.Create(new ModelProviderReliabilityOptions
        {
            CircuitBreakerFailureThreshold = -1,
            CircuitBreakerOpenSeconds = -30,
            MaxOutputTokens = -100
        }));

        var snapshot = reader.GetSnapshot();

        snapshot.CircuitBreakerFailureThreshold.Should().Be(1);
        snapshot.CircuitBreakerOpenSeconds.Should().Be(1);
        snapshot.MaxOutputTokens.Should().Be(0);
    }

    [Fact]
    public async Task GetProviderReliabilityQueryHandler_ShouldReturnSnapshotReaderResult()
    {
        var snapshot = new ModelProviderReliabilityDto(
            FallbackEnabled: true,
            FallbackProviders: [new ModelProviderFallbackRouteDto("primary", ["secondary"])],
            CircuitBreakerFailureThreshold: 5,
            CircuitBreakerOpenSeconds: 90,
            MaxOutputTokens: 4096,
            FallbackAllowedScopes: ["GeneralChat"],
            FallbackBlockedScopes: ["McpToolCall"]);
        var handler = new GetProviderReliabilityQueryHandler(
            new StubModelProviderReliabilitySnapshotReader(snapshot));

        var result = await handler.Handle(new GetProviderReliabilityQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(snapshot);
    }

    [Fact]
    public async Task GovernedChatClient_ShouldReserveAndSettleEveryProviderCall()
    {
        var quotaStore = new RecordingQuotaStore();
        var circuitBreaker = new RecordingCircuitBreaker();
        var client = CreateGovernedClient(
            new RecordingChatClient(),
            quotaStore,
            circuitBreaker);

        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "first")]);
        _ = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "second")]);

        quotaStore.Reservations.Should().HaveCount(2);
        quotaStore.Settlements.Should().HaveCount(2);
        quotaStore.Settlements.Should().OnlyContain(settlement =>
            settlement.WasDispatched &&
            settlement.OutcomeKnown &&
            settlement.ActualInputTokens == 11 &&
            settlement.ActualOutputTokens == 7);
        quotaStore.Settlements.Select(settlement => settlement.Lease.ReservationId)
            .Should()
            .OnlyHaveUniqueItems();
        circuitBreaker.SuccessCount.Should().Be(2);
        circuitBreaker.FailureCount.Should().Be(0);
        circuitBreaker.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task GovernedChatClient_ShouldRejectOpenCircuitBeforeQuotaOrProviderDispatch()
    {
        var quotaStore = new RecordingQuotaStore();
        var circuitBreaker = new RecordingCircuitBreaker { AllowAttempt = false };
        var inner = new RecordingChatClient();
        var client = CreateGovernedClient(inner, quotaStore, circuitBreaker);

        var act = () => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "blocked")]);

        await act.Should().ThrowAsync<ModelProviderCircuitOpenException>();
        circuitBreaker.AttemptCount.Should().Be(1);
        quotaStore.Reservations.Should().BeEmpty();
        quotaStore.Settlements.Should().BeEmpty();
        inner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GovernedChatClient_ShouldConservativelySettleAbandonedStream()
    {
        var quotaStore = new RecordingQuotaStore();
        var circuitBreaker = new RecordingCircuitBreaker();
        var client = CreateGovernedClient(
            new RecordingChatClient(),
            quotaStore,
            circuitBreaker);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "stream")]))
        {
            break;
        }

        quotaStore.Reservations.Should().ContainSingle();
        var settlement = quotaStore.Settlements.Should().ContainSingle().Subject;
        settlement.WasDispatched.Should().BeTrue();
        settlement.OutcomeKnown.Should().BeFalse();
        settlement.FailureCode.Should().Be("model_call_outcome_unknown");
        circuitBreaker.SuccessCount.Should().Be(0);
        circuitBreaker.FailureCount.Should().Be(0);
    }

    [Fact]
    public void GetProviderReliabilityQuery_ShouldKeepPermissionRequirement()
    {
        var attribute = typeof(GetProviderReliabilityQuery)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .Should()
            .ContainSingle()
            .Subject;

        attribute.Permission.Should().Be("AiGateway.GetProviderReliability");
    }

    private static AgentRuntimeCreateRequest CreateRequest(
        string provider,
        AiChatOptions? options = null)
    {
        var model = new LanguageModel(
            provider,
            "test-model",
            "http://localhost/v1",
            "test-key",
            new ModelParameters { MaxTokens = 4096, Temperature = 0.2f });
        var template = new ConversationTemplate(
            "test-template",
            "test",
            "system prompt",
            model.Id,
            new TemplateSpecification());

        return new AgentRuntimeCreateRequest(model, template, options ?? new AiChatOptions());
    }

    private static ModelCallGovernanceChatClient CreateGovernedClient(
        IChatClient inner,
        IModelQuotaReservationStore quotaStore,
        IModelCircuitBreaker circuitBreaker)
    {
        const string poolName = "AnswerPool";
        const string endpointId = "endpoint-1";
        var request = CreateRequest(
            "primary",
            new AiChatOptions { MaxOutputTokens = 32 });
        var reliability = new ModelProviderReliabilityOptions
        {
            EndpointPools = new Dictionary<string, ModelEndpointPoolOptions>(
                StringComparer.OrdinalIgnoreCase)
            {
                [poolName] = new()
                {
                    Endpoints =
                    [
                        new ModelEndpointOptions
                        {
                            EndpointId = endpointId,
                            Provider = "primary",
                            TimeoutMs = 1_000
                        }
                    ]
                }
            }
        };
        return new ModelCallGovernanceChatClient(
            inner,
            quotaStore,
            request,
            new ModelEndpointSelection(
                poolName,
                endpointId,
                "primary",
                "http://localhost/v1",
                HasApiKey: true,
                ApiKey: null),
            poolName,
            reliability,
            circuitBreaker,
            endpointPoolScheduler: null);
    }

    private sealed class StubModelProviderReliabilitySnapshotReader(ModelProviderReliabilityDto snapshot)
        : IModelProviderReliabilitySnapshotReader
    {
        public ModelProviderReliabilityDto GetSnapshot()
        {
            return snapshot;
        }
    }

    private sealed class RecordingQuotaStore : IModelQuotaReservationStore
    {
        public List<ModelQuotaReservationRequest> Reservations { get; } = [];

        public List<ModelQuotaSettlement> Settlements { get; } = [];

        public Task<ModelQuotaReservationOutcome> TryReserveAsync(
            ModelQuotaReservationRequest request,
            CancellationToken cancellationToken = default)
        {
            Reservations.Add(request);
            var lease = new ModelQuotaReservationLease(
                ModelQuotaReservationId.New(),
                Reservations.Count,
                request.CorrelationHash,
                request.EndpointId,
                DateTimeOffset.UtcNow.AddMinutes(1));
            return Task.FromResult(new ModelQuotaReservationOutcome(
                ModelQuotaReservationResult.Granted,
                lease,
                RetryAtUtc: null,
                SafeReason: "granted"));
        }

        public Task<ModelQuotaReservationResult> SettleAsync(
            ModelQuotaSettlement settlement,
            CancellationToken cancellationToken = default)
        {
            Settlements.Add(settlement);
            return Task.FromResult(ModelQuotaReservationResult.Granted);
        }

        public Task<int> ReclaimExpiredAsync(
            DateTimeOffset nowUtc,
            int maxItems,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class RecordingCircuitBreaker : IModelCircuitBreaker
    {
        public bool AllowAttempt { get; init; } = true;

        public int AttemptCount { get; private set; }

        public int SuccessCount { get; private set; }

        public int FailureCount { get; private set; }

        public bool CanAttempt(string providerName)
        {
            AttemptCount++;
            return AllowAttempt;
        }

        public void RecordSuccess(string providerName) => SuccessCount++;

        public void RecordFailure(string providerName, Exception exception) =>
            FailureCount++;
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 11,
                    OutputTokenCount = 7,
                    TotalTokenCount = 18
                }
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new TextContent("first")]);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new TextContent("second"),
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = 11,
                        OutputTokenCount = 7,
                        TotalTokenCount = 18
                    })
                ]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
    }
}
