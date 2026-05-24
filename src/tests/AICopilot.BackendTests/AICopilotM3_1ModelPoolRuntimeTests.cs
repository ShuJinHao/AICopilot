using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "AICopilotM3_1ModelPoolRuntime")]
public sealed class AICopilotM3_1ModelPoolRuntimeTests
{
    [Fact]
    public async Task AcquireEndpoint_ShouldQueueUntilLeaseIsReleased()
    {
        var scheduler = CreateScheduler(new ModelEndpointPoolOptions
        {
            QueueTimeoutMs = 2000,
            Endpoints = [CreateEndpoint("answer-a", concurrencyLimit: 1, queueLimit: 2)]
        });
        var request = CreateRequest();
        var context = CreateContext();

        using var first = scheduler.AcquireEndpoint("AnswerPool", request, context);
        var secondTask = Task.Run(() => scheduler.AcquireEndpoint("AnswerPool", request, context));

        await WaitUntilAsync(() =>
            scheduler.GetSnapshot().Pools.Single().Endpoints.Single().Stats.QueueLength > 0);
        secondTask.IsCompleted.Should().BeFalse();

        first.Dispose();
        using var second = await secondTask;

        second.Selection.EndpointId.Should().Be("answer-a");
        scheduler.GetSnapshot().Pools.Single().Endpoints.Single().Stats.InFlight.Should().Be(1);
    }

    [Fact]
    public void AcquireEndpoint_ShouldFail_WhenQueueIsFull()
    {
        var scheduler = CreateScheduler(new ModelEndpointPoolOptions
        {
            QueueTimeoutMs = 1000,
            Endpoints = [CreateEndpoint("answer-a", concurrencyLimit: 1, queueLimit: 0)]
        });
        var request = CreateRequest();
        var context = CreateContext();

        using var first = scheduler.AcquireEndpoint("AnswerPool", request, context);
        var action = () => scheduler.AcquireEndpoint("AnswerPool", request, context);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*queue is full*");
    }

    [Fact]
    public void AcquireEndpoint_ShouldFail_WhenQueueWaitTimesOut()
    {
        var scheduler = CreateScheduler(new ModelEndpointPoolOptions
        {
            QueueTimeoutMs = 10,
            Endpoints = [CreateEndpoint("answer-a", concurrencyLimit: 1, queueLimit: 1)]
        });
        var request = CreateRequest();
        var context = CreateContext();

        using var first = scheduler.AcquireEndpoint("AnswerPool", request, context);
        var action = () => scheduler.AcquireEndpoint("AnswerPool", request, context);

        action.Should().Throw<TimeoutException>()
            .WithMessage("*queue wait timed out*");
    }

    [Fact]
    public void AcquireEndpoint_ShouldRespectPerModelConcurrencyAcrossEndpoints()
    {
        var scheduler = CreateScheduler(new ModelEndpointPoolOptions
        {
            ModelConcurrencyLimit = 1,
            QueueTimeoutMs = 1000,
            Endpoints =
            [
                CreateEndpoint("answer-a", concurrencyLimit: 2, queueLimit: 0),
                CreateEndpoint("answer-b", concurrencyLimit: 2, queueLimit: 0)
            ]
        });
        var request = CreateRequest(modelName: "same-model");
        var otherModelRequest = CreateRequest(modelName: "other-model");
        var context = CreateContext();

        using var first = scheduler.AcquireEndpoint("AnswerPool", request, context);
        var sameModelAction = () => scheduler.AcquireEndpoint("AnswerPool", request, context);

        sameModelAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*queue is full*");

        using var other = scheduler.AcquireEndpoint("AnswerPool", otherModelRequest, context);
        other.Selection.EndpointId.Should().NotBeEmpty();
    }

    [Fact]
    public void AcquireEndpoint_ShouldRespectEndpointRpmAndEstimatedTpmLimits()
    {
        var scheduler = CreateScheduler(new ModelEndpointPoolOptions
        {
            QueueTimeoutMs = 1000,
            Endpoints = [CreateEndpoint("answer-a", rpmLimit: 1, tpmLimit: 20, queueLimit: 0)]
        });
        var context = CreateContext();

        using (scheduler.AcquireEndpoint("AnswerPool", CreateRequest(maxOutputTokens: 10), context))
        {
        }

        var rpmAction = () => scheduler.AcquireEndpoint("AnswerPool", CreateRequest(maxOutputTokens: 10), context);
        rpmAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*queue is full*");

        var endpoint = scheduler.GetSnapshot().Pools.Single().Endpoints.Single();
        endpoint.Stats.RateLimitCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AcquireEndpoint_ShouldRespectCallerRateLimits()
    {
        var scheduler = CreateScheduler(
            new ModelEndpointPoolOptions
            {
                QueueTimeoutMs = 1000,
                Endpoints = [CreateEndpoint("answer-a", concurrencyLimit: 3, queueLimit: 0)]
            },
            options =>
            {
                options.PerUserRpmLimit = 1;
                options.PerRoleRpmLimit = 2;
                options.PerTenantRpmLimit = 2;
            });
        var context = CreateContext();
        var userA = CreateRequest(caller: new AgentRuntimeCallerContext(Guid.NewGuid(), "user-a", "Operator", "tenant-a"));
        var userB = CreateRequest(caller: new AgentRuntimeCallerContext(Guid.NewGuid(), "user-b", "Operator", "tenant-a"));

        using (scheduler.AcquireEndpoint("AnswerPool", userA, context))
        {
        }

        var sameUserAction = () => scheduler.AcquireEndpoint("AnswerPool", userA, context);
        sameUserAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*queue is full*");

        using var otherUser = scheduler.AcquireEndpoint("AnswerPool", userB, context);
        otherUser.Selection.EndpointId.Should().Be("answer-a");
    }

    [Fact]
    public void AcquireEndpoint_ShouldOpenCircuitAfterFailures_AndRecoverAfterWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var scheduler = CreateScheduler(
            new ModelEndpointPoolOptions
            {
                QueueTimeoutMs = 1000,
                Endpoints = [CreateEndpoint("answer-a", concurrencyLimit: 1, queueLimit: 0)]
            },
            options =>
            {
                options.CircuitBreakerFailureThreshold = 2;
                options.CircuitBreakerOpenSeconds = 30;
            },
            () => now);
        var request = CreateRequest();
        var context = CreateContext();

        scheduler.AcquireEndpoint("AnswerPool", request, context)
            .CompleteFailure(new TimeoutException("first"));
        scheduler.AcquireEndpoint("AnswerPool", request, context)
            .CompleteFailure(new TimeoutException("second"));

        var blocked = () => scheduler.AcquireEndpoint("AnswerPool", request, context);
        blocked.Should().Throw<InvalidOperationException>()
            .WithMessage("*has no healthy endpoint*");

        now = now.AddSeconds(31);
        using var recovered = scheduler.AcquireEndpoint("AnswerPool", request, context);

        recovered.Selection.EndpointId.Should().Be("answer-a");
    }

    [Fact]
    public void RuntimeLease_ShouldHoldInFlightUntilDisposed()
    {
        var scheduler = CreateScheduler(new ModelEndpointPoolOptions
        {
            Endpoints = [CreateEndpoint("answer-a", concurrencyLimit: 1, queueLimit: 0)]
        });
        var request = CreateRequest();
        var context = CreateContext();

        var lease = scheduler.AcquireEndpoint("AnswerPool", request, context);
        scheduler.GetSnapshot().Pools.Single().Endpoints.Single().Stats.InFlight.Should().Be(1);

        lease.Dispose();

        var endpoint = scheduler.GetSnapshot().Pools.Single().Endpoints.Single();
        endpoint.Stats.InFlight.Should().Be(0);
        endpoint.Stats.SuccessCount.Should().Be(1);
    }

    [Fact]
    public void ModelPoolSnapshot_ShouldNotExposeSecretsOrRawEndpointUrl()
    {
        const string secret = "encv1:protected-test-key";
        var scheduler = CreateScheduler(new ModelEndpointPoolOptions
        {
            Endpoints =
            [
                CreateEndpoint(
                    "answer-a",
                    baseUrl: "https://provider.example.test/v1/secret-path",
                    apiKey: secret,
                    apiKeyEnvironmentVariable: "AICOPILOT_M31_TEST_KEY")
            ]
        });

        var json = System.Text.Json.JsonSerializer.Serialize(scheduler.GetSnapshot());
        var endpoint = scheduler.GetSnapshot().Pools.Single().Endpoints.Single();

        endpoint.HasApiKey.Should().BeTrue();
        endpoint.HasBaseUrl.Should().BeTrue();
        endpoint.BaseUrl.Should().Be("[redacted-endpoint]");
        json.Should().NotContain(secret);
        json.Should().NotContain("provider.example.test");
        json.Should().NotContain("AICOPILOT_M31_TEST_KEY");
    }

    private static InMemoryModelEndpointPoolScheduler CreateScheduler(
        ModelEndpointPoolOptions pool,
        Action<ModelProviderReliabilityOptions>? configureOptions = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        var options = new ModelProviderReliabilityOptions
        {
            CircuitBreakerFailureThreshold = 3,
            CircuitBreakerOpenSeconds = 60,
            EndpointPools =
            {
                ["AnswerPool"] = pool
            }
        };
        configureOptions?.Invoke(options);
        return utcNow is null
            ? new InMemoryModelEndpointPoolScheduler(Options.Create(options))
            : new InMemoryModelEndpointPoolScheduler(Options.Create(options), utcNow);
    }

    private static ModelEndpointOptions CreateEndpoint(
        string endpointId,
        int concurrencyLimit = 2,
        int queueLimit = 2,
        int rpmLimit = 0,
        int tpmLimit = 0,
        string baseUrl = "https://model.example.test/v1",
        string? apiKey = "encv1:test-key",
        string? apiKeyEnvironmentVariable = null)
    {
        return new ModelEndpointOptions
        {
            EndpointId = endpointId,
            Provider = LanguageModelProtocolTypes.OpenAICompatible,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable,
            ConcurrencyLimit = concurrencyLimit,
            QueueLimit = queueLimit,
            TimeoutMs = 5000,
            RpmLimit = rpmLimit,
            TpmLimit = tpmLimit,
            Priority = 1
        };
    }

    private static AgentRuntimeCreateRequest CreateRequest(
        string modelName = "test-model",
        int maxOutputTokens = 10,
        AgentRuntimeCallerContext? caller = null)
    {
        var model = new LanguageModel(
            "OpenAI",
            modelName,
            "https://model.example.test/v1",
            "encv1:test-key",
            new ModelParameters
            {
                MaxTokens = 4096,
                MaxOutputTokens = 1024,
                Temperature = 0.2f
            },
            LanguageModelProtocolTypes.OpenAICompatible,
            LanguageModelUsage.Chat,
            true);
        var template = new ConversationTemplate(
            "test-template",
            "test",
            "system prompt",
            model.Id,
            new TemplateSpecification());

        return new AgentRuntimeCreateRequest(
            model,
            template,
            new AiChatOptions { MaxOutputTokens = maxOutputTokens },
            caller);
    }

    private static ModelProviderExecutionContext CreateContext()
    {
        return new ModelProviderExecutionContext(
            LanguageModelProtocolTypes.OpenAICompatible,
            HasTools: false,
            HasMcpTools: false,
            HasApprovalTools: false,
            HasSideEffectingTools: false,
            HasDataAnalysisSqlToolChain: false);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        predicate().Should().BeTrue();
    }
}
