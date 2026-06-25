using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;

public sealed class ChatRuntimeSettings : BaseEntity<ChatRuntimeSettingsId>, IAggregateRoot<ChatRuntimeSettingsId>
{
    public static readonly ChatRuntimeSettingsId GlobalId = new(new Guid("11111111-1111-4111-8111-111111111111"));

    private ChatRuntimeSettings()
    {
    }

    public ChatRuntimeSettings(
        int routingHistoryCount,
        int answerHistoryCount,
        int ragRewriteHistoryCount,
        int agentPlanningHistoryCount,
        int contextTokenLimit,
        DateTimeOffset nowUtc)
    {
        Id = GlobalId;
        Update(
            routingHistoryCount,
            answerHistoryCount,
            ragRewriteHistoryCount,
            agentPlanningHistoryCount,
            contextTokenLimit,
            nowUtc);
        CreatedAt = nowUtc;
    }

    public int RoutingHistoryCount { get; private set; }

    public int AnswerHistoryCount { get; private set; }

    public int RagRewriteHistoryCount { get; private set; }

    public int AgentPlanningHistoryCount { get; private set; }

    public int ContextTokenLimit { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChatRuntimeSettings CreateDefault(DateTimeOffset nowUtc)
    {
        return new ChatRuntimeSettings(
            routingHistoryCount: 4,
            answerHistoryCount: 10,
            ragRewriteHistoryCount: 4,
            agentPlanningHistoryCount: 6,
            contextTokenLimit: 24000,
            nowUtc);
    }

    public void Update(
        int routingHistoryCount,
        int answerHistoryCount,
        int ragRewriteHistoryCount,
        int agentPlanningHistoryCount,
        int contextTokenLimit,
        DateTimeOffset nowUtc)
    {
        RoutingHistoryCount = Clamp(routingHistoryCount, 0, 20);
        AnswerHistoryCount = Clamp(answerHistoryCount, 0, 50);
        RagRewriteHistoryCount = Clamp(ragRewriteHistoryCount, 0, 20);
        AgentPlanningHistoryCount = Clamp(agentPlanningHistoryCount, 0, 30);
        ContextTokenLimit = Clamp(contextTokenLimit, 4000, 200000);
        UpdatedAt = nowUtc;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}
