using System.Text;
using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Tools;

internal static partial class ToolExecutionRecordSanitizer
{
    internal const string PolicyVersion = "tool-execution-record-sanitizer:v2";
    private const RegexOptions ExtendedPatternOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private static readonly (Regex Pattern, string Replacement)[] ExtendedSanitizers =
    [
        (new Regex(@"(?is)<think\b[^>]*>.*?</think>", ExtendedPatternOptions), "[redacted-model-reasoning]"), (new Regex(@"(?i)\bBearer\s+[A-Za-z0-9._~+\-/=]+", ExtendedPatternOptions), "Bearer ******"),
        (new Regex(@"(?i)\b(?:https?|postgres(?:ql)?|amqps?|redis)://[^\s""'<>]+", ExtendedPatternOptions), "[redacted-endpoint]"), (new Regex(@"(?i)\b(endpoint|host)\s*[:=]\s*[^\s,;""'}]+", ExtendedPatternOptions), "$1=******"),
        (new Regex(@"(?m)^\s*at\s+[^\r\n]+", ExtendedPatternOptions), "[redacted-exception-frame]"), (new Regex(@"(?i)(?<![A-Za-z0-9_])(?:DB\d+\.(?:DBX|DBB|DBW|DBD)\d+(?:\.\d+)?|%?[IQM]\d+(?:\.\d+)?)(?![A-Za-z0-9_])", ExtendedPatternOptions), "[redacted-plc-address]"),
        (new Regex(@"(?<![A-Za-z0-9_])/(?:Users|home|var|etc|opt|tmp|private|srv)/[^\s""']+", ExtendedPatternOptions), "[redacted-path]")
    ];

    public static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = SanitizeWithoutLimit(value);

        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    public static ToolExecutionSanitizationResult SanitizeUtf8WithMetadata(
        string? value,
        int maxUtf8Bytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ToolExecutionSanitizationResult(null, 0, false);
        }

        if (maxUtf8Bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes));
        }

        var sanitized = SanitizeWithoutLimit(value);
        var sanitizedUtf8ByteCount = Encoding.UTF8.GetByteCount(sanitized);
        if (sanitizedUtf8ByteCount <= maxUtf8Bytes)
        {
            return new ToolExecutionSanitizationResult(
                sanitized,
                sanitizedUtf8ByteCount,
                false);
        }

        var builder = new StringBuilder();
        var byteCount = 0;
        foreach (var rune in sanitized.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > maxUtf8Bytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            byteCount += rune.Utf8SequenceLength;
        }

        return new ToolExecutionSanitizationResult(
            builder.ToString(),
            sanitizedUtf8ByteCount,
            true);
    }

    private static string SanitizeWithoutLimit(string value)
    {
        var sanitized = value;
        foreach (var (pattern, replacement) in ExtendedSanitizers)
        {
            sanitized = pattern.Replace(sanitized, replacement);
        }

        sanitized = SecretPattern().Replace(sanitized, "$1=******");
        sanitized = WindowsPathPattern().Replace(sanitized, "[redacted-path]");
        sanitized = ConnectionStringPartPattern().Replace(sanitized, "$1=******");
        sanitized = SqlStatementPattern().Replace(sanitized, "[redacted-sql]");
        return SqlObjectPattern().Replace(sanitized, "$1 [redacted]");
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

internal sealed record ToolExecutionSanitizationResult(
    string? Value,
    int SanitizedUtf8ByteCount,
    bool IsTruncated);
