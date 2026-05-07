using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiRuntime;

internal sealed class AgentRuntimeFactory(
    IServiceScopeFactory serviceScopeFactory,
    IHostEnvironment hostEnvironment,
    IModelProviderHealth providerHealth,
    IModelFallbackPolicy fallbackPolicy,
    IModelCircuitBreaker circuitBreaker,
    IModelCostBudgetPolicy costBudgetPolicy,
    ILogger<AgentRuntimeFactory> logger) : IAgentRuntimeFactory
{
    public ScopedRuntimeAgent Create(AgentRuntimeCreateRequest request)
    {
        var scope = serviceScopeFactory.CreateScope();
        try
        {
            var context = BuildExecutionContext(request);
            costBudgetPolicy.EnsureWithinBudget(request, context);
            var providers = scope.ServiceProvider.GetServices<IChatClientProvider>().ToArray();
            var providerCandidates = new[] { request.Model.Provider }
                .Concat(fallbackPolicy.GetFallbackProviders(request, context))
                .Where(providerName => !string.IsNullOrWhiteSpace(providerName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var providerName in providerCandidates)
            {
                if (!providerHealth.IsHealthy(providerName) || !circuitBreaker.CanAttempt(providerName))
                {
                    logger.LogWarning("Skipping model provider {ProviderName} because health or circuit breaker blocked the attempt.", providerName);
                    continue;
                }

                var chatClientProvider = providers.FirstOrDefault(provider => provider.CanHandle(providerName));
                if (chatClientProvider is null)
                {
                    continue;
                }

                if (!string.Equals(providerName, request.Model.Provider, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Falling back model provider from {RequestedProvider} to {FallbackProvider}. HasTools={HasTools}; HighRisk={HighRisk}.",
                        request.Model.Provider,
                        providerName,
                        context.HasTools,
                        context.IsHighRiskToolChain);
                }

                try
                {
                    var chatClientBuilder = chatClientProvider
                        .CreateClient(request.Model)
                        .AsBuilder()
                        .UseFunctionInvocation()
                        .UseOpenTelemetry(
                            sourceName: nameof(AiRuntime),
                            configure: cfg => cfg.EnableSensitiveData = hostEnvironment.IsDevelopment());

                    var agentOptions = new ChatClientAgentOptions
                    {
                        Name = request.Template.Name,
                        ChatOptions = RuntimeToolAdapter.ToChatOptions(request.Options)
                    };

                    var agent = chatClientBuilder.BuildAIAgent(agentOptions, services: scope.ServiceProvider);
                    circuitBreaker.RecordSuccess(providerName);
                    return new ScopedRuntimeAgent(new MicrosoftAgentRuntimeChatAgent(agent), new AgentRuntimeHandle(agent, scope));
                }
                catch (Exception ex)
                {
                    circuitBreaker.RecordFailure(providerName, ex);
                    if (context.IsHighRiskToolChain || string.Equals(providerName, providerCandidates.Last(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw;
                    }

                    logger.LogWarning(ex, "Model provider {ProviderName} failed; trying next allowed fallback provider.", providerName);
                }
            }

            throw new InvalidOperationException($"No healthy chat client provider is registered for '{request.Model.Provider}'.");
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public bool CanCreate(string providerName)
    {
        using var scope = serviceScopeFactory.CreateScope();
        return scope.ServiceProvider
            .GetServices<IChatClientProvider>()
            .Any(provider => provider.CanHandle(providerName));
    }

    private static ModelProviderExecutionContext BuildExecutionContext(AgentRuntimeCreateRequest request)
    {
        var tools = request.Options.Tools;
        return new ModelProviderExecutionContext(
            request.Model.Provider,
            tools.Count > 0,
            tools.Any(tool => tool.Kind == AiToolCallKind.Mcp),
            tools.Any(tool => tool.RequiresApproval || tool.RiskLevel == AiToolRiskLevel.RequiresApproval),
            tools.Any(tool => tool.CapabilityKind == AiToolCapabilityKind.SideEffecting),
            tools.Any(tool => string.Equals(tool.TargetName, DataAnalysisPluginNames.DataAnalysisPlugin, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class AgentRuntimeHandle(ChatClientAgent agent, IServiceScope scope) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            object agentObject = agent;
            if (agentObject is IAsyncDisposable asyncDisposableAgent)
            {
                await asyncDisposableAgent.DisposeAsync();
            }
            else if (agentObject is IDisposable disposableAgent)
            {
                disposableAgent.Dispose();
            }

            if (scope is IAsyncDisposable asyncDisposableScope)
            {
                await asyncDisposableScope.DisposeAsync();
            }
            else
            {
                scope.Dispose();
            }
        }
    }
}
