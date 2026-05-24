using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
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
    ILogger<AgentRuntimeFactory> logger,
    IModelEndpointPoolScheduler? endpointPoolScheduler = null) : IAgentRuntimeFactory
{
    public ScopedRuntimeAgent Create(AgentRuntimeCreateRequest request)
    {
        var scope = serviceScopeFactory.CreateScope();
        try
        {
            var context = BuildExecutionContext(request);
            costBudgetPolicy.EnsureWithinBudget(request, context);
            var providers = scope.ServiceProvider.GetServices<IChatClientProvider>().ToArray();
            var endpointLease = TryAcquireEndpoint(request, context);
            var endpointSelection = endpointLease?.Selection;
            var runtimeModel = endpointSelection is null
                ? request.Model
                : CreateEndpointModel(request.Model, endpointSelection);
            var requestedProvider = runtimeModel.ProtocolType;
            var providerCandidates = new[] { requestedProvider }
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

                if (!string.Equals(providerName, requestedProvider, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Falling back model provider from {RequestedProvider} to {FallbackProvider}. HasTools={HasTools}; HighRisk={HighRisk}.",
                        requestedProvider,
                        providerName,
                        context.HasTools,
                        context.IsHighRiskToolChain);
                }

                try
                {
                    if (endpointSelection is not null)
                    {
                        endpointPoolScheduler?.RecordStickyStreaming(endpointSelection.EndpointId);
                    }

                    var chatClientBuilder = chatClientProvider
                        .CreateClient(runtimeModel)
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

                    return new ScopedRuntimeAgent(
                        new MicrosoftAgentRuntimeChatAgent(agent),
                        new AgentRuntimeHandle(agent, scope, endpointLease));
                }
                catch (Exception ex)
                {
                    circuitBreaker.RecordFailure(providerName, ex);
                    endpointLease?.CompleteFailure(ex);

                    if (context.IsHighRiskToolChain || string.Equals(providerName, providerCandidates.Last(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw;
                    }

                    logger.LogWarning(ex, "Model provider {ProviderName} failed; trying next allowed fallback provider.", providerName);
                }
            }

            var noProviderException = new InvalidOperationException($"No healthy chat client provider is registered for '{requestedProvider}'.");
            endpointLease?.CompleteFailure(noProviderException);
            throw noProviderException;
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
            request.Model.ProtocolType,
            tools.Count > 0,
            tools.Any(tool => tool.Kind == AiToolCallKind.Mcp),
            tools.Any(tool => tool.RequiresApproval || tool.RiskLevel == AiToolRiskLevel.RequiresApproval),
            tools.Any(tool => tool.CapabilityKind == AiToolCapabilityKind.SideEffecting),
            tools.Any(tool => string.Equals(tool.TargetName, DataAnalysisPluginNames.DataAnalysisPlugin, StringComparison.OrdinalIgnoreCase)));
    }

    private ModelEndpointLease? TryAcquireEndpoint(
        AgentRuntimeCreateRequest request,
        ModelProviderExecutionContext context)
    {
        if (endpointPoolScheduler is null)
        {
            return null;
        }

        var poolName = ResolvePoolName(request, context);
        try
        {
            return endpointPoolScheduler.AcquireEndpoint(poolName, request, context);
        }
        catch (ModelEndpointPoolNotConfiguredException ex)
        {
            logger.LogDebug(ex, "Model endpoint pool {PoolName} is not available; falling back to the language model configuration.", poolName);
            return null;
        }
    }

    private static string ResolvePoolName(
        AgentRuntimeCreateRequest request,
        ModelProviderExecutionContext context)
    {
        if (context.HasDataAnalysisSqlToolChain)
        {
            return "TextToSqlPool";
        }

        return request.Template.Scope switch
        {
            ConversationTemplateScope.AgentPlanner => "PlannerPool",
            ConversationTemplateScope.RagAnswer => "AnswerPool",
            ConversationTemplateScope.ToolCallPolicy => "PlannerPool",
            _ when request.Model.SupportsUsage(LanguageModelUsage.Routing) &&
                   !request.Model.SupportsUsage(LanguageModelUsage.Chat) => "RoutingPool",
            _ => "AnswerPool"
        };
    }

    private static LanguageModel CreateEndpointModel(
        LanguageModel model,
        ModelEndpointSelection selection)
    {
        return new LanguageModel(
            model.Provider,
            model.Name,
            string.IsNullOrWhiteSpace(selection.BaseUrl) ? model.BaseUrl : selection.BaseUrl,
            string.IsNullOrWhiteSpace(selection.ApiKey) ? model.ApiKey : selection.ApiKey,
            model.Parameters,
            string.IsNullOrWhiteSpace(selection.Provider) ? model.ProtocolType : selection.Provider,
            model.Usage,
            model.IsEnabled);
    }

    private sealed class AgentRuntimeHandle(
        ChatClientAgent agent,
        IServiceScope scope,
        ModelEndpointLease? endpointLease) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
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
            finally
            {
                endpointLease?.Dispose();
            }
        }
    }
}
