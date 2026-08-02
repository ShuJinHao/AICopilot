using System.Runtime.CompilerServices;
using AICopilot.Services.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AICopilot.AiRuntime;

/// <summary>
/// Final fail-closed boundary before a request reaches the governed provider.
/// The exact tool set for each provider request comes from the effective
/// <see cref="ChatOptions"/> produced by the Harness; the guard never applies
/// mode-specific filtering or changes tool order.
/// </summary>
internal sealed class ToolInvocationGuardChatClient(IChatClient inner)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        RejectAlwaysApprove(materialized);
        var guardedOptions = PrepareOptions(options);
        var allowedToolNames = ResolveAllowedToolNames(guardedOptions);
        var response = await base.GetResponseAsync(
            materialized,
            guardedOptions,
            cancellationToken);
        ValidateModelContents(
            response.Messages.SelectMany(message => message.Contents),
            allowedToolNames,
            new HashSet<RequestedToolCall>());
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        RejectAlwaysApprove(materialized);
        var guardedOptions = PrepareOptions(options);
        var allowedToolNames = ResolveAllowedToolNames(guardedOptions);
        var observedToolCalls = new HashSet<RequestedToolCall>();
        await foreach (var update in base.GetStreamingResponseAsync(
                           materialized,
                           guardedOptions,
                           cancellationToken))
        {
            ValidateModelContents(
                update.Contents,
                allowedToolNames,
                observedToolCalls);
            yield return update;
        }
    }

    private static ChatOptions PrepareOptions(ChatOptions? options)
    {
        var guarded = options?.Clone() ?? new ChatOptions();
        guarded.AllowMultipleToolCalls = false;
        return guarded;
    }

    private static HashSet<string> ResolveAllowedToolNames(ChatOptions options) =>
        options.Tools?
            .Select(ResolveName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

    private static void ValidateModelContents(
        IEnumerable<AIContent> contents,
        IReadOnlySet<string> allowedToolNames,
        ISet<RequestedToolCall> observedToolCalls)
    {
        foreach (var request in contents
                     .Select(ResolveRequestedTool)
                     .Where(request => request is not null)
                     .Select(request => request!.Value))
        {
            if (!allowedToolNames.Contains(request.ToolName))
            {
                throw new HarnessToolInvocationViolationException(request.ToolName);
            }

            if (observedToolCalls.Add(request) && observedToolCalls.Count > 1)
            {
                throw new AgentRuntimeMultipleToolCallsException();
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

    private static RequestedToolCall? ResolveRequestedTool(AIContent content)
    {
#pragma warning disable MEAI001
        return content switch
        {
            FunctionCallContent function => new RequestedToolCall(
                function.CallId,
                function.Name),
            ToolApprovalRequestContent approval => new RequestedToolCall(
                approval.ToolCall.CallId,
                ResolveToolCallName(approval.ToolCall)),
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

    private readonly record struct RequestedToolCall(
        string ToolCallId,
        string ToolName);
}

internal sealed class HarnessToolInvocationViolationException(string toolName)
    : InvalidOperationException(
        $"The model attempted to invoke unexposed tool '{toolName}'. The request was blocked.");

internal sealed class HarnessAlwaysApproveRejectedException()
    : InvalidOperationException(
        "Standing or always-approve tool responses are not accepted.");
