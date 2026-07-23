using System.Text;
using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Tools;

internal static partial class ToolExecutionRecordSanitizer
{
    internal const string PolicyVersion = "tool-execution-record-sanitizer:v2";

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
        var sanitized = ModelReasoningBlockPattern().Replace(value, "[redacted-model-reasoning]");
        sanitized = BearerTokenPattern().Replace(sanitized, "Bearer ******");
        sanitized = EndpointUriPattern().Replace(sanitized, "[redacted-endpoint]");
        sanitized = EndpointAssignmentPattern().Replace(sanitized, "$1=******");
        sanitized = ExceptionFramePattern().Replace(sanitized, "[redacted-exception-frame]");
        sanitized = PlcAddressPattern().Replace(sanitized, "[redacted-plc-address]");
        sanitized = SecretPattern().Replace(sanitized, "$1=******");
        sanitized = WindowsPathPattern().Replace(sanitized, "[redacted-path]");
        sanitized = UnixSensitivePathPattern().Replace(sanitized, "[redacted-path]");
        sanitized = ConnectionStringPartPattern().Replace(sanitized, "$1=******");
        sanitized = SqlStatementPattern().Replace(sanitized, "[redacted-sql]");
        return SqlObjectPattern().Replace(sanitized, "$1 [redacted]");
    }

    [GeneratedRegex(@"(?i)(api[_-]?key|token|password|secret|connection\s*string)\s*[""':=]+\s*[^,""}\s]+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"(?is)<think\b[^>]*>.*?</think>")]
    private static partial Regex ModelReasoningBlockPattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+\-/=]+")]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"(?i)\b(?:https?|postgres(?:ql)?|amqps?|redis)://[^\s""'<>]+")]
    private static partial Regex EndpointUriPattern();

    [GeneratedRegex(@"(?i)\b(endpoint|host)\s*[:=]\s*[^\s,;""'}]+")]
    private static partial Regex EndpointAssignmentPattern();

    [GeneratedRegex(@"(?m)^\s*at\s+[^\r\n]+")]
    private static partial Regex ExceptionFramePattern();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_])(?:DB\d+\.(?:DBX|DBB|DBW|DBD)\d+(?:\.\d+)?|%?[IQM]\d+(?:\.\d+)?)(?![A-Za-z0-9_])")]
    private static partial Regex PlcAddressPattern();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s""']+")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])/(?:Users|home|var|etc|opt|tmp|private|srv)/[^\s""']+")]
    private static partial Regex UnixSensitivePathPattern();

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
