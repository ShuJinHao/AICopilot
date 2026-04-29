using System.Diagnostics;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Observability;

public interface IChatTokenTelemetry
{
    void RecordUsage(
        ChatTokenTelemetryContext context,
        AiUsageDetails usage,
        int estimatedInputTokens,
        bool isEstimated);
}

public sealed class ChatTokenTelemetry(ILogger<ChatTokenTelemetry> logger) : IChatTokenTelemetry
{
    public void RecordUsage(
        ChatTokenTelemetryContext context,
        AiUsageDetails usage,
        int estimatedInputTokens,
        bool isEstimated)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(usage);

        var activity = Activity.Current;
        activity?.SetTag("ai.session.id", context.SessionId?.ToString());
        activity?.SetTag("ai.model.name", context.ModelName);
        activity?.SetTag("ai.template.name", context.TemplateName);
        activity?.SetTag("ai.token.input", usage.InputTokenCount);
        activity?.SetTag("ai.token.output", usage.OutputTokenCount);
        activity?.SetTag("ai.token.total", usage.TotalTokenCount);
        activity?.SetTag("ai.token.cached_input", usage.CachedInputTokenCount);
        activity?.SetTag("ai.token.reasoning", usage.ReasoningTokenCount);
        activity?.SetTag("ai.token.estimated_input", estimatedInputTokens);
        activity?.SetTag("ai.token.is_estimated", isEstimated);
        activity?.SetTag("ai.token.budget.total", context.TotalTokenBudget);
        activity?.SetTag("ai.token.budget.reserved_output", context.ReservedOutputTokens);

        logger.LogInformation(
            "AI token usage recorded. SessionId: {SessionId}, Model: {ModelName}, Template: {TemplateName}, InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}, CachedInputTokens: {CachedInputTokens}, ReasoningTokens: {ReasoningTokens}, EstimatedInputTokens: {EstimatedInputTokens}, BudgetTotal: {BudgetTotal}, ReservedOutputTokens: {ReservedOutputTokens}, Estimated: {Estimated}",
            context.SessionId,
            context.ModelName,
            context.TemplateName,
            usage.InputTokenCount,
            usage.OutputTokenCount,
            usage.TotalTokenCount,
            usage.CachedInputTokenCount,
            usage.ReasoningTokenCount,
            estimatedInputTokens,
            context.TotalTokenBudget,
            context.ReservedOutputTokens,
            isEstimated);
    }
}
