using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Services.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public sealed record IntentRoutingStepResult(
    List<IntentResult> Intents,
    ManufacturingSceneType Scene,
    string? ResponseText,
    ChatExecutionMetadataSnapshot ExecutionMetadata);

public class IntentRoutingExecutor(
    IMediator mediator,
    IntentRoutingAgentBuilder agentBuilder,
    IManufacturingSceneClassifier sceneClassifier,
    IAgentExecutionMetadataAccessor executionMetadataAccessor,
    IAgentRuntimeSettingsProvider runtimeSettingsProvider,
    ILogger<IntentRoutingExecutor> logger,
    IOptions<AgentModelCallTimeoutOptions>? modelCallTimeoutOptions = null)
{
    public const string ExecutorId = nameof(IntentRoutingExecutor);

    public async Task<IntentRoutingStepResult> ExecuteAsync(
        ChatStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting intent routing for session {SessionId}.", request.SessionId);

        var sceneDecision = sceneClassifier.Classify(request.Message);
        var runtimeSettings = await runtimeSettingsProvider.GetAsync(cancellationToken);

        var result = await mediator.Send(
            new GetListChatMessagesQuery(request.SessionId, runtimeSettings.RoutingHistoryCount),
            cancellationToken);
        var history = result.Value!;
        history.Add(new AiChatMessage(AiChatRole.User, request.Message));

        var responseText = await RunRoutingAsPlainJsonTextAsync(history, cancellationToken);
        logger.LogDebug("Intent routing raw response: {ResponseText}", responseText);
        logger.LogInformation("Manufacturing scene classified as {Scene} for session {SessionId}.", sceneDecision.Scene, request.SessionId);

        if (!IntentRoutingResultParser.TryParse(responseText, out var intentResults))
        {
            logger.LogWarning("Intent routing returned unparsable JSON. Falling back to General.Chat.");
            intentResults = CreateFallbackIntents(request.Message, "routing JSON parse failed");
        }

        var normalizedResponseText = JsonSerializer.Serialize(intentResults, JsonSerializerOptions.Web);
        return new IntentRoutingStepResult(
            intentResults,
            sceneDecision.Scene,
            normalizedResponseText,
            executionMetadataAccessor.Snapshot());
    }

    private async Task<string?> RunRoutingAsPlainJsonTextAsync(
        List<AiChatMessage> history,
        CancellationToken cancellationToken)
    {
        var timeout = (modelCallTimeoutOptions?.Value ?? new AgentModelCallTimeoutOptions()).RoutingTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await using var scopedAgent = await agentBuilder.BuildAsync();
            var timeoutToken = timeoutCts.Token;
            var session = await scopedAgent.Agent.CreateSessionAsync(timeoutToken);
            var builder = new StringBuilder();
            var thinkTagFilter = new StreamingThinkTagFilter();
            await foreach (var update in scopedAgent.Agent.RunStreamingAsync(
                    history,
                    session,
                    new RuntimeAgentRunOptions(new AiChatOptions
                    {
                        MaxOutputTokens = 512,
                        Temperature = 0,
                        Tools = []
                    }),
                    timeoutToken))
            {
                foreach (var content in update.Contents.OfType<AiTextContent>())
                {
                    builder.Append(thinkTagFilter.Append(content.Text));
                }
            }

            builder.Append(thinkTagFilter.Flush());
            return builder.ToString();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                 && timeoutCts.IsCancellationRequested)
        {
            throw new AgentWorkflowException(
                AppProblemCodes.ModelRequestTimeout,
                $"Intent routing model call exceeded {timeout.TotalSeconds:N0} seconds.",
                "意图识别模型响应超时，请稍后重试。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Intent routing model call failed. Falling back to General.Chat.");
            return JsonSerializer.Serialize(
                CreateFallbackIntents(history.LastOrDefault()?.Text, "routing model call failed"),
                JsonSerializerOptions.Web);
        }
    }

    private static List<IntentResult> CreateFallbackIntents(string? message, string reasoning)
    {
        if (IntentRoutingFallbackClassifier.TryClassify(message, reasoning, out var semanticFallback))
        {
            return semanticFallback;
        }

        return [new IntentResult { Intent = "General.Chat", Confidence = 1.0, Reasoning = reasoning }];
    }
}
