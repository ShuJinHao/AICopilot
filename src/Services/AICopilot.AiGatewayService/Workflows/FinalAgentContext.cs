using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Workflows;

public sealed record FunctionApprovalDecision(
    string CallId,
    bool IsApproved,
    bool OnsiteConfirmed);

public class FinalAgentContext : IAsyncDisposable
{
    public required ScopedRuntimeAgent ScopedAgent { get; init; }

    public IRuntimeChatAgent Agent => ScopedAgent.Agent;

    public required IRuntimeAgentSession Thread { get; init; }

    public required string InputText { get; set; }

    public required RuntimeAgentRunOptions RunOptions { get; init; }

    public Guid SessionId { get; init; }

    public required ChatTokenTelemetryContext TokenTelemetryContext { get; init; }

    public int EstimatedInputTokens { get; set; }

    public int SystemPromptTokenCount { get; init; }

    public List<AiToolApprovalRequest> FunctionApprovalRequestContents { get; } = [];

    public List<FunctionApprovalDecision> ApprovalDecisions { get; } = [];

    public ValueTask DisposeAsync()
    {
        return ScopedAgent.DisposeAsync();
    }
}
