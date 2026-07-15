using System.Security.Cryptography;
using System.Text;

namespace AICopilot.Dapper.Security;

public static class SqlExecutionLogPolicy
{
    public static SqlLogMetadata CreateMetadata(string? sql)
    {
        var normalizedSql = sql ?? string.Empty;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSql))).ToLowerInvariant();
        return new SqlLogMetadata(normalizedSql.Length, hash);
    }

    public static string ClassifyGuardrailFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown";
        }

        if (message.Contains("不能为空", StringComparison.OrdinalIgnoreCase))
        {
            return "empty_sql";
        }

        if (message.Contains("语法解析", StringComparison.OrdinalIgnoreCase))
        {
            return "parse_error";
        }

        if (message.Contains("多条", StringComparison.OrdinalIgnoreCase))
        {
            return "multiple_statements";
        }

        if (message.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return "non_select";
        }

        if (message.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("锁", StringComparison.OrdinalIgnoreCase))
        {
            return "locking_clause";
        }

        if (message.Contains("函数", StringComparison.OrdinalIgnoreCase))
        {
            return "forbidden_function";
        }

        return "guardrail_rejected";
    }
}

public sealed record SqlLogMetadata(int Length, string Sha256);
