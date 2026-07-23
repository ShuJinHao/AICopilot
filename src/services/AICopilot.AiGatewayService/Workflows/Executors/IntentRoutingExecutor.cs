using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
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
    ChatExecutionMetadataSnapshot ExecutionMetadata)
{
    internal AgentIntentRegistrySnapshot RegistrySnapshot { get; init; } = AgentIntentRegistryV1.FrozenSnapshot;
}

internal sealed record IntentRoutingResponseLogMetadata(
    int ResponseLength,
    string ResponseSha256,
    string ResponseType,
    bool Parsed);

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

        var registry = await agentBuilder.ReadRegistrySnapshotAsync(cancellationToken);
        var responseText = await RunRoutingAsPlainJsonTextAsync(history, registry, cancellationToken);
        logger.LogInformation("Manufacturing scene classified as {Scene} for session {SessionId}.", sceneDecision.Scene, request.SessionId);

        var parsed = IntentRoutingResultParser.TryParse(responseText, out var intentResults) &&
                     AgentIntentRegistryV1.ValidateRoutedResults(registry, intentResults);
        LogResponseMetadata(logger, responseText, parsed);

        if (!parsed)
        {
            logger.LogWarning("Intent routing returned unparsable JSON. Falling back to General.Chat.");
            intentResults = CreateFallbackIntents(
                request.Message,
                "routing JSON parse or Registry validation failed",
                registry);
        }

        DeviceLogFollowUpIntentRewriter.Rewrite(intentResults, history);

        var normalizedResponseText = JsonSerializer.Serialize(intentResults, JsonSerializerOptions.Web);
        return new IntentRoutingStepResult(
            intentResults,
            sceneDecision.Scene,
            normalizedResponseText,
            executionMetadataAccessor.Snapshot())
        {
            RegistrySnapshot = registry
        };
    }

    private async Task<string?> RunRoutingAsPlainJsonTextAsync(
        List<AiChatMessage> history,
        AgentIntentRegistrySnapshot registry,
        CancellationToken cancellationToken)
    {
        var timeout = (modelCallTimeoutOptions?.Value ?? new AgentModelCallTimeoutOptions()).RoutingTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await using var scopedAgent = await agentBuilder.BuildAsync(registry);
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
            logger.LogWarning(
                "Intent routing model call failed. Falling back to General.Chat. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                ex.GetType().Name);
            return JsonSerializer.Serialize(
                CreateFallbackIntents(
                    history.LastOrDefault()?.Text,
                    "routing model call failed",
                    registry),
                JsonSerializerOptions.Web);
        }
    }

    private static List<IntentResult> CreateFallbackIntents(
        string? message,
        string routingNote,
        AgentIntentRegistrySnapshot registry)
    {
        if (IntentRoutingFallbackClassifier.TryClassify(
                message,
                routingNote,
                registry,
                out var semanticFallback))
        {
            return semanticFallback;
        }

        if (!registry.TryGet("General.Chat", out _))
        {
            throw new InvalidOperationException(
                "The active IntentRegistry is missing the fail-closed General.Chat fallback.");
        }

        return [new IntentResult
        {
            Intent = "General.Chat",
            Confidence = 1.0,
            RoutingNote = routingNote
        }];
    }

    internal static IntentRoutingResponseLogMetadata CreateResponseLogMetadata(string? responseText, bool parsed)
    {
        var normalized = responseText ?? string.Empty;
        var trimmed = normalized.TrimStart();
        var responseType = trimmed.Length == 0
            ? "Empty"
            : trimmed[0] switch
            {
                '[' => "JsonArray",
                '{' => "JsonObject",
                _ => "Text"
            };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();

        return new IntentRoutingResponseLogMetadata(
            normalized.Length,
            hash,
            responseType,
            parsed);
    }

    internal static void LogResponseMetadata(ILogger logger, string? responseText, bool parsed)
    {
        var metadata = CreateResponseLogMetadata(responseText, parsed);
        logger.LogDebug(
            "Intent routing response processed. ResponseLength={ResponseLength}; ResponseSha256={ResponseSha256}; ResponseType={ResponseType}; Parsed={Parsed}",
            metadata.ResponseLength,
            metadata.ResponseSha256,
            metadata.ResponseType,
            metadata.Parsed);
    }
}
