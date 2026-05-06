using AICopilot.AiGatewayService.Safety;
using SharpToken;

namespace AICopilot.Infrastructure.AiGateway;

public sealed class SharpTokenTextTokenEstimator : ITextTokenEstimator
{
    private readonly GptEncoding encoding = GptEncoding.GetEncoding("cl100k_base");

    public int CountTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return encoding.Encode(text).Count;
    }
}
