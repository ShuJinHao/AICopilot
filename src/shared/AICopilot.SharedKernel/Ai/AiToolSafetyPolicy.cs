using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

public sealed record AiToolSafetyDecision(bool IsAllowed, string? Reason, IReadOnlyList<string>? BlockReasons = null)
{
    public static AiToolSafetyDecision Allow { get; } = new(true, null, []);
}

public sealed record AiToolSafetyDescriptor(
    bool ReadOnlyDeclared,
    bool? McpReadOnlyHint,
    bool? McpDestructiveHint,
    bool? McpIdempotentHint,
    AiToolCapabilityKind CapabilityKind,
    AiToolExternalSystemType ExternalSystemType,
    AiToolRiskLevel RiskLevel,
    IReadOnlyList<string> DeclaredEffects,
    IReadOnlyList<string> BlockReasons)
{
    public static AiToolSafetyDescriptor Create(
        bool readOnlyDeclared,
        bool? mcpReadOnlyHint,
        bool? mcpDestructiveHint,
        bool? mcpIdempotentHint,
        AiToolCapabilityKind capabilityKind,
        AiToolExternalSystemType externalSystemType,
        AiToolRiskLevel riskLevel,
        IReadOnlyList<string>? declaredEffects = null,
        IReadOnlyList<string>? blockReasons = null)
    {
        return new AiToolSafetyDescriptor(
            readOnlyDeclared,
            mcpReadOnlyHint,
            mcpDestructiveHint,
            mcpIdempotentHint,
            capabilityKind,
            externalSystemType,
            riskLevel,
            declaredEffects ?? [],
            blockReasons ?? []);
    }
}

public static class AiToolSafetyPolicy
{
    private static readonly string[] CloudForbiddenVerbs =
    [
        "create",
        "update",
        "delete",
        "register",
        "disable",
        "approve",
        "dispatch",
        "trigger",
        "backfill",
        "correct",
        "upload",
        "submit",
        "set",
        "modify",
        "enable",
        "reset",
        "start",
        "stop",
        "restart",
        "reboot",
        "rotate",
        "grant",
        "revoke",
        "apply",
        "assign",
        "remove",
        "archive",
        "import",
        "export",
        "write",
        "新增",
        "创建",
        "注册",
        "更新",
        "修改",
        "删除",
        "禁用",
        "启用",
        "审批",
        "派发",
        "触发",
        "补录",
        "纠正",
        "上传",
        "提交",
        "设置",
        "重置",
        "开始",
        "停止",
        "重启",
        "下发",
        "生效",
        "授权",
        "撤销",
        "应用",
        "分配",
        "移除",
        "归档",
        "导入",
        "导出",
        "写入",
        "同步"
    ];

    private static readonly string[] CloudAllowedVerbs =
    [
        "get",
        "list",
        "query",
        "search",
        "summarize",
        "explain",
        "analyze"
    ];

    public static AiToolSafetyDecision Evaluate(
        AiToolExternalSystemType externalSystemType,
        AiToolCapabilityKind capabilityKind,
        AiToolRiskLevel riskLevel,
        string toolName,
        string? description,
        bool readOnlyDeclared = false,
        JsonElement? inputSchema = null,
        JsonElement? returnSchema = null)
    {
        var descriptor = AiToolSafetyDescriptor.Create(
            readOnlyDeclared,
            mcpReadOnlyHint: null,
            mcpDestructiveHint: null,
            mcpIdempotentHint: null,
            capabilityKind,
            externalSystemType,
            riskLevel);

        return Evaluate(descriptor, toolName, description, inputSchema, returnSchema);
    }

    public static AiToolSafetyDecision Evaluate(
        AiToolSafetyDescriptor descriptor,
        string toolName,
        string? description,
        JsonElement? inputSchema = null,
        JsonElement? returnSchema = null)
    {
        var blockReasons = new List<string>(descriptor.BlockReasons);

        if (descriptor.RiskLevel == AiToolRiskLevel.Blocked)
        {
            blockReasons.Add("Tool risk level is blocked.");
            return Block(blockReasons);
        }

        if (descriptor.ExternalSystemType != AiToolExternalSystemType.CloudReadOnly)
        {
            return AiToolSafetyDecision.Allow;
        }

        if (!descriptor.ReadOnlyDeclared)
        {
            blockReasons.Add("Cloud-related tools must explicitly declare read-only behavior.");
        }

        if (descriptor.McpDestructiveHint == true)
        {
            blockReasons.Add("Cloud-related tools must not declare MCP destructive behavior.");
        }

        if (descriptor.McpReadOnlyHint == false)
        {
            blockReasons.Add("Cloud-related tools must not declare MCP non-read-only behavior.");
        }

        if (descriptor.CapabilityKind == AiToolCapabilityKind.SideEffecting)
        {
            blockReasons.Add("Cloud-related tools must not be side-effecting.");
        }

        if (!CloudAllowedVerbs.Any(verb => StartsWithToken(toolName, verb)))
        {
            blockReasons.Add("Cloud-related tool must use an approved read-only verb.");
        }

        var safetyText = BuildSafetyText(toolName, description, inputSchema, returnSchema);
        if (CloudForbiddenVerbs.Any(verb => ContainsToken(safetyText, verb)))
        {
            blockReasons.Add("Cloud-related tool contains forbidden write semantics.");
        }

        foreach (var effect in descriptor.DeclaredEffects)
        {
            if (CloudForbiddenVerbs.Any(verb => ContainsToken(effect, verb)))
            {
                blockReasons.Add($"Cloud-related tool declares forbidden effect '{effect}'.");
            }
        }

        return blockReasons.Count == 0 ? AiToolSafetyDecision.Allow : Block(blockReasons);
    }

    private static AiToolSafetyDecision Block(IReadOnlyList<string> blockReasons)
    {
        var reason = string.Join(" ", blockReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
        return new AiToolSafetyDecision(false, reason, blockReasons);
    }

    private static string BuildSafetyText(
        string toolName,
        string? description,
        JsonElement? inputSchema,
        JsonElement? returnSchema)
    {
        return string.Join(
            ' ',
            [
                toolName,
                description ?? string.Empty,
                inputSchema.HasValue ? inputSchema.Value.GetRawText() : string.Empty,
                returnSchema.HasValue ? returnSchema.Value.GetRawText() : string.Empty
            ]);
    }

    private static bool ContainsToken(string value, string token)
    {
        if (token.Any(ch => ch > 127))
        {
            return value.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        return SplitIdentifier(value).Any(part => string.Equals(part, token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithToken(string value, string token)
    {
        return SplitIdentifier(value).FirstOrDefault() is { } first
               && string.Equals(first, token, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitIdentifier(string value)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                Flush();
                continue;
            }

            if (char.IsUpper(ch) && current.Count > 0)
            {
                Flush();
            }

            current.Add(char.ToLowerInvariant(ch));
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            var token = new string(current.ToArray());
            current.Clear();
            if (!string.IsNullOrWhiteSpace(token))
            {
                tokens.Add(token);
            }
        }
    }
}
