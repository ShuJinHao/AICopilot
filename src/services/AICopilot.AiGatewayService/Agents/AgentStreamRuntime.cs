using System.Runtime.CompilerServices;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Agents;

internal sealed record ChatErrorChunkPayload(string? Code, string? Detail, string? UserFacingMessage);

public interface IAgentStreamRuntime
{
    IAsyncEnumerable<ChatChunk> CreateUpdateChunksAsync(
        RuntimeAgentUpdate update,
        string source,
        SessionRuntimeSnapshot? session,
        StringBuilder? assistantText,
        bool appendAssistantText,
        CancellationToken ct,
        StreamingThinkTagFilter? thinkTagFilter = null);

    Task<SessionRuntimeSnapshot?> LoadSessionAsync(
        IReadRepository<Session> repository,
        Guid sessionId,
        CancellationToken cancellationToken);
}

public sealed class AgentStreamRuntime(ApprovalRequirementResolver approvalRequirementResolver) : IAgentStreamRuntime
{
    private const int MaxFunctionResultPayloadBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex SensitiveAssignmentRegex = new(
        @"(?i)\b(api[-_ ]?key|authorization|bearer|token|secret|password|pwd|connection\s*string)\b\s*[:=]\s*[^;\s,}\]]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ConnectionSegmentRegex = new(
        @"(?i)\b(password|pwd|user id|uid|host|server|database|data source)\s*=\s*[^;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InternalPathRegex = new(
        @"(/Users/[^\s]+|[A-Za-z]:\\[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StackTraceRegex = new(
        @"(?m)^\s*at\s+.+\(.+\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SqlStatementRegex = new(
        @"(?is)\b(select|insert|update|delete|merge|drop|alter|create)\b.+\b(from|into|table|where|values)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async IAsyncEnumerable<ChatChunk> CreateUpdateChunksAsync(
        RuntimeAgentUpdate update,
        string source,
        SessionRuntimeSnapshot? session,
        StringBuilder? assistantText,
        bool appendAssistantText,
        [EnumeratorCancellation] CancellationToken ct,
        StreamingThinkTagFilter? thinkTagFilter = null)
    {
        foreach (var evtContent in update.Contents)
        {
            switch (evtContent)
            {
                case AiTextContent content:
                    var cleanText = thinkTagFilter is null
                        ? ModelOutputSanitizer.Strip(content.Text).CleanText
                        : thinkTagFilter.Append(content.Text);
                    if (string.IsNullOrEmpty(cleanText))
                    {
                        break;
                    }

                    if (appendAssistantText)
                    {
                        assistantText?.Append(cleanText);
                    }

                    yield return new ChatChunk(source, ChunkType.Text, cleanText);
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

    public async Task<SessionRuntimeSnapshot?> LoadSessionAsync(
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
        if (exception is AgentWorkflowException workflowException)
        {
            return CreateErrorChunk(
                workflowException.Code,
                BuildSafeWorkflowDetail(workflowException.Code),
                source,
                workflowException.UserFacingMessage);
        }

        if (exception is TimeoutException or TaskCanceledException or OperationCanceledException)
        {
            return CreateErrorChunk(
                AppProblemCodes.ModelRequestTimeout,
                "Model provider did not return a response before the configured timeout.",
                source,
                "模型响应超时，请稍后重试或缩小问题范围。");
        }

        if (exception is HttpRequestException)
        {
            return CreateErrorChunk(
                AppProblemCodes.ModelProviderUnavailable,
                "Model provider request failed before a response could be completed.",
                source,
                "模型服务暂时不可用，请稍后重试或联系管理员检查模型网络。");
        }

        return CreateErrorChunk(
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
                detail = SanitizeErrorText(detail),
                userFacingMessage = userFacingMessage is null ? null : SanitizeErrorText(userFacingMessage)
            }.ToJson());
    }

    public static ChatChunk? CreateMetadataChunk(
        ChatExecutionMetadataSnapshot snapshot,
        string source = "Chat")
    {
        if (!snapshot.FinalModelId.HasValue
            && string.IsNullOrWhiteSpace(snapshot.FinalModelName)
            && !snapshot.RoutingModelId.HasValue
            && string.IsNullOrWhiteSpace(snapshot.RoutingModelName)
            && !snapshot.ContextWindowTokens.HasValue
            && !snapshot.MaxOutputTokens.HasValue)
        {
            return null;
        }

        return new ChatChunk(
            source,
            ChunkType.Metadata,
            new ChatModelMetadataPayload(
                snapshot.FinalModelId,
                snapshot.FinalModelName,
                snapshot.RoutingModelId,
                snapshot.RoutingModelName,
                snapshot.ContextWindowTokens,
                snapshot.MaxOutputTokens).ToJson());
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

    private static string SanitizeErrorText(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var sanitized = detail.Trim();
        if (StackTraceRegex.IsMatch(sanitized))
        {
            return "错误详情包含内部堆栈信息，已按安全策略隐藏。";
        }

        if (SqlStatementRegex.IsMatch(sanitized))
        {
            return "错误详情包含内部查询信息，已按安全策略隐藏。";
        }

        sanitized = SensitiveAssignmentRegex.Replace(sanitized, match =>
        {
            var separatorIndex = match.Value.IndexOfAny([':', '=']);
            return separatorIndex < 0
                ? "[redacted]"
                : $"{match.Value[..(separatorIndex + 1)]}[redacted]";
        });
        sanitized = ConnectionSegmentRegex.Replace(sanitized, match =>
        {
            var separatorIndex = match.Value.IndexOf('=');
            return separatorIndex < 0
                ? "[redacted]"
                : $"{match.Value[..(separatorIndex + 1)]}[redacted]";
        });
        sanitized = InternalPathRegex.Replace(sanitized, "[internal-path]");

        return sanitized.Length <= 1000
            ? sanitized
            : $"{sanitized[..1000]}...";
    }

    private static string BuildSafeWorkflowDetail(string code)
    {
        return code switch
        {
            AppProblemCodes.ChatConfigurationMissing =>
                "错误码 chat_configuration_missing：对话运行配置不可用，请管理员检查模板、模型或密钥配置。",
            AppProblemCodes.TokenBudgetExceeded =>
                "错误码 token_budget_exceeded：当前输入上下文超过模型 token 预算，请缩小问题范围或减少历史内容。",
            AppProblemCodes.ModelRequestTimeout =>
                "错误码 model_request_timeout：模型调用超过配置的超时时间。",
            AppProblemCodes.ModelProviderUnavailable =>
                "错误码 model_provider_unavailable：模型服务暂时不可用或网络请求未完成。",
            AppProblemCodes.ChatContextExpired =>
                "错误码 chat_context_expired：待恢复的对话上下文已过期，请重新发起请求。",
            AppProblemCodes.ApprovalAlreadyProcessed =>
                "错误码 approval_already_processed：该审批已处理，不能重复提交。",
            AppProblemCodes.ApprovalPending =>
                "错误码 approval_pending：当前会话已有等待处理的审批。",
            AppProblemCodes.ControlActionBlocked =>
                "错误码 control_action_blocked：该动作被 AICopilot 只读安全边界拦截。",
            AppProblemCodes.CapabilityNotAllowed =>
                "错误码 capability_not_allowed：当前能力不允许执行该操作。",
            AppProblemCodes.AgentPlanInvalid =>
                "错误码 agent_plan_invalid：计划内容未通过校验。",
            AppProblemCodes.AgentPlanToolDenied =>
                "错误码 agent_plan_tool_denied：计划引用了当前 Skill 或安全策略不允许的工具。",
            AppProblemCodes.AgentPlanSchemaInvalid =>
                "错误码 agent_plan_schema_invalid：工具输入未通过 schema 校验。",
            AppProblemCodes.ToolBlocked =>
                "错误码 tool_blocked：该工具被安全策略阻止执行。",
            AppProblemCodes.ToolExecutionNotFound =>
                "错误码 tool_execution_not_found：未找到可执行的工具运行时。",
            AppProblemCodes.ToolExecutionTimeout =>
                "错误码 tool_execution_timeout：工具执行超过配置的超时时间。",
            AppProblemCodes.CloudReadonlyIntentUnsupported =>
                "错误码 cloud_readonly_intent_unsupported：当前 Cloud 只读接口不支持该查询意图。",
            _ =>
                $"错误码 {code}：请求未能完成，详情已按安全策略隐藏。"
        };
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
