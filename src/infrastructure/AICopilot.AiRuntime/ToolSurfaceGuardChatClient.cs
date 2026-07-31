using System.Runtime.CompilerServices;
using AICopilot.Services.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AICopilot.AiRuntime;

internal sealed class HarnessToolSurfacePolicy(IEnumerable<string> executeToolNames)
{
    private static readonly HashSet<string> HarnessTools =
    [
        "mode_get",
        "todos_add",
        "todos_complete",
        "todos_remove",
        "todos_get_remaining",
        "todos_get_all"
    ];

    private readonly HashSet<string> executeTools = executeToolNames
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Where(name => !IsForbiddenCapability(name))
        .Concat(HarnessTools)
        .ToHashSet(StringComparer.Ordinal);
    private volatile RuntimeAgentMode mode = RuntimeAgentMode.Plan;

    public void SetMode(RuntimeAgentMode value) => mode = value;

    public bool IsAllowed(string toolName)
    {
        if (string.Equals(toolName, "mode_set", StringComparison.Ordinal))
        {
            return false;
        }

        return mode == RuntimeAgentMode.Plan
            ? HarnessTools.Contains(toolName)
            : executeTools.Contains(toolName);
    }

    private static bool IsForbiddenCapability(string name)
    {
        return name.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("file_access", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("file_artifact", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("background_agent", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Final fail-closed boundary before a request reaches the governed provider.
/// Context-provider tools are filtered here as well as application tools, so
/// hidden capabilities cannot be restored by prompt text or run options.
/// </summary>
internal sealed class ToolSurfaceGuardChatClient(
    IChatClient inner,
    HarnessToolSurfacePolicy policy) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        RejectAlwaysApprove(materialized);
        var response = await base.GetResponseAsync(
            materialized,
            FilterOptions(options),
            cancellationToken);
        ValidateModelContents(response.Messages.SelectMany(message => message.Contents));
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        RejectAlwaysApprove(materialized);
        await foreach (var update in base.GetStreamingResponseAsync(
                           materialized,
                           FilterOptions(options),
                           cancellationToken))
        {
            ValidateModelContents(update.Contents);
            yield return update;
        }
    }

    private ChatOptions FilterOptions(ChatOptions? options)
    {
        var filtered = options?.Clone() ?? new ChatOptions();
        filtered.Tools = filtered.Tools?
            .Where(tool => ResolveName(tool) is { } name && policy.IsAllowed(name))
            .ToList();
        return filtered;
    }

    private void ValidateModelContents(IEnumerable<AIContent> contents)
    {
        foreach (var toolName in contents.Select(ResolveRequestedToolName).Where(name => name is not null))
        {
            if (!policy.IsAllowed(toolName!))
            {
                throw new HarnessToolSurfaceViolationException(toolName!);
            }
        }
    }

    private static void RejectAlwaysApprove(IEnumerable<ChatMessage> messages)
    {
        if (messages.SelectMany(message => message.Contents)
            .Any(content => content is AlwaysApproveToolApprovalResponseContent))
        {
            throw new HarnessAlwaysApproveRejectedException();
        }
    }

    private static string? ResolveName(AITool tool)
    {
        return tool switch
        {
            AIFunction function => function.Name,
            _ => tool.GetService<AIFunction>()?.Name
        };
    }

    private static string? ResolveRequestedToolName(AIContent content)
    {
#pragma warning disable MEAI001
        return content switch
        {
            FunctionCallContent function => function.Name,
            ToolApprovalRequestContent approval => ResolveToolCallName(approval.ToolCall),
            _ => null
        };
#pragma warning restore MEAI001
    }

    private static string ResolveToolCallName(ToolCallContent toolCall)
    {
#pragma warning disable MEAI001
        return toolCall switch
        {
            FunctionCallContent function => function.Name,
            McpServerToolCallContent mcp => mcp.Name,
            _ => toolCall.CallId
        };
#pragma warning restore MEAI001
    }
}

internal sealed class HarnessToolSurfaceViolationException(string toolName)
    : InvalidOperationException(
        $"The model attempted to invoke hidden tool '{toolName}'. The request was blocked.");

internal sealed class HarnessAlwaysApproveRejectedException()
    : InvalidOperationException(
        "Standing or always-approve tool responses are not accepted.");
