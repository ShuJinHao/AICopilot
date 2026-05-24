using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;

public enum DataSourcePermissionGrantTargetType
{
    User = 0,
    Role = 1,
    Department = 2
}

public sealed class DataSourcePermissionGrant : IAggregateRoot<DataSourcePermissionGrantId>
{
    private DataSourcePermissionGrant()
    {
    }

    public DataSourcePermissionGrant(
        BusinessDatabaseId dataSourceId,
        DataSourcePermissionGrantTargetType targetType,
        string targetValue,
        bool canQuery,
        bool canSchemaView,
        bool isEnabled = true)
    {
        Id = DataSourcePermissionGrantId.New();
        CreatedAt = DateTime.UtcNow;
        Update(
            dataSourceId,
            targetType,
            targetValue,
            canQuery,
            canSchemaView,
            isEnabled);
    }

    public DataSourcePermissionGrantId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public BusinessDatabaseId DataSourceId { get; private set; }

    public DataSourcePermissionGrantTargetType TargetType { get; private set; }

    public string TargetValue { get; private set; } = string.Empty;

    public bool CanQuery { get; private set; }

    public bool CanSchemaView { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public void Update(
        BusinessDatabaseId dataSourceId,
        DataSourcePermissionGrantTargetType targetType,
        string targetValue,
        bool canQuery,
        bool canSchemaView,
        bool isEnabled)
    {
        if (!Enum.IsDefined(typeof(DataSourcePermissionGrantTargetType), targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Data source permission target type is invalid.");
        }

        if (!canQuery && !canSchemaView)
        {
            throw new ArgumentException("Data source permission grant must allow query or schema view.");
        }

        DataSourceId = dataSourceId;
        TargetType = targetType;
        TargetValue = NormalizeTargetValue(targetValue);
        CanQuery = canQuery;
        CanSchemaView = canSchemaView;
        IsEnabled = isEnabled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Disable()
    {
        IsEnabled = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeTargetValue(string targetValue)
    {
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            throw new ArgumentException("Data source permission target value is required.", nameof(targetValue));
        }

        var normalized = targetValue.Trim();
        return normalized.Length > 160 ? normalized[..160] : normalized;
    }
}
