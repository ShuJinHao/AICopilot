using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AICopilot.AiRuntime;

internal sealed class AgentRuntimeFactory(
    IServiceScopeFactory serviceScopeFactory,
    IHostEnvironment hostEnvironment,
    IModelProviderHealth providerHealth,
    IModelFallbackPolicy fallbackPolicy,
    IModelCircuitBreaker circuitBreaker,
    IModelCostBudgetPolicy costBudgetPolicy,
    ILogger<AgentRuntimeFactory> logger,
    IOptions<ModelProviderReliabilityOptions> reliabilityOptions,
    IModelEndpointPoolScheduler? endpointPoolScheduler = null) : IAgentRuntimeFactory
{
    public ScopedRuntimeAgent Create(AgentRuntimeCreateRequest request)
    {
        var scope = serviceScopeFactory.CreateScope();
        try
        {
            var quotaStore = scope.ServiceProvider.GetService<IModelQuotaReservationStore>()
                ?? throw new InvalidOperationException(
                    "PostgreSQL model quota reservation store is required before any model call can run.");
            var context = BuildExecutionContext(request);
            costBudgetPolicy.EnsureWithinBudget(request, context);
            var providers = scope.ServiceProvider.GetServices<IChatClientProvider>().ToArray();
            var endpointLease = TryAcquireEndpoint(request, context);
            var endpointSelection = endpointLease?.Selection;
            var poolName = ResolvePoolName(request, context);
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

                    var quotaEndpoint = endpointSelection ?? new ModelEndpointSelection(
                        poolName,
                        $"model:{request.Model.Id.Value:D}",
                        runtimeModel.ProtocolType,
                        runtimeModel.BaseUrl,
                        !string.IsNullOrWhiteSpace(runtimeModel.ApiKey),
                        ApiKey: null);

                    return new ScopedRuntimeAgent(
                        new QuotaReservedRuntimeChatAgent(
                            new MicrosoftAgentRuntimeChatAgent(agent),
                            quotaStore,
                            request,
                            quotaEndpoint,
                            poolName,
                            reliabilityOptions.Value),
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

                    logger.LogWarning(
                        "Model provider {ProviderName} failed; trying next allowed fallback provider. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                        providerName,
                        ex.GetType().Name);
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
            logger.LogDebug(
                "Model endpoint pool {PoolName} is not available; falling back to the language model configuration. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                poolName,
                ex.GetType().Name);
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
            ConversationTemplateScope.TextToSql => "TextToSqlPool",
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
