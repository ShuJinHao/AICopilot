using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiRuntime;

internal sealed class QuotaReservedRuntimeChatAgent(
    IRuntimeChatAgent inner,
    IModelQuotaReservationStore quotaStore,
    AgentRuntimeCreateRequest createRequest,
    ModelEndpointSelection endpoint,
    string poolName,
    ModelProviderReliabilityOptions reliabilityOptions)
    : IRuntimeChatAgent
{
    public Task<IRuntimeAgentSession> CreateSessionAsync(CancellationToken cancellationToken = default) =>
        inner.CreateSessionAsync(cancellationToken);

    public Task<string> SerializeSessionAsync(
        IRuntimeAgentSession session,
        System.Text.Json.JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default) =>
        inner.SerializeSessionAsync(session, serializerOptions, cancellationToken);

    public Task<IRuntimeAgentSession> DeserializeSessionAsync(
        string serializedSessionState,
        System.Text.Json.JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default) =>
        inner.DeserializeSessionAsync(serializedSessionState, serializerOptions, cancellationToken);

    public async Task<StructuredAgentResponse<T>> RunStructuredAsync<T>(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession? session,
        System.Text.Json.JsonSerializerOptions serializerOptions,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        var budget = ResolveBudget(materialized, options);
        var lease = await ReserveAsync(budget, cancellationToken);
        var dispatched = false;
        try
        {
            dispatched = true;
            var response = await inner.RunStructuredAsync<T>(
                materialized,
                session,
                serializerOptions,
                options,
                cancellationToken);
            await SettleKnownAsync(lease, budget, CancellationToken.None);
            return response;
        }
        catch
        {
            await SettleUnknownOrReleaseAsync(lease, budget, dispatched, CancellationToken.None);
            throw;
        }
    }

    public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        var budget = ResolveBudget(materialized, options);
        var lease = await ReserveAsync(budget, cancellationToken);
        var usage = new AiUsageDetails();
        var completed = false;
        try
        {
            await foreach (var update in inner.RunStreamingAsync(
                               materialized,
                               session,
                               options,
                               cancellationToken))
            {
                AccumulateUsage(update, usage);
                yield return update;
            }

            completed = true;
        }
        finally
        {
            if (completed)
            {
                await SettleKnownAsync(lease, ResolveActualUsage(budget, usage), CancellationToken.None);
            }
            else
            {
                await SettleUnknownOrReleaseAsync(lease, budget, wasDispatched: true, CancellationToken.None);
            }
        }
    }

    public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        string input,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in RunStreamingAsync(
                           [new AiChatMessage(AiChatRole.User, input)],
                           session,
                           options,
                           cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<ModelQuotaReservationLease> ReserveAsync(
        ModelCallBudget budget,
        CancellationToken cancellationToken)
    {
        var fallbackEndpointId = $"model:{createRequest.Model.Id.Value:D}";
        var isLanguageModelFallback = string.Equals(
            endpoint.EndpointId,
            fallbackEndpointId,
            StringComparison.OrdinalIgnoreCase);
        var poolOptions = reliabilityOptions.EndpointPools.GetValueOrDefault(poolName)
                          ?? (isLanguageModelFallback ? new ModelEndpointPoolOptions() : null);
        var endpointOptions = poolOptions?.Endpoints
            .SingleOrDefault(candidate => string.Equals(
                candidate.EndpointId,
                endpoint.EndpointId,
                StringComparison.OrdinalIgnoreCase))
                              ?? (isLanguageModelFallback
                                  ? new ModelEndpointOptions { EndpointId = fallbackEndpointId }
                                  : null);
        if (poolOptions is null || endpointOptions is null)
        {
            throw new ModelQuotaReservationDeniedException(
                ModelQuotaReservationResult.PolicyUnavailable,
                retryAtUtc: null,
                "Distributed model quota policy is not configured for the selected pool and endpoint.");
        }
        var caller = createRequest.Caller;
        var tenantKey = caller?.TenantId?.Trim() ?? "tenant:none";
        var roleKey = caller?.Role?.Trim() ?? "role:none";
        var now = DateTimeOffset.UtcNow;
        var correlationHash = Hash(string.Join('|',
            Guid.NewGuid().ToString("N"),
            createRequest.Model.Id.Value.ToString("D"),
            endpoint.EndpointId,
            caller?.UserId?.ToString("D") ?? "anonymous"));
        var timeoutMs = endpointOptions.TimeoutMs is > 0
            ? endpointOptions.TimeoutMs
            : 60_000;
        var outcome = await quotaStore.TryReserveAsync(
            new ModelQuotaReservationRequest(
                Hash($"tenant|{tenantKey}"),
                caller?.UserId,
                Hash($"tenant|{tenantKey}|role|{roleKey}"),
                createRequest.Model.Id,
                endpoint.EndpointId,
                poolName,
                budget.InputTokens,
                budget.OutputTokens,
                ConcurrencySlots: 1,
                endpointOptions.RpmLimit,
                endpointOptions.TpmLimit,
                endpointOptions.ConcurrencyLimit,
                poolOptions.ModelRpmLimit,
                poolOptions.ModelTpmLimit,
                poolOptions.ModelConcurrencyLimit,
                reliabilityOptions.PerUserRpmLimit,
                reliabilityOptions.PerUserTpmLimit,
                reliabilityOptions.PerUserConcurrencyLimit,
                reliabilityOptions.PerRoleRpmLimit,
                reliabilityOptions.PerRoleTpmLimit,
                reliabilityOptions.PerRoleConcurrencyLimit,
                reliabilityOptions.PerTenantRpmLimit,
                reliabilityOptions.PerTenantTpmLimit,
                reliabilityOptions.PerTenantConcurrencyLimit,
                correlationHash,
                now,
                TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs + 30_000, 60_000, 1_800_000))),
            cancellationToken);
        if (outcome.Result is (ModelQuotaReservationResult.Granted or ModelQuotaReservationResult.Duplicate) &&
            outcome.Lease is not null)
        {
            return outcome.Lease;
        }

        throw new ModelQuotaReservationDeniedException(outcome.Result, outcome.RetryAtUtc, outcome.SafeReason);
    }

    private async Task SettleKnownAsync(
        ModelQuotaReservationLease lease,
        ModelCallBudget usage,
        CancellationToken cancellationToken)
    {
        _ = await quotaStore.SettleAsync(
            new ModelQuotaSettlement(
                lease,
                usage.InputTokens,
                usage.OutputTokens,
                WasDispatched: true,
                OutcomeKnown: true,
                FailureCode: null,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task SettleUnknownOrReleaseAsync(
        ModelQuotaReservationLease lease,
        ModelCallBudget usage,
        bool wasDispatched,
        CancellationToken cancellationToken)
    {
        _ = await quotaStore.SettleAsync(
            new ModelQuotaSettlement(
                lease,
                usage.InputTokens,
                usage.OutputTokens,
                wasDispatched,
                OutcomeKnown: !wasDispatched,
                FailureCode: wasDispatched ? "model_call_outcome_unknown" : null,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private ModelCallBudget ResolveBudget(
        IReadOnlyCollection<AiChatMessage> messages,
        RuntimeAgentRunOptions? options)
    {
        var inputCharacters = (createRequest.Options.Instructions?.Length ?? 0) +
                              messages.Sum(message =>
                                  message.Text?.Length ??
                                  message.Contents.Sum(content => content switch
                                  {
                                      AiTextContent text => text.Text.Length,
                                      AiFunctionResultContent result => result.Result?.ToString()?.Length ?? 0,
                                      _ => 32
                                  }));
        var inputTokens = Math.Max(1, (int)Math.Ceiling(inputCharacters / 4d));
        var outputTokens = options?.Options.MaxOutputTokens
                           ?? createRequest.Options.MaxOutputTokens
                           ?? createRequest.Model.Parameters.MaxOutputTokens;
        return new ModelCallBudget(inputTokens, Math.Max(1, outputTokens));
    }

    private static ModelCallBudget ResolveActualUsage(ModelCallBudget estimate, AiUsageDetails usage)
    {
        return new ModelCallBudget(
            ClampTokens(usage.InputTokenCount, estimate.InputTokens),
            ClampTokens(usage.OutputTokenCount, estimate.OutputTokens));
    }

    private static int ClampTokens(long? value, int fallback)
    {
        return value is null
            ? fallback
            : (int)Math.Clamp(value.Value, 0, int.MaxValue);
    }

    private static void AccumulateUsage(RuntimeAgentUpdate update, AiUsageDetails usage)
    {
        foreach (var content in update.Contents.OfType<AiUsageContent>())
        {
            usage.Add(content.Details);
        }
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private sealed record ModelCallBudget(int InputTokens, int OutputTokens);
}

internal sealed class ModelQuotaReservationDeniedException(
    ModelQuotaReservationResult result,
    DateTimeOffset? retryAtUtc,
    string safeReason)
    : InvalidOperationException($"Model quota reservation denied: {result}. {safeReason}")
{
    public ModelQuotaReservationResult Result { get; } = result;

    public DateTimeOffset? RetryAtUtc { get; } = retryAtUtc;
}
