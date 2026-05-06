using System.Runtime.CompilerServices;
using System.Text;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Observability;
using AICopilot.AiGatewayService.Safety;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
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
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case AiTextContent textContent:
                        finalAssistantText.Append(textContent.Text);
                        break;

                    case AiToolApprovalRequestContent requestContent:
                        logger.LogInformation(
                            "Agent requested approval for tool {ToolName}.",
                            requestContent.Request.ToolCall.Name);
                        agentContext.FunctionApprovalRequestContents.Add(requestContent.Request);
                        break;

                    case AiUsageContent runtimeUsage:
                        observedUsage ??= new AiUsageDetails();
                        observedUsage.Add(runtimeUsage.Details);
                        break;
                }
            }

            await foreach (var chunk in ChatStreamRuntime.CreateUpdateChunksAsync(
                               approvalRequirementResolver,
                               update,
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
