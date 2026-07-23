using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AICopilot.Services.Contracts;

public enum CloudReadOnlyTextToSqlFailureStage
{
    Unknown = 0,
    Draft = 1,
    Guard = 2,
    Runtime = 3,
    Metadata = 4,
    AgentTool = 5
}

public enum CloudReadOnlyTextToSqlFailureCode
{
    Unknown = 0,
    Syntax = 1,
    UnknownTable = 2,
    UnknownColumn = 3,
    WildcardProjection = 4,
    SensitiveField = 5,
    WriteSql = 6,
    MultiStatement = 7,
    SystemCatalog = 8,
    Timeout = 9,
    Credential = 10,
    Forbidden = 11,
    NotReadOnly = 12,
    GovernedSchemaMissing = 13,
    SourceUnavailable = 14,
    RuntimeRejected = 15
}

public sealed record CloudReadOnlyTextToSqlRepairDecision(
    CloudReadOnlyTextToSqlFailureStage Stage,
    CloudReadOnlyTextToSqlFailureCode Code,
    bool CanRepairSql,
    bool CanRetry,
    string SafeSummary);

public sealed record CloudReadOnlyTextToSqlRepairAttemptRecord(
    int AttemptIndex,
    CloudReadOnlyTextToSqlFailureStage Stage,
    CloudReadOnlyTextToSqlFailureCode FailureCode,
    bool CanRepairSql,
    bool CanRetry,
    string SqlHash,
    int SqlLength,
    string SafeErrorSummary,
    DateTimeOffset CreatedAt);

public static class CloudReadOnlyTextToSqlRepairClassifier
{
    private const int MaxSummaryLength = 500;

    public static CloudReadOnlyTextToSqlRepairDecision Classify(
        CloudReadOnlyTextToSqlFailureStage stage,
        string? errorMessage,
        BusinessQuerySecurityProfile? securityProfile = null)
    {
        var safeSummary = TryBuildPermissionDeniedSummary(
                errorMessage,
                securityProfile,
                out var permissionDeniedSummary)
            ? permissionDeniedSummary
            : SanitizeErrorSummary(errorMessage, securityProfile);
        var lower = (errorMessage ?? string.Empty).ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(lower))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.Unknown, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(lower, "timeout", "timed out", "operation canceled"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.Timeout, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(lower, "not authorized", "forbidden", "permission", "unauthorized", "data_source_forbidden"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.Forbidden, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(lower, "verified readonly credential", "readonly credential", "read-only credential", "credential_unverified"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.Credential, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(lower, "disabled or not readonly", "not readonly", "not read-only", "read only mode", "readonly mode"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.NotReadOnly, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(lower, "governed semantic schema is required", "governed query safety schema is required"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.GovernedSchemaMissing, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(
                lower,
                "system catalog",
                "禁止访问数据库系统目录",
                "information_schema.",
                "pg_catalog.",
                "pg_user",
                "pg_shadow",
                "sys.",
                "mysql."))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.SystemCatalog, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(lower, "sensitive field", "sensitive fields", "敏感字段") ||
            ResolveBlockedFragments(securityProfile).Any(fragment =>
                lower.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.SensitiveField, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(
                lower,
                "multiple sql statements",
                "multiple statements",
                "semicolon",
                "禁止在单次调用中执行多条 sql"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.MultiStatement, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(
                lower,
                "only select statements are allowed",
                "ddl and dml statements are not allowed",
                "仅允许执行 select"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.WriteSql, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(
                lower,
                "wildcard select",
                "wildcard projection",
                "select *",
                "禁止使用通配符投影"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.WildcardProjection, canRepairSql: true, safeSummary);
        }

        if (ContainsAny(lower, "column") &&
            ContainsAny(lower, "does not exist", "unknown", "not allowed", "not allowlisted", "invalid identifier"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.UnknownColumn, canRepairSql: true, safeSummary);
        }

        if (ContainsAny(lower, "relation", "table") &&
            ContainsAny(lower, "does not exist", "unknown", "not allowed", "not allowlisted", "must reference an allowed business table"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.UnknownTable, canRepairSql: true, safeSummary);
        }

        if (ContainsAny(lower, "syntax error", "sql syntax", "parse error", "unterminated", "near \"", "near '"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.Syntax, canRepairSql: true, safeSummary);
        }

        if (ContainsAny(lower, "runtime guard", "guardrail"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.RuntimeRejected, canRepairSql: false, safeSummary);
        }

        if (ContainsAny(lower, "data source", "source is not available", "not found"))
        {
            return Decision(stage, CloudReadOnlyTextToSqlFailureCode.SourceUnavailable, canRepairSql: false, safeSummary);
        }

        return Decision(stage, CloudReadOnlyTextToSqlFailureCode.Unknown, canRepairSql: false, safeSummary);
    }

    public static CloudReadOnlyTextToSqlRepairAttemptRecord CreateAttemptRecord(
        int attemptIndex,
        CloudReadOnlyTextToSqlFailureStage stage,
        string? sql,
        string? errorMessage,
        DateTimeOffset? createdAt = null,
        int maxRepairAttempts = CloudReadOnlyTextToSqlOptions.DefaultMaxRepairAttempts,
        BusinessQuerySecurityProfile? securityProfile = null)
    {
        var decision = Classify(stage, errorMessage, securityProfile);
        var resolvedMaxRepairAttempts = Math.Clamp(
            maxRepairAttempts,
            0,
            CloudReadOnlyTextToSqlOptions.AbsoluteMaxRepairAttempts);
        return new CloudReadOnlyTextToSqlRepairAttemptRecord(
            attemptIndex,
            stage,
            decision.Code,
            decision.CanRepairSql,
            decision.CanRetry && attemptIndex <= resolvedMaxRepairAttempts,
            ComputeSqlHash(sql),
            sql?.Length ?? 0,
            decision.SafeSummary,
            createdAt ?? DateTimeOffset.UtcNow);
    }

    public static string ComputeSqlHash(string? sql)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql ?? string.Empty))).ToLowerInvariant();
    }

    public static string SanitizeErrorSummary(
        string? errorMessage,
        BusinessQuerySecurityProfile? securityProfile = null)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "No error message was provided.";
        }

        var sanitized = errorMessage.Trim();
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)(password|pwd|secret|token|api[_-]?key)\s*=\s*[^;\s]+",
            "[redacted]=[redacted]",
            RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(
            sanitized,
            @"'(?<value>[^']*)'",
            match =>
            {
                var value = match.Groups["value"].Value;
                return value.Length > 64 || value.Any(char.IsWhiteSpace) || value.Contains(';', StringComparison.Ordinal)
                    ? "'[redacted]'"
                    : match.Value;
            },
            RegexOptions.CultureInvariant);

        foreach (var fragment in ResolveBlockedFragments(securityProfile)
                     .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
                     .OrderByDescending(fragment => fragment.Length))
        {
            sanitized = Regex.Replace(
                sanitized,
                Regex.Escape(fragment),
                "[redacted]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        sanitized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return sanitized.Length <= MaxSummaryLength
            ? sanitized
            : sanitized[..MaxSummaryLength];
    }

    private static CloudReadOnlyTextToSqlRepairDecision Decision(
        CloudReadOnlyTextToSqlFailureStage stage,
        CloudReadOnlyTextToSqlFailureCode code,
        bool canRepairSql,
        string safeSummary)
    {
        return new CloudReadOnlyTextToSqlRepairDecision(stage, code, canRepairSql, canRepairSql, safeSummary);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryBuildPermissionDeniedSummary(
        string? errorMessage,
        BusinessQuerySecurityProfile? securityProfile,
        out string safeSummary)
    {
        safeSummary = string.Empty;
        if (string.IsNullOrWhiteSpace(errorMessage) ||
            !errorMessage.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = Regex.Match(
            errorMessage,
            @"(?i)\bpermission denied for (?:table|relation)\s+""?(?<table>[a-z_][a-z0-9_]*)""?",
            RegexOptions.CultureInvariant);
        if (match.Success)
        {
            var tableName = match.Groups["table"].Value;
            safeSummary = securityProfile?.AllowedTables.Contains(tableName) == true
                ? $"Readonly permission denied for table {tableName}."
                : "Readonly permission denied.";
            return true;
        }

        safeSummary = "Readonly permission denied.";
        return true;
    }

    private static IEnumerable<string> ResolveBlockedFragments(
        BusinessQuerySecurityProfile? securityProfile)
    {
        return securityProfile is null
            ? CloudReadOnlyGovernedSchema.BlockedFieldFragments
            : securityProfile.BlockedIdentifierFragments;
    }
}
