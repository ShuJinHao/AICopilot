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
