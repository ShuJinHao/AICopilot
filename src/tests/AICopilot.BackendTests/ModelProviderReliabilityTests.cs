using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

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
}
