using System.Globalization;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReportFormatting
{
    public const int PreviewRowLimit = 20;
    public const int HtmlRowLimit = 50;

    public static string BuildSourceMarker(AgentReportSourceInfo? sourceInfo)
    {
        if (sourceInfo is null)
        {
            return "sourceMode=Local; isSimulation=false; sourceLabel=Local workspace data";
        }

        var queryHash = string.IsNullOrWhiteSpace(sourceInfo.QueryHash) ? string.Empty : $"; queryHash={sourceInfo.QueryHash}";
        return $"sourceMode={sourceInfo.SourceMode ?? "Unknown"}; isSimulation={FormatBool(sourceInfo.IsSimulation)}; sourceLabel={sourceInfo.SourceLabel ?? string.Empty}; rowCount={sourceInfo.RowCount}; truncated={FormatBool(sourceInfo.IsTruncated)}{queryHash}";
    }

    public static bool TryParseNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double doubleValue:
                number = doubleValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                return TryParseNumber(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, out number);
        }
    }

    public static bool TryParseNumber(string value, out double number)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
               || double.TryParse(value, out number);
    }

    public static string FormatCellValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolValue => FormatBool(boolValue),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    public static string FormatChartLabel(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            bool boolValue => FormatBool(boolValue),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    public static string SafeShaPrefix(string sha256)
    {
        return sha256.Length <= 12 ? sha256 : sha256[..12];
    }

    public static string EscapeMarkdownCell(string value)
    {
        return (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }

    public static string EscapeHtml(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    public static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }
}
