using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Approvals;
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
    ApprovalRequirementResolver approvalRequirementResolver)
{
    public const string ExecutorId = nameof(FinalAgentRunExecutor);

    public async IAsyncEnumerable<ChatChunk> ExecuteAsync(
        FinalAgentContext agentContext,
        SessionRuntimeSnapshot? session,
        StringBuilder assistantText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<AiChatMessage> messages = [];
        var finalAssistantText = new StringBuilder();
        AiUsageDetails? observedUsage = null;

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
                var identity = requestContent.ToolCall.Identity;
                var toolName = identity is null
                    ? requestContent.ToolCall.Name
                    : $"{identity.TargetType}:{identity.TargetName}/{identity.ToolName}";
                messages.Add(new AiChatMessage(AiChatRole.User, [response]));

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
            messages.Add(new AiChatMessage(AiChatRole.User, agentContext.InputText));
        }

        await foreach (var update in agentContext.Agent.RunStreamingAsync(
                           messages,
                           agentContext.Thread,
                           agentContext.RunOptions,
                           cancellationToken))
        {
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

                        safeContents.Add(new AiToolCallContent(EnrichToolCall(toolCallContent.ToolCall, grantedCallTool)));
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

                        var enrichedRequest = new AiToolApprovalRequest(
                            requestContent.Request.RequestId,
                            EnrichToolCall(requestContent.Request.ToolCall, grantedApprovalTool));
                        agentContext.FunctionApprovalRequestContents.Add(enrichedRequest);
                        safeContents.Add(new AiToolApprovalRequestContent(enrichedRequest));
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

            await foreach (var chunk in ChatStreamRuntime.CreateUpdateChunksAsync(
                               approvalRequirementResolver,
                               new RuntimeAgentUpdate(safeContents),
                               ExecutorId,
                               session,
                               assistantText,
                               appendAssistantText: true,
                               cancellationToken))
            {
                yield return chunk;
            }
        }

        var usage = HasUsage(observedUsage)
            ? observedUsage!
            : BuildEstimatedUsage(agentContext, finalAssistantText.ToString(), isApprovalResumption, tokenEstimator);

        if (HasUsage(usage))
        {
            var estimatedInputTokens = isApprovalResumption
                ? agentContext.SystemPromptTokenCount + tokenEstimator.CountTokens(agentContext.InputText)
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

    private static ChatChunk CreateUnauthorizedToolChunk(string toolName)
    {
        return ChatStreamRuntime.CreateErrorChunk(
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

    private static AiUsageDetails BuildEstimatedUsage(
        FinalAgentContext agentContext,
        string assistantText,
        bool isApprovalResumption,
        ITextTokenEstimator tokenEstimator)
    {
        var estimatedInputTokens = isApprovalResumption
            ? agentContext.SystemPromptTokenCount + tokenEstimator.CountTokens(agentContext.InputText)
            : agentContext.EstimatedInputTokens;
        var estimatedOutputTokens = tokenEstimator.CountTokens(assistantText);

        return new AiUsageDetails
        {
            InputTokenCount = estimatedInputTokens,
            OutputTokenCount = estimatedOutputTokens,
            TotalTokenCount = estimatedInputTokens + estimatedOutputTokens
        };
    }
}
