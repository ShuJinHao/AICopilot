using System.Globalization;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal static class SemanticSummaryFormatting
{
    public static string BuildBreakdown(
        IReadOnlyList<Dictionary<string, object?>> rows,
        string field,
        string unit)
    {
        var breakdown = rows
            .Select(row => GetString(row, field))
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} {group.Count()}{unit}")
            .ToArray();

        return breakdown.Length == 0 ? string.Empty : string.Join("，", breakdown);
    }

    public static string GetString(Dictionary<string, object?> row, string field)
    {
        return row.TryGetValue(field, out var value)
            ? value?.ToString() ?? "-"
            : "-";
    }

    public static decimal GetDecimal(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null)
        {
            return 0m;
        }

        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue),
            float floatValue => Convert.ToDecimal(floatValue),
            int intValue => intValue,
            long longValue => longValue,
            _ when decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0m
        };
    }

    public static bool GetBoolean(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null)
        {
            return false;
        }

        return value switch
        {
            bool boolValue => boolValue,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            _ when bool.TryParse(value.ToString(), out var parsedBool) => parsedBool,
            _ when int.TryParse(value.ToString(), out var parsedInt) => parsedInt != 0,
            _ => false
        };
    }

    public static DateTimeOffset? GetTimestamp(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null)
        {
            return null;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset;
        }

        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        }

        return DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    public static string FormatNumber(decimal value)
    {
        return decimal.Truncate(value) == value
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string FormatBoolean(bool value)
    {
        return value ? "是" : "否";
    }

    public static string FormatTimestamp(string rawValue)
    {
        return DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? FormatTimestamp(parsed)
            : rawValue;
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    public static VersionKey ParseVersionKey(string version)
    {
        var numbers = new List<int>();
        var current = string.Empty;
        foreach (var ch in version)
        {
            if (char.IsDigit(ch))
            {
                current += ch;
                continue;
            }

            if (current.Length > 0)
            {
                numbers.Add(int.Parse(current, CultureInfo.InvariantCulture));
                current = string.Empty;
            }
        }

        if (current.Length > 0)
        {
            numbers.Add(int.Parse(current, CultureInfo.InvariantCulture));
        }

        return new VersionKey(numbers);
    }
}

internal sealed record VersionKey(IReadOnlyList<int> Parts) : IComparable<VersionKey>
{
    public int CompareTo(VersionKey? other)
    {
        if (other is null)
        {
            return 1;
        }

        var maxLength = Math.Max(Parts.Count, other.Parts.Count);
        for (var index = 0; index < maxLength; index++)
        {
            var left = index < Parts.Count ? Parts[index] : 0;
            var right = index < other.Parts.Count ? other.Parts[index] : 0;
            var comparison = left.CompareTo(right);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
