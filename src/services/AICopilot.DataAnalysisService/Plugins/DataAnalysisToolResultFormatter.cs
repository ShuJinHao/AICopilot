using System.Text;

namespace AICopilot.DataAnalysisService.Plugins;

internal static class DataAnalysisToolResultFormatter
{
    private const int MaxToolResultBytes = 256 * 1024;

    public static string BuildSafeFailureMessage(string prefix, Exception ex)
    {
        var safeConfigurationMessage = ResolveSafeConfigurationMessage(ex.Message);
        return safeConfigurationMessage is not null
            ? $"{prefix}: {safeConfigurationMessage}"
            : $"{prefix}: 当前只读数据源暂时不可用，请稍后重试或联系管理员检查配置。";
    }

    public static string BuildSafeSqlRejectedMessage(Exception ex)
    {
        var rawMessageForClassification = ex.Message;
        return BuildSafeSqlRejectedMessage(rawMessageForClassification);
    }

    private static string BuildSafeSqlRejectedMessage(string? message)
    {
        return $"安全警告: 查询被系统拒绝。原因: {ResolveSafeSqlRejectedReason(message)}";
    }

    public static string ClassifySqlRejection(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown";
        }

        if (ResolveSafeConfigurationMessage(message) is not null)
        {
            return "configuration";
        }

        if (message.Contains("Only SELECT", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("仅允许", StringComparison.OrdinalIgnoreCase))
        {
            return "non_select";
        }

        if (message.Contains("Multiple", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("多条", StringComparison.OrdinalIgnoreCase))
        {
            return "multiple_statements";
        }

        if (message.Contains("语法解析", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("syntax", StringComparison.OrdinalIgnoreCase))
        {
            return "parse_error";
        }

        if (message.Contains("Sensitive", StringComparison.OrdinalIgnoreCase))
        {
            return "sensitive_field";
        }

        if (message.Contains("Wildcard", StringComparison.OrdinalIgnoreCase))
        {
            return "wildcard";
        }

        if (message.Contains("not allowed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Column '", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("治理", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("schema", StringComparison.OrdinalIgnoreCase))
        {
            return "governance_policy";
        }

        return "read_only_policy";
    }

    public static string Limit(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) <= MaxToolResultBytes)
        {
            return value;
        }

        const string suffix = "\n[系统提示] 工具输出过大，已截断为前 256KB 预览。";
        var builder = new StringBuilder(value.Length);
        var currentBytes = 0;
        foreach (var ch in value)
        {
            var charBytes = Encoding.UTF8.GetByteCount(ch.ToString());
            if (currentBytes + charBytes + Encoding.UTF8.GetByteCount(suffix) > MaxToolResultBytes)
            {
                break;
            }

            builder.Append(ch);
            currentBytes += charBytes;
        }

        builder.Append(suffix);
        return builder.ToString();
    }

    private static string ResolveSafeSqlRejectedReason(string? message)
    {
        var safeConfigurationMessage = ResolveSafeConfigurationMessage(message);
        if (safeConfigurationMessage is not null)
        {
            return safeConfigurationMessage;
        }

        return ClassifySqlRejection(message) switch
        {
            "non_select" => "仅允许 SELECT 或 WITH SELECT 只读查询。",
            "multiple_statements" => "禁止在单次调用中执行多条 SQL 语句。",
            "parse_error" => "SQL 未通过安全语法解析。",
            "sensitive_field" => "查询包含敏感字段，未通过治理白名单。",
            "wildcard" => "查询列未通过治理白名单，请显式选择允许字段。",
            "governance_policy" => "查询对象或字段不在治理白名单内。",
            _ => "查询未通过只读安全策略。"
        };
    }

    private static string? ResolveSafeConfigurationMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        if (message.Contains("未找到名称为", StringComparison.Ordinal) ||
            message.Contains("已被禁用", StringComparison.Ordinal) ||
            message.Contains("未配置为只读模式", StringComparison.Ordinal) ||
            message.Contains("数据源权限服务未配置", StringComparison.Ordinal) ||
            message.Contains("当前用户未获得该数据源的访问授权", StringComparison.Ordinal))
        {
            return message;
        }

        return null;
    }
}
