using System.Text;

namespace AICopilot.DataAnalysisService.Plugins;

internal static class DataAnalysisToolResultFormatter
{
    private const int MaxToolResultBytes = 256 * 1024;

    public static string BuildSafeFailureMessage(string prefix, Exception ex)
    {
        return ex switch
        {
            ArgumentException or InvalidOperationException => $"{prefix}: {ex.Message}",
            _ => $"{prefix}: 当前只读数据源暂时不可用，请稍后重试或联系管理员检查配置。"
        };
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
}
