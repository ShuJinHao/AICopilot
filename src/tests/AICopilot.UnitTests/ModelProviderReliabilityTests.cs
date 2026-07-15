using AICopilot.AiGatewayService.Queries.Runtime;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Ai;
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

    private sealed class StubModelProviderReliabilitySnapshotReader(ModelProviderReliabilityDto snapshot)
        : IModelProviderReliabilitySnapshotReader
    {
        public ModelProviderReliabilityDto GetSnapshot()
        {
            return snapshot;
        }
    }
}
