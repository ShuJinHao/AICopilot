using System.Text.Json;
using AICopilot.SharedKernel.Ai;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;

namespace AICopilot.Services.Contracts;

public sealed record AgentRuntimeCreateRequest(
    LanguageModel Model,
    ConversationTemplate Template,
    AiChatOptions Options);

public sealed record RuntimeAgentRunOptions(AiChatOptions Options);

public sealed record RuntimeAgentUpdate(IReadOnlyList<AiRuntimeContent> Contents);

public sealed record StructuredAgentResponse<T>(string? Text, T? Result);

public sealed record ModelProviderFallbackRouteDto(
    string Provider,
    IReadOnlyList<string> FallbackProviders);

public sealed record ModelProviderReliabilityDto(
    bool FallbackEnabled,
    IReadOnlyList<ModelProviderFallbackRouteDto> FallbackProviders,
    int CircuitBreakerFailureThreshold,
    int CircuitBreakerOpenSeconds,
    int MaxOutputTokens,
    IReadOnlyList<string> FallbackAllowedScopes,
    IReadOnlyList<string> FallbackBlockedScopes);

public interface IRuntimeAgentSession;

public interface IRuntimeChatAgent
{
    Task<IRuntimeAgentSession> CreateSessionAsync(CancellationToken cancellationToken = default);

    Task<string> SerializeSessionAsync(
        IRuntimeAgentSession session,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default);

    Task<IRuntimeAgentSession> DeserializeSessionAsync(
        string serializedSessionState,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default);

    Task<StructuredAgentResponse<T>> RunStructuredAsync<T>(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession? session,
        JsonSerializerOptions serializerOptions,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        string input,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class ScopedRuntimeAgent(
    IRuntimeChatAgent agent,
    IAsyncDisposable runtimeScope) : IAsyncDisposable
{
    private int disposed;

    public IRuntimeChatAgent Agent { get; } = agent;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await runtimeScope.DisposeAsync();
    }
}

public interface IAgentRuntimeFactory
{
    bool CanCreate(string providerName);

    ScopedRuntimeAgent Create(AgentRuntimeCreateRequest request);
}

public interface IModelProviderReliabilitySnapshotReader
{
    ModelProviderReliabilityDto GetSnapshot();
}

public sealed record ModelEndpointStatsDto(
    int InFlight,
    int QueueLength,
    long SuccessCount,
    long FailureCount,
    double AverageDurationMs,
    double P95DurationMs,
    long RateLimitCount,
    long CircuitBreakerOpenCount,
    long FallbackCount,
    int StickyStreamingCount = 0,
    string CircuitState = "Closed",
    string? LastFailureReason = null);

public sealed record ModelEndpointDto(
    string EndpointId,
    string Provider,
    string BaseUrl,
    int ConcurrencyLimit,
    int QueueLimit,
    int TimeoutMs,
    int RpmLimit,
    int TpmLimit,
    int Weight,
    int Priority,
    bool IsHealthy,
    bool IsCircuitOpen,
    bool HasApiKey,
    ModelEndpointStatsDto Stats);

public sealed record ModelPoolDto(
    string PoolName,
    string Usage,
    string Strategy,
    IReadOnlyList<ModelEndpointDto> Endpoints);

public sealed record ModelPoolSnapshotDto(
    IReadOnlyList<ModelPoolDto> Pools);

public interface IModelPoolSnapshotReader
{
    ModelPoolSnapshotDto GetSnapshot();
}
