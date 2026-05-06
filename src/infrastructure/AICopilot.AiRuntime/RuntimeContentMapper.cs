using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.AI;

namespace AICopilot.AiRuntime;

internal static class RuntimeContentMapper
{
    public static ChatMessage ToChatMessage(AiChatMessage message)
    {
        if (message.Contents.Count > 0)
        {
            return new ChatMessage(ToChatRole(message.Role), message.Contents.Select(ToAiContent).ToList());
        }

        return new ChatMessage(ToChatRole(message.Role), message.Text ?? string.Empty);
    }

    public static IReadOnlyList<AiRuntimeContent> ToRuntimeContents(IEnumerable<AIContent> contents)
    {
        return contents
            .Select(ToRuntimeContent)
            .Where(content => content is not null)
            .Select(content => content!)
            .ToArray();
    }

    private static AIContent ToAiContent(AiRuntimeContent content)
    {
        return content switch
        {
            AiTextContent text => new TextContent(text.Text),
            AiToolCallContent call => ToToolCallContent(call.ToolCall),
            AiFunctionResultContent result => new FunctionResultContent(result.CallId, result.Result),
            AiToolApprovalResponseContent response => CreateApprovalResponse(response),
            _ => throw new NotSupportedException($"Unsupported runtime content type '{content.GetType().FullName}'.")
        };
    }

    private static AIContent CreateApprovalResponse(AiToolApprovalResponseContent response)
    {
        var request = new ToolApprovalRequestContent(
            response.Request.RequestId,
            ToToolCallContent(response.Request.ToolCall));

        return request.CreateResponse(response.IsApproved, response.Reason);
    }

    private static AiRuntimeContent? ToRuntimeContent(AIContent content)
    {
#pragma warning disable MEAI001
        return content switch
        {
            TextContent text => new AiTextContent(text.Text),
            FunctionCallContent call => new AiToolCallContent(ToToolCall(call)),
            FunctionResultContent result => new AiFunctionResultContent(result.CallId, result.Result),
            ToolApprovalRequestContent approval => new AiToolApprovalRequestContent(
                new AiToolApprovalRequest(
                    approval.RequestId,
                    ToToolCall(approval.ToolCall))),
            UsageContent usage => new AiUsageContent(ToUsageDetails(usage.Details)),
            _ => null
        };
#pragma warning restore MEAI001
    }

    private static ToolCallContent ToToolCallContent(AiToolCall toolCall)
    {
        return toolCall.Kind switch
        {
            AiToolCallKind.Mcp => new McpServerToolCallContent(
                toolCall.CallId,
                toolCall.Name,
                toolCall.ServerName ?? string.Empty)
            {
                Arguments = ToArguments(toolCall.Arguments)
            },
            _ => new FunctionCallContent(toolCall.CallId, toolCall.Name, ToArguments(toolCall.Arguments))
        };
    }

    private static AiToolCall ToToolCall(ToolCallContent toolCall)
    {
#pragma warning disable MEAI001
        return toolCall switch
        {
            FunctionCallContent function => new AiToolCall(
                function.CallId,
                function.Name,
                AiToolCallKind.Function,
                null,
                NormalizeArguments(function.Arguments),
                ResolveTargetType(function.Name),
                ResolveTargetName(function.Name),
                ResolveToolName(function.Name)),
            McpServerToolCallContent mcp => new AiToolCall(
                mcp.CallId,
                mcp.Name,
                AiToolCallKind.Mcp,
                mcp.ServerName,
                NormalizeArguments(mcp.Arguments),
                AiToolTargetType.McpServer,
                mcp.ServerName,
                mcp.Name),
            _ => new AiToolCall(
                toolCall.CallId,
                toolCall.CallId,
                AiToolCallKind.Function,
                null,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase))
        };
#pragma warning restore MEAI001
    }

    private static AiToolTargetType? ResolveTargetType(string name)
    {
        return AiToolIdentity.TryParseRuntimeName(name, out var identity)
            ? identity!.TargetType
            : null;
    }

    private static string? ResolveTargetName(string name)
    {
        return AiToolIdentity.TryParseRuntimeName(name, out var identity)
            ? identity!.TargetName
            : null;
    }

    private static string? ResolveToolName(string name)
    {
        return AiToolIdentity.TryParseRuntimeName(name, out var identity)
            ? identity!.ToolName
            : null;
    }

    private static ChatRole ToChatRole(AiChatRole role)
    {
        return role switch
        {
            AiChatRole.System => ChatRole.System,
            AiChatRole.Assistant => ChatRole.Assistant,
            AiChatRole.Tool => ChatRole.Tool,
            _ => ChatRole.User
        };
    }

    private static Dictionary<string, object?> NormalizeArguments(
        IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        return arguments?.ToDictionary(
                   item => item.Key,
                   item => item.Value,
                   StringComparer.OrdinalIgnoreCase)
               ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> ToArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        return arguments.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static AiUsageDetails ToUsageDetails(UsageDetails details)
    {
        return new AiUsageDetails
        {
            InputTokenCount = details.InputTokenCount,
            OutputTokenCount = details.OutputTokenCount,
            TotalTokenCount = details.TotalTokenCount,
            CachedInputTokenCount = details.CachedInputTokenCount,
            ReasoningTokenCount = details.ReasoningTokenCount
        };
    }
}
