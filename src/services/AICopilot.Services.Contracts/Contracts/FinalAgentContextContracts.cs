namespace AICopilot.Services.Contracts;

public sealed record ChatTokenTelemetryContext(
    Guid? SessionId,
    string ModelName,
    string TemplateName,
    int TotalTokenBudget,
    int ReservedOutputTokens);

public sealed record StoredToolApprovalRequest(
    string RequestId,
    string CallId,
    string ToolKind,
    string? ToolName,
    string? ServerName,
    Dictionary<string, object?> Arguments,
    string? TargetType = null,
    string? TargetName = null,
    string? RuntimeName = null);

public sealed record StoredFinalAgentContext(
    Guid SessionId,
    string InputText,
    int EstimatedInputTokens,
    int SystemPromptTokenCount,
    ChatTokenTelemetryContext TokenTelemetryContext,
    int? MaxOutputTokens,
    float? Temperature,
    IReadOnlyList<string> ToolNames,
    string SerializedThreadState,
    IReadOnlyList<StoredToolApprovalRequest> PendingApprovals);

public interface IFinalAgentContextStore
{
    Task<StoredFinalAgentContext?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task SetAsync(Guid sessionId, StoredFinalAgentContext context, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
