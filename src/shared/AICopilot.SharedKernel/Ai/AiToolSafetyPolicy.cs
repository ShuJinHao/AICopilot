using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

public sealed record AiToolSafetyDecision(bool IsAllowed, string? Reason)
{
    public static AiToolSafetyDecision Allow { get; } = new(true, null);
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
        "write"
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
        if (riskLevel == AiToolRiskLevel.Blocked)
        {
            return new AiToolSafetyDecision(false, "Tool risk level is blocked.");
        }

        if (externalSystemType != AiToolExternalSystemType.CloudReadOnly)
        {
            return AiToolSafetyDecision.Allow;
        }

        if (!readOnlyDeclared)
        {
            return new AiToolSafetyDecision(false, "Cloud-related tools must explicitly declare read-only behavior.");
        }

        if (capabilityKind == AiToolCapabilityKind.SideEffecting)
        {
            return new AiToolSafetyDecision(false, "Cloud-related tools must not be side-effecting.");
        }

        if (!CloudAllowedVerbs.Any(verb => StartsWithToken(toolName, verb)))
        {
            return new AiToolSafetyDecision(false, "Cloud-related tool must use an approved read-only verb.");
        }

        var safetyText = BuildSafetyText(toolName, description, inputSchema, returnSchema);
        if (CloudForbiddenVerbs.Any(verb => ContainsToken(safetyText, verb)))
        {
            return new AiToolSafetyDecision(false, "Cloud-related tool contains forbidden write semantics.");
        }

        return AiToolSafetyDecision.Allow;
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
