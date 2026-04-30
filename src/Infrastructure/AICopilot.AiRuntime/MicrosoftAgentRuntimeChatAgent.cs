using System.Runtime.CompilerServices;
using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Agents.AI;

namespace AICopilot.AiRuntime;

internal sealed class MicrosoftAgentRuntimeChatAgent(ChatClientAgent agent) : IRuntimeChatAgent
{
    public async Task<IRuntimeAgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = await agent.CreateSessionAsync(cancellationToken);
        return new MicrosoftAgentRuntimeSession(session);
    }

    public async Task<string> SerializeSessionAsync(
        IRuntimeAgentSession session,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
    {
        var serialized = await agent.SerializeSessionAsync(
            UnwrapSession(session),
            serializerOptions,
            cancellationToken);

        return serialized.GetRawText();
    }

    public async Task<IRuntimeAgentSession> DeserializeSessionAsync(
        string serializedSessionState,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(serializedSessionState);
        var session = await agent.DeserializeSessionAsync(
            document.RootElement,
            serializerOptions,
            cancellationToken);

        return new MicrosoftAgentRuntimeSession(session);
    }

    public async Task<StructuredAgentResponse<T>> RunStructuredAsync<T>(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession? session,
        JsonSerializerOptions serializerOptions,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await agent.RunAsync<T>(
            messages.Select(RuntimeContentMapper.ToChatMessage),
            session is null ? null : UnwrapSession(session),
            serializerOptions,
            ToAgentRunOptions(options),
            cancellationToken);

        return new StructuredAgentResponse<T>(response.Text, response.Result);
    }

    public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in agent.RunStreamingAsync(
                           messages.Select(RuntimeContentMapper.ToChatMessage),
                           UnwrapSession(session),
                           ToAgentRunOptions(options),
                           cancellationToken))
        {
            yield return new RuntimeAgentUpdate(RuntimeContentMapper.ToRuntimeContents(update.Contents));
        }
    }

    public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        string input,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in agent.RunStreamingAsync(
                           input,
                           UnwrapSession(session),
                           ToAgentRunOptions(options),
                           cancellationToken))
        {
            yield return new RuntimeAgentUpdate(RuntimeContentMapper.ToRuntimeContents(update.Contents));
        }
    }

    private static ChatClientAgentRunOptions? ToAgentRunOptions(RuntimeAgentRunOptions? options)
    {
        return options is null
            ? null
            : new ChatClientAgentRunOptions
            {
                ChatOptions = RuntimeToolAdapter.ToChatOptions(options.Options)
            };
    }

    private static AgentSession UnwrapSession(IRuntimeAgentSession session)
    {
        return session is MicrosoftAgentRuntimeSession runtimeSession
            ? runtimeSession.Session
            : throw new InvalidOperationException($"Unsupported runtime agent session type '{session.GetType().FullName}'.");
    }
}
