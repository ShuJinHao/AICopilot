namespace AICopilot.Services.Contracts;

public sealed record CloudReadOnlyJoinHint(
    string LeftTable,
    string LeftColumn,
    string RightTable,
    string RightColumn);

public static class CloudReadOnlyGovernedSchema
{
    public static readonly IReadOnlySet<string> AllowedTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "devices",
            "mfg_processes",
            "device_logs",
            "hourly_capacity",
            "pass_station_records"
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["devices"] = ColumnSet("id", "client_code", "device_name", "process_id"),
            ["mfg_processes"] = ColumnSet("id", "process_name"),
            ["device_logs"] = ColumnSet("id", "device_id", "level", "message", "log_time"),
            ["hourly_capacity"] = ColumnSet("id", "device_id", "date", "total_count", "ok_count", "reported_at"),
            ["pass_station_records"] = ColumnSet("id", "device_id", "barcode", "type_key", "cell_result", "completed_time")
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllowedColumnTypes =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["devices"] = ColumnTypes(
                ("id", "uuid"),
                ("client_code", "varchar"),
                ("device_name", "varchar"),
                ("process_id", "uuid")),
            ["mfg_processes"] = ColumnTypes(
                ("id", "uuid"),
                ("process_name", "varchar")),
            ["device_logs"] = ColumnTypes(
                ("id", "uuid"),
                ("device_id", "uuid"),
                ("level", "varchar"),
                ("message", "text"),
                ("log_time", "timestamptz")),
            ["hourly_capacity"] = ColumnTypes(
                ("id", "uuid"),
                ("device_id", "uuid"),
                ("date", "date"),
                ("total_count", "integer"),
                ("ok_count", "integer"),
                ("reported_at", "timestamptz")),
            ["pass_station_records"] = ColumnTypes(
                ("id", "uuid"),
                ("device_id", "uuid"),
                ("barcode", "varchar"),
                ("type_key", "varchar"),
                ("cell_result", "varchar"),
                ("completed_time", "timestamp"))
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllowedColumnValueHints =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["device_logs"] = ColumnTypes(
                ("level", "Allowed values are ERROR, WARN, INFO. Use uppercase exact values in filters."))
        };

    public static readonly IReadOnlyList<CloudReadOnlyJoinHint> JoinHints =
    [
        new("device_logs", "device_id", "devices", "id"),
        new("devices", "process_id", "mfg_processes", "id"),
        new("hourly_capacity", "device_id", "devices", "id"),
        new("pass_station_records", "device_id", "devices", "id")
    ];

    public static readonly IReadOnlyList<string> BlockedFieldFragments =
    [
        "api_key",
        "apikey",
        "bootstrap_secret",
        "connection_string",
        "credential",
        "password",
        "secret",
        "security_stamp",
        "token"
    ];

    public static bool IsAllowedTable(string? tableName)
    {
        return !string.IsNullOrWhiteSpace(tableName) && AllowedTables.Contains(tableName);
    }

    public static bool IsAllowedColumn(string? tableName, string? columnName)
    {
        return !string.IsNullOrWhiteSpace(tableName) &&
               !string.IsNullOrWhiteSpace(columnName) &&
               AllowedColumns.TryGetValue(tableName, out var columns) &&
               columns.Contains(columnName);
    }

    public static bool IsSensitiveIdentifier(string? identifier)
    {
        return !string.IsNullOrWhiteSpace(identifier) &&
               BlockedFieldFragments.Any(fragment =>
                   identifier.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlySet<string> ColumnSet(params string[] columns)
    {
        return columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> ColumnTypes(params (string Column, string Type)[] columnTypes)
    {
        return columnTypes.ToDictionary(
            item => item.Column,
            item => item.Type,
            StringComparer.OrdinalIgnoreCase);
    }
}
