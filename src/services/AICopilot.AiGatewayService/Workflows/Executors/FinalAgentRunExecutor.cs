using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Observability;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public class FinalAgentRunExecutor(
    ILogger<FinalAgentRunExecutor> logger,
    IAuditLogWriter auditLogWriter,
    ITextTokenEstimator tokenEstimator,
    IChatTokenTelemetry chatTokenTelemetry,
    ToolExecutionAuditRecorder toolExecutionAuditRecorder,
    IAgentStreamRuntime chatStreamRuntime)
{
    public const string ExecutorId = nameof(FinalAgentRunExecutor);
    private static readonly TimeSpan ModelResponseTimeout = TimeSpan.FromSeconds(90);

    public async IAsyncEnumerable<ChatChunk> ExecuteAsync(
        FinalAgentContext agentContext,
        SessionRuntimeSnapshot? session,
        StringBuilder assistantText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<AiChatMessage> messages = [];
        var finalAssistantText = new StringBuilder();
        AiUsageDetails? observedUsage = null;
        var grantedToolExecutions = new Dictionary<string, GrantedToolExecution>(StringComparer.Ordinal);
        var suspendForApproval = false;
        var thinkTagFilter = new StreamingThinkTagFilter();

        var isApprovalResumption = agentContext.FunctionApprovalRequestContents.Count != 0
                                   && agentContext.ApprovalDecisions.Count != 0;

        if (isApprovalResumption)
        {
            logger.LogInformation("Detected approval decision resumption for session {SessionId}.", agentContext.SessionId);

            foreach (var decision in agentContext.ApprovalDecisions)
            {
                var requestContent = agentContext.FunctionApprovalRequestContents
                    .FirstOrDefault(item => item.ToolCall.CallId == decision.CallId);

                if (requestContent == null)
                {
                    logger.LogWarning("Approval decision callId {CallId} no longer matches any pending tool approval request.", decision.CallId);
                    continue;
                }

                var response = new AiToolApprovalResponseContent(requestContent, decision.IsApproved);
                var toolName = FormatToolName(requestContent.ToolCall);
                messages.Add(new AiChatMessage(AiChatRole.User, [response]));

                if (decision.IsApproved)
                {
                    grantedToolExecutions[requestContent.ToolCall.CallId] = new GrantedToolExecution(
                        toolName,
                        requestContent.ToolCall.Identity);
                }

                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        AuditActionGroups.Approval,
                        decision.IsApproved ? "Approval.Approve" : "Approval.Reject",
                        "ToolApproval",
                        requestContent.ToolCall.CallId,
                        toolName,
                        decision.IsApproved ? AuditResults.Succeeded : AuditResults.Rejected,
                        decision.IsApproved
                            ? $"Approval accepted: {toolName}; onsiteConfirmed={decision.OnsiteConfirmed}."
                            : $"Approval rejected: {toolName}."),
                    cancellationToken);
                await auditLogWriter.SaveChangesAsync(cancellationToken);

                agentContext.FunctionApprovalRequestContents.Remove(requestContent);
            }

            agentContext.ApprovalDecisions.Clear();
        }
        else
        {
            if (agentContext.InputMessages.Count == 0)
            {
                messages.Add(new AiChatMessage(AiChatRole.User, agentContext.InputText));
            }
            else
            {
                messages.AddRange(agentContext.InputMessages);
            }
        }

        using var modelResponseTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var updateEnumerator = agentContext.Agent.RunStreamingAsync(
            messages,
            agentContext.Thread,
            agentContext.RunOptions,
            modelResponseTimeoutCts.Token).GetAsyncEnumerator(modelResponseTimeoutCts.Token);

        while (await MoveNextModelUpdateAsync(
                   updateEnumerator,
                   modelResponseTimeoutCts,
                   cancellationToken,
                   ModelResponseTimeout))
        {
            var update = updateEnumerator.Current;
            var safeContents = new List<AiRuntimeContent>();
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case AiTextContent textContent:
                        finalAssistantText.Append(textContent.Text);
                        safeContents.Add(content);
                        break;

                    case AiToolCallContent toolCallContent:
                        var grantedCallTool = FindGrantedTool(agentContext, toolCallContent.ToolCall);
                        if (grantedCallTool is null)
                        {
                            logger.LogWarning(
                                "Agent requested unauthorized tool call {ToolName} for session {SessionId}.",
                                toolCallContent.ToolCall.Name,
                                agentContext.SessionId);
                            yield return CreateUnauthorizedToolChunk(toolCallContent.ToolCall.Name);
                            break;
                        }

                        var enrichedToolCall = EnrichToolCall(toolCallContent.ToolCall, grantedCallTool);
                        grantedToolExecutions[enrichedToolCall.CallId] = new GrantedToolExecution(
                            FormatToolName(enrichedToolCall),
                            enrichedToolCall.Identity);
                        safeContents.Add(new AiToolCallContent(enrichedToolCall));
                        break;

                    case AiToolApprovalRequestContent requestContent:
                        var grantedApprovalTool = FindGrantedTool(agentContext, requestContent.Request.ToolCall);
                        if (grantedApprovalTool is null)
                        {
                            logger.LogWarning(
                                "Agent requested approval for unauthorized tool {ToolName} in session {SessionId}.",
                                requestContent.Request.ToolCall.Name,
                                agentContext.SessionId);
                            yield return CreateUnauthorizedToolChunk(requestContent.Request.ToolCall.Name);
                            break;
                        }

                        logger.LogInformation(
                            "Agent requested approval for tool {ToolName}.",
                            requestContent.Request.ToolCall.Name);

                        var enrichedToolApprovalCall = EnrichToolCall(requestContent.Request.ToolCall, grantedApprovalTool);
                        var enrichedRequest = new AiToolApprovalRequest(
                            requestContent.Request.RequestId,
                            enrichedToolApprovalCall);
                        agentContext.FunctionApprovalRequestContents.Add(enrichedRequest);
                        safeContents.Add(new AiToolApprovalRequestContent(enrichedRequest));
                        break;

                    case AiFunctionResultContent toolResult:
                        if (grantedToolExecutions.TryGetValue(toolResult.CallId, out var grantedToolExecution))
                        {
                            await toolExecutionAuditRecorder.RecordResultAsync(
                                toolResult,
                                grantedToolExecution.ToolName,
                                grantedToolExecution.Identity,
                                cancellationToken);
                        }
                        else
                        {
                            logger.LogWarning(
                                "Runtime returned a tool result for unknown callId {CallId} in session {SessionId}.",
                                toolResult.CallId,
                                agentContext.SessionId);
                        }

                        safeContents.Add(content);
                        break;

                    case AiUsageContent runtimeUsage:
                        observedUsage ??= new AiUsageDetails();
                        observedUsage.Add(runtimeUsage.Details);
                        safeContents.Add(content);
                        break;

                    default:
                        safeContents.Add(content);
                        break;
                }
            }

            if (safeContents.Count == 0)
            {
                continue;
            }

            await foreach (var chunk in chatStreamRuntime.CreateUpdateChunksAsync(
                               new RuntimeAgentUpdate(safeContents),
                               ExecutorId,
                               session,
                               assistantText,
                               appendAssistantText: true,
                               cancellationToken,
                               thinkTagFilter))
            {
                yield return chunk;
            }

            if (!isApprovalResumption && agentContext.FunctionApprovalRequestContents.Count > 0)
            {
                suspendForApproval = true;
                break;
            }
        }

        var cleanRemainder = thinkTagFilter.Flush();
        if (!string.IsNullOrEmpty(cleanRemainder))
        {
            assistantText.Append(cleanRemainder);
            yield return new ChatChunk(ExecutorId, ChunkType.Text, cleanRemainder);
        }

        if (suspendForApproval)
        {
            logger.LogInformation(
                "Suspended final agent run for tool approval in session {SessionId}.",
                agentContext.SessionId);
        }

        var usage = HasUsage(observedUsage)
            ? observedUsage!
            : BuildEstimatedUsage(agentContext, finalAssistantText.ToString(), isApprovalResumption, tokenEstimator);

        if (HasUsage(usage))
        {
            var estimatedInputTokens = isApprovalResumption
                ? agentContext.EstimatedInputTokens
                : agentContext.EstimatedInputTokens;

            chatTokenTelemetry.RecordUsage(
                agentContext.TokenTelemetryContext,
                usage,
                estimatedInputTokens,
                !HasUsage(observedUsage));
        }
    }

    private static AiToolDefinition? FindGrantedTool(FinalAgentContext agentContext, AiToolCall toolCall)
    {
        var grantedTools = agentContext.RunOptions.Options.Tools;
        if (grantedTools.Count == 0)
        {
            return null;
        }

        var runtimeNameMatch = grantedTools.FirstOrDefault(tool =>
            string.Equals(tool.Name, toolCall.Name, StringComparison.OrdinalIgnoreCase));
        if (runtimeNameMatch is not null)
        {
            return runtimeNameMatch;
        }

        var requestedIdentity = toolCall.Identity;
        if (requestedIdentity is not null)
        {
            var identityMatch = grantedTools.FirstOrDefault(tool =>
            {
                var grantedIdentity = tool.Identity;
                return grantedIdentity is not null
                       && grantedIdentity.Kind == requestedIdentity.Kind
                       && grantedIdentity.TargetType == requestedIdentity.TargetType
                       && string.Equals(grantedIdentity.TargetName, requestedIdentity.TargetName, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(grantedIdentity.ToolName, requestedIdentity.ToolName, StringComparison.OrdinalIgnoreCase);
            });
            if (identityMatch is not null)
            {
                return identityMatch;
            }
        }

        return FindUniqueGrantedToolByRawName(grantedTools, toolCall.ToolName ?? toolCall.Name);
    }

    private static AiToolDefinition? FindUniqueGrantedToolByRawName(
        IReadOnlyList<AiToolDefinition> grantedTools,
        string rawToolName)
    {
        var matches = grantedTools
            .Where(tool => ToolHasRawName(tool, rawToolName))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool ToolHasRawName(AiToolDefinition tool, string rawToolName)
    {
        return string.Equals(tool.ToolName, rawToolName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(tool.Identity?.ToolName, rawToolName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(tool.Method?.Name, rawToolName, StringComparison.OrdinalIgnoreCase);
    }

    private static AiToolCall EnrichToolCall(AiToolCall toolCall, AiToolDefinition grantedTool)
    {
        var identity = grantedTool.Identity;
        if (identity is null)
        {
            return toolCall;
        }

        return new AiToolCall(
            toolCall.CallId,
            toolCall.Name,
            identity.Kind,
            toolCall.ServerName ?? grantedTool.ServerName,
            toolCall.Arguments,
            identity.TargetType,
            identity.TargetName,
            identity.ToolName);
    }

    private static string FormatToolName(AiToolCall toolCall)
    {
        var identity = toolCall.Identity;
        return identity is null
            ? toolCall.ToolName ?? toolCall.Name
            : $"{identity.TargetType}:{identity.TargetName}/{identity.ToolName}";
    }

    private static ChatChunk CreateUnauthorizedToolChunk(string toolName)
    {
        return AgentStreamRuntime.CreateErrorChunk(
            AppProblemCodes.CapabilityNotAllowed,
            $"AI runtime requested unauthorized tool '{toolName}'.",
            ExecutorId,
            "模型请求了未授权工具，系统已拒绝执行。");
    }

    private static bool HasUsage(AiUsageDetails? usage)
    {
        return usage is not null
               && (usage.InputTokenCount > 0
                   || usage.OutputTokenCount > 0
                   || usage.TotalTokenCount > 0);
    }

    internal static async Task<bool> MoveNextModelUpdateAsync(
        IAsyncEnumerator<RuntimeAgentUpdate> updateEnumerator,
        CancellationTokenSource modelResponseTimeoutCts,
        CancellationToken cancellationToken,
        TimeSpan modelResponseTimeout)
    {
        modelResponseTimeoutCts.CancelAfter(modelResponseTimeout);
        try
        {
            return await updateEnumerator
                .MoveNextAsync()
                .AsTask()
                .WaitAsync(modelResponseTimeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                 && modelResponseTimeoutCts.IsCancellationRequested)
        {
            throw new AgentWorkflowException(
                AppProblemCodes.ModelRequestTimeout,
                $"Model provider did not produce the next response chunk within {modelResponseTimeout.TotalSeconds:N0} seconds.",
                "模型响应超时，请稍后重试或缩小问题范围。");
        }
        finally
        {
            if (!modelResponseTimeoutCts.IsCancellationRequested)
            {
                modelResponseTimeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static AiUsageDetails BuildEstimatedUsage(
        FinalAgentContext agentContext,
        string assistantText,
        bool isApprovalResumption,
        ITextTokenEstimator tokenEstimator)
    {
        var estimatedInputTokens = isApprovalResumption
            ? agentContext.EstimatedInputTokens
            : agentContext.EstimatedInputTokens;
        var estimatedOutputTokens = tokenEstimator.CountTokens(assistantText);

        return new AiUsageDetails
        {
            InputTokenCount = estimatedInputTokens,
            OutputTokenCount = estimatedOutputTokens,
            TotalTokenCount = estimatedInputTokens + estimatedOutputTokens
        };
    }

    private sealed record GrantedToolExecution(string ToolName, AiToolIdentity? Identity);
}
