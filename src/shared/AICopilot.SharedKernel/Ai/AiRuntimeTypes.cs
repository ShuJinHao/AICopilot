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

public enum AiToolTargetType
{
    Plugin = 0,
    McpServer = 1
}

public enum AiToolExternalSystemType
{
    Unknown = 0,
    InternalDemo = 1,
    CloudReadOnly = 2,
    NonCloud = 3
}

public enum AiToolCapabilityKind
{
    ReadOnlyQuery = 0,
    Diagnostics = 1,
    LocalSuggestion = 2,
    SideEffecting = 3
}

public enum AiToolRiskLevel
{
    Low = 0,
    RequiresApproval = 1,
    Blocked = 2
}

public sealed record AiToolIdentity(
    AiToolCallKind Kind,
    AiToolTargetType TargetType,
    string TargetName,
    string ToolName)
{
    private const string McpPrefix = "mcp__";
    private const string PluginPrefix = "plugin__";

    public string RuntimeName => CreateRuntimeName(TargetType, TargetName, ToolName);

    public static string CreateRuntimeName(AiToolTargetType targetType, string targetName, string toolName)
    {
        var prefix = targetType == AiToolTargetType.McpServer ? McpPrefix : PluginPrefix;
        return $"{prefix}{NormalizeSegment(targetName)}__{NormalizeSegment(toolName)}";
    }

    public static bool TryParseRuntimeName(string runtimeName, out AiToolIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(runtimeName))
        {
            return false;
        }

        var targetType = runtimeName.StartsWith(McpPrefix, StringComparison.OrdinalIgnoreCase)
            ? AiToolTargetType.McpServer
            : runtimeName.StartsWith(PluginPrefix, StringComparison.OrdinalIgnoreCase)
                ? AiToolTargetType.Plugin
                : (AiToolTargetType?)null;

        if (targetType is null)
        {
            return false;
        }

        var prefixLength = targetType == AiToolTargetType.McpServer ? McpPrefix.Length : PluginPrefix.Length;
        var payload = runtimeName[prefixLength..];
        var parts = payload.Split("__", 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        identity = new AiToolIdentity(
            targetType == AiToolTargetType.McpServer ? AiToolCallKind.Mcp : AiToolCallKind.Function,
            targetType.Value,
            parts[0],
            parts[1]);
        return true;
    }

    private static string NormalizeSegment(string value)
    {
        var normalized = new string((value ?? string.Empty)
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')
            .ToArray());

        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "unnamed" : normalized;
    }
}

public sealed record AiToolCall(
    string CallId,
    string Name,
    AiToolCallKind Kind,
    string? ServerName,
    IReadOnlyDictionary<string, object?> Arguments,
    AiToolTargetType? TargetType = null,
    string? TargetName = null,
    string? ToolName = null)
{
    public AiToolIdentity? Identity =>
        TargetType.HasValue && !string.IsNullOrWhiteSpace(TargetName) && !string.IsNullOrWhiteSpace(ToolName)
            ? new AiToolIdentity(Kind, TargetType.Value, TargetName!, ToolName!)
            : null;
}

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

    public string? ToolName { get; init; }

    public string? Description { get; init; }

    public bool RequiresApproval { get; init; }

    public AiToolCallKind Kind { get; init; } = AiToolCallKind.Function;

    public AiToolTargetType? TargetType { get; init; }

    public string? TargetName { get; init; }

    public string? ServerName { get; init; }

    public AiToolExternalSystemType ExternalSystemType { get; init; } = AiToolExternalSystemType.Unknown;

    public AiToolCapabilityKind CapabilityKind { get; init; } = AiToolCapabilityKind.Diagnostics;

    public AiToolRiskLevel RiskLevel { get; init; } = AiToolRiskLevel.Low;

    public MethodInfo? Method { get; init; }

    public object? Target { get; init; }

    public JsonElement? JsonSchema { get; init; }

    public JsonElement? ReturnJsonSchema { get; init; }

    public Func<AiToolInvocationContext, CancellationToken, ValueTask<object?>>? InvokeAsync { get; init; }

    public IReadOnlyDictionary<string, object?> AdditionalProperties { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public AiToolIdentity? Identity =>
        TargetType.HasValue && !string.IsNullOrWhiteSpace(TargetName)
            ? new AiToolIdentity(Kind, TargetType.Value, TargetName!, ToolName ?? Name)
            : null;

    public static AiToolDefinition FromMethod(MethodInfo method, object target)
    {
        return new AiToolDefinition
        {
            Name = method.Name,
            ToolName = method.Name,
            Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
            Method = method,
            Target = target
        };
    }

    public AiToolDefinition WithIdentity(
        AiToolTargetType targetType,
        string targetName,
        string toolName,
        AiToolExternalSystemType externalSystemType,
        AiToolCapabilityKind capabilityKind,
        AiToolRiskLevel riskLevel)
    {
        var kind = targetType == AiToolTargetType.McpServer ? AiToolCallKind.Mcp : Kind;
        return Copy(
            name: AiToolIdentity.CreateRuntimeName(targetType, targetName, toolName),
            toolName: toolName,
            kind: kind,
            targetType: targetType,
            targetName: targetName,
            externalSystemType: externalSystemType,
            capabilityKind: capabilityKind,
            riskLevel: riskLevel,
            requiresApproval: RequiresApproval || riskLevel == AiToolRiskLevel.RequiresApproval);
    }

    public AiToolDefinition WithRequiresApproval(bool requiresApproval)
    {
        return Copy(requiresApproval: requiresApproval);
    }

    private AiToolDefinition Copy(
        string? name = null,
        string? toolName = null,
        bool? requiresApproval = null,
        AiToolCallKind? kind = null,
        AiToolTargetType? targetType = null,
        string? targetName = null,
        AiToolExternalSystemType? externalSystemType = null,
        AiToolCapabilityKind? capabilityKind = null,
        AiToolRiskLevel? riskLevel = null)
    {
        return new AiToolDefinition
        {
            Name = name ?? Name,
            ToolName = toolName ?? ToolName,
            Description = Description,
            RequiresApproval = requiresApproval ?? RequiresApproval,
            Kind = kind ?? Kind,
            TargetType = targetType ?? TargetType,
            TargetName = targetName ?? TargetName,
            ServerName = ServerName,
            ExternalSystemType = externalSystemType ?? ExternalSystemType,
            CapabilityKind = capabilityKind ?? CapabilityKind,
            RiskLevel = riskLevel ?? RiskLevel,
            Method = Method,
            Target = Target,
            JsonSchema = JsonSchema,
            ReturnJsonSchema = ReturnJsonSchema,
            InvokeAsync = InvokeAsync,
            AdditionalProperties = AdditionalProperties
        };
    }
}
