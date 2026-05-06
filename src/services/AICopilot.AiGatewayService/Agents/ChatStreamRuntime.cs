using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Agents;

internal sealed record ChatErrorChunkPayload(string? Code, string? Detail, string? UserFacingMessage);

internal static class ChatStreamRuntime
{
    private const int MaxFunctionResultPayloadBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async IAsyncEnumerable<ChatChunk> CreateUpdateChunksAsync(
        ApprovalRequirementResolver approvalRequirementResolver,
        RuntimeAgentUpdate update,
        string source,
        SessionRuntimeSnapshot? session,
        StringBuilder? assistantText,
        bool appendAssistantText,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var evtContent in update.Contents)
        {
            switch (evtContent)
            {
                case AiTextContent content:
                    if (appendAssistantText)
                    {
                        assistantText?.Append(content.Text);
                    }

                    yield return new ChatChunk(source, ChunkType.Text, content.Text);
                    break;

                case AiToolCallContent content:
                    var functionCall = new
                    {
                        id = content.ToolCall.CallId,
                        name = content.ToolCall.Name,
                        args = content.ToolCall.Arguments
                    };
                    yield return new ChatChunk(source, ChunkType.FunctionCall, functionCall.ToJson());
                    break;

                case AiFunctionResultContent content:
                    var result = new
                    {
                        id = content.CallId,
                        result = content.Result
                    };
                    yield return new ChatChunk(
                        source,
                        ChunkType.FunctionResult,
                        LimitFunctionResultPayload(result.ToJson()));
                    break;

                case AiToolApprovalRequestContent content:
                    var toolCall = content.Request.ToolCall;
                    var identity = toolCall.Identity;
                    var toolName = identity?.ToolName ?? toolCall.ToolName ?? toolCall.Name;
                    var requirement = await approvalRequirementResolver.GetMergedRequirementByIdentityAsync(identity, ct);
                    var approval = new
                    {
                        callId = toolCall.CallId,
                        name = toolName,
                        runtimeName = toolCall.Name,
                        targetType = identity?.TargetType.ToString(),
                        targetName = identity?.TargetName,
                        toolName,
                        args = toolCall.Arguments,
                        requiresOnsiteAttestation = requirement.RequiresOnsiteAttestation,
                        attestationExpiresAt = session?.OnsiteConfirmationExpiresAt
                    };
                    yield return new ChatChunk(source, ChunkType.ApprovalRequest, approval.ToJson());
                    break;
            }
        }
    }

    public static async Task<SessionRuntimeSnapshot?> LoadSessionAsync(
        IReadRepository<Session> repository,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await repository.FirstOrDefaultAsync(new SessionByIdSpec(new SessionId(sessionId)), cancellationToken);
        if (session is null)
        {
            return null;
        }

        return new SessionRuntimeSnapshot
        {
            Id = session.Id,
            UserId = session.UserId,
            Title = session.Title,
            OnsiteConfirmedAt = session.OnsiteConfirmedAt,
            OnsiteConfirmedBy = session.OnsiteConfirmedBy,
            OnsiteConfirmationExpiresAt = session.OnsiteConfirmationExpiresAt
        };
    }

    public static ChatChunk CreateErrorChunk(
        Exception exception,
        string source,
        string fallbackCode,
        string fallbackUserFacingMessage)
    {
        return exception is ChatWorkflowException workflowException
            ? CreateErrorChunk(workflowException.Code, workflowException.Detail, source, workflowException.UserFacingMessage)
            : CreateErrorChunk(
                fallbackCode,
                fallbackUserFacingMessage,
                source,
                fallbackUserFacingMessage);
    }

    public static ChatChunk CreateErrorChunk(
        StringBuilder assistantText,
        Exception exception,
        string source,
        string fallbackCode,
        string fallbackUserFacingMessage)
    {
        var errorChunk = CreateErrorChunk(exception, source, fallbackCode, fallbackUserFacingMessage);
        AppendAssistantErrorSummary(assistantText, errorChunk);
        return errorChunk;
    }

    public static ChatChunk CreateErrorChunk(
        StringBuilder assistantText,
        string code,
        string detail,
        string source = "Chat",
        string? userFacingMessage = null)
    {
        var errorChunk = CreateErrorChunk(code, detail, source, userFacingMessage);
        AppendAssistantErrorSummary(assistantText, errorChunk);
        return errorChunk;
    }

    public static ChatChunk CreateErrorChunk(
        string code,
        string detail,
        string source = "Chat",
        string? userFacingMessage = null)
    {
        return new ChatChunk(
            source,
            ChunkType.Error,
            new
            {
                code,
                detail,
                userFacingMessage
            }.ToJson());
    }

    public static void AppendAssistantErrorSummary(StringBuilder assistantText, ChatChunk errorChunk)
    {
        var payload = JsonSerializer.Deserialize<ChatErrorChunkPayload>(errorChunk.Content, JsonOptions);
        var message = !string.IsNullOrWhiteSpace(payload?.UserFacingMessage)
            ? payload!.UserFacingMessage
            : payload?.Detail;
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (assistantText.Length > 0)
        {
            assistantText.AppendLine();
        }

        assistantText.Append(message.Trim());
    }

    public static string BuildApprovalSummary(
        string toolName,
        bool isApproved,
        bool onsiteConfirmed,
        bool requiresOnsiteAttestation)
    {
        if (!isApproved)
        {
            return $"[审批拒绝] {toolName}";
        }

        if (!requiresOnsiteAttestation)
        {
            return $"[审批通过] {toolName}";
        }

        return onsiteConfirmed
            ? $"[审批通过] {toolName}（已再次确认现场有人在岗）"
            : $"[审批通过] {toolName}";
    }

    private static string LimitFunctionResultPayload(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) <= MaxFunctionResultPayloadBytes)
        {
            return payload;
        }

        var preview = BuildUtf8Preview(payload, MaxFunctionResultPayloadBytes / 2);
        return new
        {
            truncated = true,
            detail = "函数结果过大，已截断为预览内容。",
            preview
        }.ToJson();
    }

    private static string BuildUtf8Preview(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var currentBytes = 0;
        foreach (var ch in value)
        {
            var charBytes = Encoding.UTF8.GetByteCount(ch.ToString());
            if (currentBytes + charBytes > maxBytes)
            {
                break;
            }

            builder.Append(ch);
            currentBytes += charBytes;
        }

        return builder.ToString();
    }
}
