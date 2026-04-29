using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.Safety;
using MediatR;
using Microsoft.Extensions.Logging;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed record IntentRoutingStepResult(
    List<IntentResult> Intents,
    ManufacturingSceneType Scene,
    string? ResponseText);

public class IntentRoutingExecutor(
    IMediator mediator,
    IntentRoutingAgentBuilder agentBuilder,
    IManufacturingSceneClassifier sceneClassifier,
    ILogger<IntentRoutingExecutor> logger)
{
    public const string ExecutorId = nameof(IntentRoutingExecutor);

    public async Task<IntentRoutingStepResult> ExecuteAsync(
        ChatStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting intent routing for session {SessionId}.", request.SessionId);

        var sceneDecision = sceneClassifier.Classify(request.Message);

        var result = await mediator.Send(new GetListChatMessagesQuery(request.SessionId, 4), cancellationToken);
        var history = result.Value!;
        history.Add(new AiChatMessage(AiChatRole.User, request.Message));

        await using var scopedAgent = await agentBuilder.BuildAsync();
        var response = await scopedAgent.Agent.RunStructuredAsync<List<IntentResult>>(
            history,
            null,
            JsonSerializerOptions.Web,
            null,
            cancellationToken);

        logger.LogDebug("Intent routing raw response: {ResponseText}", response.Text);
        logger.LogInformation("Manufacturing scene classified as {Scene} for session {SessionId}.", sceneDecision.Scene, request.SessionId);

        var intentResults = response.Result;
        if (intentResults == null || intentResults.Count == 0)
        {
            logger.LogWarning("Intent routing returned no structured result. Falling back to General.Chat.");
            intentResults = [new IntentResult { Intent = "General.Chat", Confidence = 1.0, Reasoning = "structured result is empty" }];
        }

        return new IntentRoutingStepResult(intentResults, sceneDecision.Scene, response.Text);
    }
}
