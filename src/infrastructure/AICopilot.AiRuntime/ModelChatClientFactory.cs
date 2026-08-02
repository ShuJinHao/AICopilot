using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AICopilot.AiRuntime;

/// <summary>
/// Creates the provider-bound <see cref="IChatClient"/> shared by lightweight
/// agents and Harness main chat. Endpoint credentials, quota reservations,
/// telemetry, and circuit accounting are all resolved here.
/// </summary>
internal sealed class ModelChatClientFactory(
    IHostEnvironment hostEnvironment,
    IModelProviderHealth providerHealth,
    IModelFallbackPolicy fallbackPolicy,
    IModelCircuitBreaker circuitBreaker,
    IModelCostBudgetPolicy costBudgetPolicy,
    ILogger<ModelChatClientFactory> logger,
    IOptions<ModelProviderReliabilityOptions> reliabilityOptions,
    IModelEndpointPoolScheduler? endpointPoolScheduler = null)
{
    public IChatClient Create(
        AgentRuntimeCreateRequest request,
        IServiceProvider scopedServices)
    {
        var quotaStore = scopedServices.GetService<IModelQuotaReservationStore>()
            ?? throw new InvalidOperationException(
                "PostgreSQL model quota reservation store is required before any model call can run.");
        var context = BuildExecutionContext(request);
        costBudgetPolicy.EnsureWithinBudget(request, context);
        var poolName = ResolvePoolName(request, context);
        var endpointSelection = TrySelectEndpoint(poolName);
        var runtimeModel = endpointSelection is null
            ? request.Model
            : CreateEndpointModel(request.Model, endpointSelection);
        var requestedProvider = runtimeModel.ProtocolType;
        var providerCandidates = new[] { requestedProvider }
            .Concat(fallbackPolicy.GetFallbackProviders(request, context))
            .Where(providerName => !string.IsNullOrWhiteSpace(providerName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var providers = scopedServices.GetServices<IChatClientProvider>().ToArray();

        foreach (var providerName in providerCandidates)
        {
            if (!providerHealth.IsHealthy(providerName) || !circuitBreaker.CanAttempt(providerName))
            {
                logger.LogWarning(
                    "Skipping model provider {ProviderName} because health or circuit breaker blocked the attempt.",
                    providerName);
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
                var providerModel = string.Equals(
                    providerName,
                    runtimeModel.ProtocolType,
                    StringComparison.OrdinalIgnoreCase)
                    ? runtimeModel
                    : CloneWithProvider(runtimeModel, providerName);
                var telemetryClient = chatClientProvider
                    .CreateClient(providerModel)
                    .AsBuilder()
                    .UseOpenTelemetry(
                        sourceName: nameof(AiRuntime),
                        configure: configuration =>
                            configuration.EnableSensitiveData = hostEnvironment.IsDevelopment())
                    .Build();
                var governedEndpoint = endpointSelection is null
                    ? new ModelEndpointSelection(
                        poolName,
                        $"model:{request.Model.Id.Value:D}",
                        providerName,
                        providerModel.BaseUrl,
                        !string.IsNullOrWhiteSpace(providerModel.ApiKey),
                        ApiKey: null)
                    : endpointSelection with { Provider = providerName };

                return new ModelCallGovernanceChatClient(
                    telemetryClient,
                    quotaStore,
                    request,
                    governedEndpoint,
                    poolName,
                    reliabilityOptions.Value,
                    circuitBreaker,
                    endpointPoolScheduler);
            }
            catch (Exception exception)
            {
                if (context.IsHighRiskToolChain ||
                    string.Equals(
                        providerName,
                        providerCandidates.Last(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }

                logger.LogWarning(
                    "Model provider {ProviderName} could not be constructed; trying the next allowed provider. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                    providerName,
                    exception.GetType().Name);
            }
        }

        throw new InvalidOperationException(
            $"No healthy chat client provider is registered for '{requestedProvider}'.");
    }

    public bool CanCreate(string providerName, IServiceProvider scopedServices)
    {
        return scopedServices
            .GetServices<IChatClientProvider>()
            .Any(provider => provider.CanHandle(providerName));
    }

    private ModelEndpointSelection? TrySelectEndpoint(string poolName)
    {
        if (endpointPoolScheduler is null)
        {
            return null;
        }

        try
        {
            return endpointPoolScheduler.SelectEndpoint(poolName);
        }
        catch (ModelEndpointPoolNotConfiguredException exception)
        {
            logger.LogDebug(
                "Model endpoint pool {PoolName} is not available; using the language model configuration. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                poolName,
                exception.GetType().Name);
            return null;
        }
    }

    private static ModelProviderExecutionContext BuildExecutionContext(
        AgentRuntimeCreateRequest request)
    {
        var tools = request.Options.Tools;
        return new ModelProviderExecutionContext(
            request.Model.ProtocolType,
            tools.Count > 0,
            tools.Any(tool => tool.Kind == AiToolCallKind.Mcp),
            tools.Any(tool =>
                tool.RequiresApproval ||
                tool.RiskLevel == AiToolRiskLevel.RequiresApproval),
            tools.Any(tool => tool.CapabilityKind == AiToolCapabilityKind.SideEffecting),
            HasDataAnalysisSqlToolChain: false);
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

    private static LanguageModel CloneWithProvider(
        LanguageModel model,
        string providerName)
    {
        return new LanguageModel(
            model.Provider,
            model.Name,
            model.BaseUrl,
            model.ApiKey,
            model.Parameters,
            providerName,
            model.Usage,
            model.IsEnabled);
    }
}
