using System.Runtime.CompilerServices;
using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Agents.AI;

namespace AICopilot.AiRuntime;

internal sealed class HarnessRuntimeChatAgent(
    HarnessAgent agent,
    AgentModeProvider modeProvider)
    : IHarnessRuntimeChatAgent
{
    private readonly MicrosoftAgentRuntimeChatAgent inner = new(agent);

    public Task<IRuntimeAgentSession> CreateSessionAsync(
        CancellationToken cancellationToken = default) =>
        inner.CreateSessionAsync(cancellationToken);

    public Task<string> SerializeSessionAsync(
        IRuntimeAgentSession session,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default) =>
        inner.SerializeSessionAsync(session, serializerOptions, cancellationToken);

    public Task<IRuntimeAgentSession> DeserializeSessionAsync(
        string serializedSessionState,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default) =>
        inner.DeserializeSessionAsync(serializedSessionState, serializerOptions, cancellationToken);

    public async Task<RuntimeAgentMode> GetModeAsync(
        IRuntimeAgentSession session,
        CancellationToken cancellationToken = default)
    {
        var mode = await modeProvider.GetModeAsync(
            MicrosoftAgentRuntimeChatAgent.UnwrapSession(session),
            cancellationToken);
        return ParseMode(mode);
    }

    public Task SetModeAsync(
        IRuntimeAgentSession session,
        RuntimeAgentMode mode,
        CancellationToken cancellationToken = default) =>
        modeProvider.SetModeAsync(
            MicrosoftAgentRuntimeChatAgent.UnwrapSession(session),
            FormatMode(mode),
            cancellationToken);

    public Task<StructuredAgentResponse<T>> RunStructuredAsync<T>(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession? session,
        JsonSerializerOptions serializerOptions,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        RejectAlwaysApprove(materialized);
        return inner.RunStructuredAsync<T>(
            materialized,
            session,
            serializerOptions,
            options,
            cancellationToken);
    }

    public async IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        IEnumerable<AiChatMessage> messages,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        RejectAlwaysApprove(materialized);
        await foreach (var update in inner.RunStreamingAsync(
                           materialized,
                           session,
                           options,
                           cancellationToken))
        {
            yield return update;
        }
    }

    public IAsyncEnumerable<RuntimeAgentUpdate> RunStreamingAsync(
        string input,
        IRuntimeAgentSession session,
        RuntimeAgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunStreamingAsync(
            [new AiChatMessage(AiChatRole.User, input)],
            session,
            options,
            cancellationToken);

    private static void RejectAlwaysApprove(IEnumerable<AiChatMessage> messages)
    {
        // The shared runtime contract intentionally has no standing-approval
        // content. Reject unknown future approval response subtypes as well.
        if (messages.SelectMany(message => message.Contents)
            .Any(content =>
                content is AiToolApprovalResponseContent response &&
                response.GetType() != typeof(AiToolApprovalResponseContent)))
        {
            throw new HarnessAlwaysApproveRejectedException();
        }
    }

    private static RuntimeAgentMode ParseMode(string mode)
    {
        return mode switch
        {
            "plan" => RuntimeAgentMode.Plan,
            "execute" => RuntimeAgentMode.Execute,
            _ => throw new InvalidOperationException(
                $"Unsupported persisted agent mode '{mode}'.")
        };
    }

    private static string FormatMode(RuntimeAgentMode mode)
    {
        return mode switch
        {
            RuntimeAgentMode.Plan => "plan",
            RuntimeAgentMode.Execute => "execute",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
