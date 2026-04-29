using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

public enum AiChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record AiChatMessage
{
    public AiChatMessage(AiChatRole role, string text)
    {
        Role = role;
        Text = text;
        Contents = [];
    }

    public AiChatMessage(AiChatRole role, IReadOnlyList<AiRuntimeContent> contents)
    {
        Role = role;
        Contents = contents;
    }

    public AiChatRole Role { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<AiRuntimeContent> Contents { get; init; } = [];
}

public sealed class AiChatOptions
{
    public string? Instructions { get; set; }

    public float? Temperature { get; set; }

    public int? MaxOutputTokens { get; set; }

    public IReadOnlyList<AiToolDefinition> Tools { get; set; } = [];
}

public enum AiToolCallKind
{
    Function,
    Mcp
}

public sealed record AiToolCall(
    string CallId,
    string Name,
    AiToolCallKind Kind,
    string? ServerName,
    IReadOnlyDictionary<string, object?> Arguments);

public sealed record AiToolApprovalRequest(
    string RequestId,
    AiToolCall ToolCall);

public abstract record AiRuntimeContent;

public sealed record AiTextContent(string Text) : AiRuntimeContent;

public sealed record AiToolCallContent(AiToolCall ToolCall) : AiRuntimeContent;

public sealed record AiFunctionResultContent(string CallId, object? Result) : AiRuntimeContent;

public sealed record AiToolApprovalRequestContent(AiToolApprovalRequest Request) : AiRuntimeContent;

public sealed record AiToolApprovalResponseContent(
    AiToolApprovalRequest Request,
    bool IsApproved,
    string? Reason = null) : AiRuntimeContent;

public sealed record AiUsageContent(AiUsageDetails Details) : AiRuntimeContent;

public sealed class AiUsageDetails
{
    public long? InputTokenCount { get; set; }

    public long? OutputTokenCount { get; set; }

    public long? TotalTokenCount { get; set; }

    public long? CachedInputTokenCount { get; set; }

    public long? ReasoningTokenCount { get; set; }

    public void Add(AiUsageDetails details)
    {
        InputTokenCount = AddCounts(InputTokenCount, details.InputTokenCount);
        OutputTokenCount = AddCounts(OutputTokenCount, details.OutputTokenCount);
        TotalTokenCount = AddCounts(TotalTokenCount, details.TotalTokenCount);
        CachedInputTokenCount = AddCounts(CachedInputTokenCount, details.CachedInputTokenCount);
        ReasoningTokenCount = AddCounts(ReasoningTokenCount, details.ReasoningTokenCount);
    }

    private static long? AddCounts(long? current, long? next)
    {
        return (current, next) switch
        {
            (null, null) => null,
            (long value, null) => value,
            (null, long value) => value,
            (long left, long right) => left + right
        };
    }
}

public sealed record AiToolInvocationContext(
    IReadOnlyDictionary<string, object?> Arguments,
    IServiceProvider? Services,
    IReadOnlyDictionary<object, object?>? Context);

public sealed class AiToolDefinition
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool RequiresApproval { get; init; }

    public AiToolCallKind Kind { get; init; } = AiToolCallKind.Function;

    public string? ServerName { get; init; }

    public MethodInfo? Method { get; init; }

    public object? Target { get; init; }

    public JsonElement? JsonSchema { get; init; }

    public JsonElement? ReturnJsonSchema { get; init; }

    public Func<AiToolInvocationContext, CancellationToken, ValueTask<object?>>? InvokeAsync { get; init; }

    public IReadOnlyDictionary<string, object?> AdditionalProperties { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public static AiToolDefinition FromMethod(MethodInfo method, object target)
    {
        return new AiToolDefinition
        {
            Name = method.Name,
            Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
            Method = method,
            Target = target
        };
    }

    public AiToolDefinition WithRequiresApproval(bool requiresApproval)
    {
        return new AiToolDefinition
        {
            Name = Name,
            Description = Description,
            RequiresApproval = requiresApproval,
            Kind = Kind,
            ServerName = ServerName,
            Method = Method,
            Target = Target,
            JsonSchema = JsonSchema,
            ReturnJsonSchema = ReturnJsonSchema,
            InvokeAsync = InvokeAsync,
            AdditionalProperties = AdditionalProperties
        };
    }
}
