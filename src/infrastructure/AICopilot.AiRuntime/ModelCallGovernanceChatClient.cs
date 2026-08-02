using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.AI;

namespace AICopilot.AiRuntime;

/// <summary>
/// Applies quota, endpoint telemetry, and circuit-breaker accounting at the
/// actual provider boundary. Agent and Harness orchestration may invoke this
/// client multiple times; every invocation receives an independent lease.
/// </summary>
internal sealed class ModelCallGovernanceChatClient(
    IChatClient inner,
    IModelQuotaReservationStore quotaStore,
    AgentRuntimeCreateRequest createRequest,
    ModelEndpointSelection endpoint,
    string poolName,
    ModelProviderReliabilityOptions reliabilityOptions,
    IModelCircuitBreaker circuitBreaker,
    IModelEndpointPoolScheduler? endpointPoolScheduler)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        var budget = ResolveBudget(materialized, options);
        EnsureCircuitAllowsAttempt();
        var lease = await ReserveAsync(budget, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var dispatched = false;
        ChatResponse response;

        try
        {
            RecordStarted(isStreaming: false);
            dispatched = true;
            response = await base.GetResponseAsync(materialized, options, cancellationToken);
        }
        catch (Exception exception)
        {
            await SettleUnknownOrReleaseAsync(
                lease,
                budget,
                dispatched,
                CancellationToken.None);
            RecordFailed(stopwatch.Elapsed, exception, dispatched);
            throw;
        }

        RecordSucceeded(stopwatch.Elapsed);
        await SettleKnownAsync(
            lease,
            ResolveActualUsage(budget, response.Usage),
            CancellationToken.None);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        var budget = ResolveBudget(materialized, options);
        EnsureCircuitAllowsAttempt();
        var lease = await ReserveAsync(budget, cancellationToken);
        var usage = new UsageDetails();
        var stopwatch = Stopwatch.StartNew();
        var dispatched = false;
        var completed = false;
        Exception? failure = null;

        try
        {
            RecordStarted(isStreaming: true);
            dispatched = true;
            await using var enumerator = base.GetStreamingResponseAsync(
                    materialized,
                    options,
                    cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                    throw;
                }

                if (!hasNext)
                {
                    completed = true;
                    break;
                }

                var update = enumerator.Current;
                AccumulateUsage(update, usage);
                yield return update;
            }
        }
        finally
        {
            if (completed)
            {
                RecordSucceeded(stopwatch.Elapsed);
                await SettleKnownAsync(
                    lease,
                    ResolveActualUsage(budget, usage),
                    CancellationToken.None);
            }
            else
            {
                // Once enumeration was dispatched, cancellation and transport
                // ambiguity settle the full reservation conservatively.
                await SettleUnknownOrReleaseAsync(
                    lease,
                    budget,
                    dispatched,
                    CancellationToken.None);
                RecordFailed(
                    stopwatch.Elapsed,
                    failure ?? new OperationCanceledException("Streaming model call did not complete."),
                    dispatched);
            }
        }
    }

    private void RecordStarted(bool isStreaming)
    {
        endpointPoolScheduler?.RecordStarted(endpoint.EndpointId);
        if (isStreaming)
        {
            endpointPoolScheduler?.RecordStickyStreaming(endpoint.EndpointId);
        }
    }

    private void EnsureCircuitAllowsAttempt()
    {
        if (!circuitBreaker.CanAttempt(endpoint.Provider))
        {
            throw new ModelProviderCircuitOpenException(endpoint.Provider);
        }
    }

    private void RecordSucceeded(TimeSpan duration)
    {
        endpointPoolScheduler?.RecordSucceeded(endpoint.EndpointId, duration);
        circuitBreaker.RecordSuccess(endpoint.Provider);
    }

    private void RecordFailed(TimeSpan duration, Exception exception, bool dispatched)
    {
        if (!dispatched)
        {
            return;
        }

        endpointPoolScheduler?.RecordFailed(endpoint.EndpointId, duration, exception);
        if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests })
        {
            endpointPoolScheduler?.RecordRateLimited(endpoint.EndpointId);
        }

        if (exception is not OperationCanceledException)
        {
            circuitBreaker.RecordFailure(endpoint.Provider, exception);
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
        var correlationHash = Hash(string.Join(
            '|',
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
        IReadOnlyCollection<ChatMessage> messages,
        ChatOptions? options)
    {
        var inputCharacters = (options?.Instructions?.Length ??
                               createRequest.Options.Instructions?.Length ??
                               0) +
                              messages.Sum(message =>
                                  message.Text?.Length ??
                                  message.Contents.Sum(content => content switch
                                  {
                                      TextContent text => text.Text.Length,
                                      FunctionResultContent result =>
                                          result.Result?.ToString()?.Length ?? 0,
                                      _ => 32
                                  }));
        var inputTokens = Math.Max(1, (int)Math.Ceiling(inputCharacters / 4d));
        var outputTokens = options?.MaxOutputTokens
                           ?? createRequest.Options.MaxOutputTokens
                           ?? createRequest.Model.Parameters.MaxOutputTokens;
        return new ModelCallBudget(inputTokens, Math.Max(1, outputTokens));
    }

    private static ModelCallBudget ResolveActualUsage(
        ModelCallBudget estimate,
        UsageDetails? usage)
    {
        return usage is null
            ? estimate
            : new ModelCallBudget(
                ClampTokens(usage.InputTokenCount, estimate.InputTokens),
                ClampTokens(usage.OutputTokenCount, estimate.OutputTokens));
    }

    private static int ClampTokens(long? value, int fallback)
    {
        return value is null
            ? fallback
            : (int)Math.Clamp(value.Value, 0, int.MaxValue);
    }

    private static void AccumulateUsage(ChatResponseUpdate update, UsageDetails usage)
    {
        foreach (var content in update.Contents.OfType<UsageContent>())
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

internal sealed class ModelProviderCircuitOpenException(string providerName)
    : InvalidOperationException(
        $"Model provider '{providerName}' is unavailable because its circuit is open.");
