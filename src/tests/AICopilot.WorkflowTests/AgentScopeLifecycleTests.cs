using AICopilot.AiGatewayService.Agents;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;

namespace AICopilot.WorkflowTests;

public sealed class AgentScopeLifecycleTests
{
    [Fact]
    public async Task CreateAgent_ShouldResolveProviderFromAgentScope_AndDisposeScopeWithAgent()
    {
        var builder = Host.CreateApplicationBuilder();
        var secretProtector = new EndpointPoolSecretProtector();
        builder.Services.AddScoped<ScopedLifecycleProbe>();
        builder.Services.AddScoped<IChatClientProvider, ProbeChatClientProvider>();
        builder.Services.AddSingleton<IModelQuotaReservationStore, UnusedModelQuotaReservationStore>();
        builder.Services.AddSingleton<ISecretProtector>(secretProtector);
        builder.AddAiRuntime();

        await using var serviceProvider = builder.Services.BuildServiceProvider(validateScopes: true);
        using var requestScope = serviceProvider.CreateScope();
        var factory = new ConfiguredAgentRuntimeFactory(
            templateRepository: new InMemoryReadRepository<ConversationTemplate>(),
            modelRepository: new InMemoryReadRepository<LanguageModel>(),
            runtimeFactory: serviceProvider.GetRequiredService<IAgentRuntimeFactory>());

        var model = new LanguageModel(
            ProbeChatClientProvider.ProviderName,
            "fake-model",
            "http://localhost/v1",
            secretProtector.Protect("fake-key")!,
            new ModelParameters { MaxTokens = 4096, Temperature = 0.2f });
        var template = new ConversationTemplate(
            "test-template",
            "test",
            "system prompt",
            model.Id,
            new TemplateSpecification());

        var scopedAgent = factory.CreateAgent(model, template);
        var probe = ProbeChatClientProvider.LastProbe;
        probe.Should().NotBeNull();
        probe!.IsDisposed.Should().BeFalse();

        await scopedAgent.DisposeAsync();

        probe.IsDisposed.Should().BeTrue();
    }

    private sealed class ScopedLifecycleProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class UnusedModelQuotaReservationStore : IModelQuotaReservationStore
    {
        public Task<ModelQuotaReservationOutcome> TryReserveAsync(
            ModelQuotaReservationRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This lifecycle test does not execute a model call.");
        }

        public Task<ModelQuotaReservationResult> SettleAsync(
            ModelQuotaSettlement settlement,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This lifecycle test does not execute a model call.");
        }

        public Task<int> ReclaimExpiredAsync(
            DateTimeOffset nowUtc,
            int maxItems,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class ProbeChatClientProvider(ScopedLifecycleProbe probe) : IChatClientProvider
    {
        public const string ProviderName = "Probe";

        public static ScopedLifecycleProbe? LastProbe { get; private set; }

        public bool CanHandle(string providerName)
        {
            return string.Equals(providerName, ProviderName, StringComparison.OrdinalIgnoreCase);
        }

        public IChatClient CreateClient(LanguageModel model)
        {
            LastProbe = probe;
            return new ProbeChatClient();
        }
    }

    private sealed class ProbeChatClient : IChatClient
    {
        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return serviceType.IsInstanceOfType(this) ? this : null;
        }
    }
}
