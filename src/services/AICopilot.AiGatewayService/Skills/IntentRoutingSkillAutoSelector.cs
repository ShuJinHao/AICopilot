using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows.Executors;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Skills;

public interface IAgentSkillAutoSelector
{
    Task<string?> SelectSkillCodeAsync(Guid sessionId, string goal, CancellationToken cancellationToken);
}

public sealed class IntentRoutingSkillAutoSelector(
    IntentRoutingExecutor intentRoutingExecutor,
    ILogger<IntentRoutingSkillAutoSelector> logger) : IAgentSkillAutoSelector
{
    public async Task<string?> SelectSkillCodeAsync(Guid sessionId, string goal, CancellationToken cancellationToken)
    {
        var routing = await intentRoutingExecutor.ExecuteAsync(
            new ChatStreamRequest(sessionId, goal),
            cancellationToken);
        var selected = SelectBestSkillCode(routing.Intents);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            logger.LogInformation("Intent routing auto-selected skill {SkillCode} for session {SessionId}.", selected, sessionId);
        }

        return selected;
    }

    internal static string? SelectBestSkillCode(IEnumerable<IntentResult> intents)
    {
        return intents
            .Where(intent => intent.Intent.StartsWith("Skill.", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(intent => intent.Confidence)
            .Select(intent => intent.Intent["Skill.".Length..].Trim())
            .FirstOrDefault(skillCode => !string.IsNullOrWhiteSpace(skillCode));
    }
}
