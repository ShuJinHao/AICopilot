using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using SharpToken;

namespace AICopilot.AiGatewayService.Safety;

public interface ITextTokenEstimator
{
    int CountTokens(string? text);
}

public sealed class SharpTokenTextTokenEstimator : ITextTokenEstimator
{
    private readonly GptEncoding _encoding = GptEncoding.GetEncoding("cl100k_base");

    public int CountTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return _encoding.Encode(text).Count;
    }
}

public sealed record TokenBudgetDecision(
    bool IsAllowed,
    int EstimatedInputTokens,
    int ReservedOutputTokens,
    int TotalTokenBudget,
    string? Detail = null,
    string? UserFacingMessage = null);

public interface ITokenBudgetPolicy
{
    int CountSystemPromptTokens(ConversationTemplate template);

    TokenBudgetDecision Evaluate(
        LanguageModel model,
        ConversationTemplate template,
        string finalUserPrompt);
}

public sealed class ChatTokenBudgetPolicy(ITextTokenEstimator tokenEstimator) : ITokenBudgetPolicy
{
    private const int DefaultReservedOutputTokens = 1024;
    private const int MinimumReservedOutputTokens = 256;

    public int CountSystemPromptTokens(ConversationTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return tokenEstimator.CountTokens(template.SystemPrompt);
    }

    public TokenBudgetDecision Evaluate(
        LanguageModel model,
        ConversationTemplate template,
        string finalUserPrompt)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(template);

        var totalTokenBudget = Math.Max(model.Parameters.MaxTokens, 1);
        var reservedOutputTokens = ResolveReservedOutputTokens(totalTokenBudget, template.Specification.MaxTokens);
        var safetyBuffer = Math.Clamp(totalTokenBudget / 10, 64, 256);
        var estimatedInputTokens = CountSystemPromptTokens(template) + tokenEstimator.CountTokens(finalUserPrompt);
        var availableInputTokens = Math.Max(1, totalTokenBudget - reservedOutputTokens - safetyBuffer);

        if (estimatedInputTokens <= availableInputTokens)
        {
            return new TokenBudgetDecision(
                true,
                estimatedInputTokens,
                reservedOutputTokens,
                totalTokenBudget);
        }

        return new TokenBudgetDecision(
            false,
            estimatedInputTokens,
            reservedOutputTokens,
            totalTokenBudget,
            $"当前模型 Token 预算不足。EstimatedInputTokens={estimatedInputTokens}, ReservedOutputTokens={reservedOutputTokens}, SafetyBuffer={safetyBuffer}, TotalTokenBudget={totalTokenBudget}.",
            "本次问题连同参考信息预计会超过当前模型的 token 预算。请缩小时间范围、减少筛选条件，或分步提问后再试。");
    }

    private static int ResolveReservedOutputTokens(int totalTokenBudget, int? templateMaxTokens)
    {
        var desiredTokens = templateMaxTokens
            ?? Math.Min(DefaultReservedOutputTokens, Math.Max(MinimumReservedOutputTokens, totalTokenBudget / 3));

        return Math.Clamp(desiredTokens, MinimumReservedOutputTokens, totalTokenBudget);
    }
}
