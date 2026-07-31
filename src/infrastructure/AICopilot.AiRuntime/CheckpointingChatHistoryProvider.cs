using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AICopilot.AiRuntime;

internal sealed class CheckpointingChatHistoryProvider(
    Func<AgentSession, CancellationToken, ValueTask>? checkpointAsync) : ChatHistoryProvider
{
    private readonly ProviderSessionState<InMemoryChatHistoryProvider.State> sessionState =
        new(
            _ => new InMemoryChatHistoryProvider.State(),
            nameof(CheckpointingChatHistoryProvider));

    public override IReadOnlyList<string> StateKeys => [sessionState.StateKey];

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IEnumerable<ChatMessage>>(
            sessionState.GetOrInitializeState(context.Session).Messages);
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var state = sessionState.GetOrInitializeState(context.Session);
        state.Messages.AddRange(
            (context.RequestMessages ?? []).Concat(context.ResponseMessages ?? []));

        if (checkpointAsync is not null && context.Session is not null)
        {
            await checkpointAsync(context.Session, cancellationToken);
        }
    }
}
