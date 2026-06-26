using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Tools;

internal static partial class ToolExecutionRecordSanitizer
{
    public static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = SecretPattern().Replace(value, "$1=******");
        sanitized = WindowsPathPattern().Replace(sanitized, "[redacted-path]");
        sanitized = ConnectionStringPartPattern().Replace(sanitized, "$1=******");
        sanitized = SqlStatementPattern().Replace(sanitized, "[redacted-sql]");
        sanitized = SqlObjectPattern().Replace(sanitized, "$1 [redacted]");

        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    [GeneratedRegex(@"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]+\s*[^,""}\s]+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s""']+")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?i)(Host|Username|Password|Database|Port)\s*=\s*[^;""'}]+")]
    private static partial Regex ConnectionStringPartPattern();

    [GeneratedRegex(@"(?is)\b(select|insert|update|delete|drop|alter|create)\b\s+.+?(?=,|;|\}|$)")]
    private static partial Regex SqlStatementPattern();

    [GeneratedRegex(@"(?i)\b(from|join|table)\s+[""`\[]?[A-Za-z0-9_.\]-]+")]
    private static partial Regex SqlObjectPattern();
}
