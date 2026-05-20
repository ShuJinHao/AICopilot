using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.SharedKernel.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;

/// <summary>
/// 业务数据库聚合根
/// 代表一个可被AI Agent访问的外部数据源
/// </summary>
public class BusinessDatabase : IAggregateRoot<BusinessDatabaseId>
{
    protected BusinessDatabase()
    {
    }

    public BusinessDatabase(
        string name,
        string description,
        string connectionString,
        DbProviderType provider,
        bool isReadOnly = true,
        BusinessDataExternalSystemType externalSystemType = BusinessDataExternalSystemType.Unknown,
        bool readOnlyCredentialVerified = false,
        bool isEnabled = true,
        string? category = null,
        IEnumerable<string>? tags = null,
        string? ownerDepartment = null,
        string? businessDomain = null,
        string? sensitivityLevel = null,
        int defaultQueryLimit = 200,
        int maxQueryLimit = 1000,
        bool isSelectableInChat = true,
        bool isSelectableInAgent = true)
    {
        ValidateInfo(name, description);
        ValidateConnection(connectionString, provider);
        ValidateSettings(
            isEnabled,
            isReadOnly,
            externalSystemType,
            readOnlyCredentialVerified);
        ValidateGovernance(defaultQueryLimit, maxQueryLimit);

        Id = BusinessDatabaseId.New();
        Name = name.Trim();
        Description = description.Trim();
        ConnectionString = connectionString.Trim();
        Provider = provider;
        IsReadOnly = isReadOnly;
        ExternalSystemType = externalSystemType;
        ReadOnlyCredentialVerified = readOnlyCredentialVerified;
        IsEnabled = isEnabled;
        CreatedAt = DateTime.UtcNow;
        Category = NormalizeOptionalText(category, "General");
        Tags = NormalizeTags(tags);
        OwnerDepartment = NormalizeOptionalText(ownerDepartment, string.Empty);
        BusinessDomain = NormalizeOptionalText(businessDomain, string.Empty);
        SensitivityLevel = NormalizeOptionalText(sensitivityLevel, "Internal");
        DefaultQueryLimit = defaultQueryLimit;
        MaxQueryLimit = maxQueryLimit;
        IsSelectableInChat = isSelectableInChat;
        IsSelectableInAgent = isSelectableInAgent;
    }

    public BusinessDatabaseId Id { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// 数据库标识名称
    /// 用于在多库路由时作为唯一Key
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// 数据库业务描述 (如: "包含所有销售订单、客户资料及发货记录")
    /// 该字段将被注入到System Prompt中，辅助LLM进行意图路由判断
    /// </summary>
    public string Description { get; private set; } = null!;

    /// <summary>
    /// 连接字符串
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// 数据库类型
    /// </summary>
    public DbProviderType Provider { get; private set; }

    /// <summary>
    /// 是否只允许只读查询
    /// </summary>
    public bool IsReadOnly { get; private set; }

    public BusinessDataExternalSystemType ExternalSystemType { get; private set; }

    public bool ReadOnlyCredentialVerified { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public string Category { get; private set; } = "General";

    public string Tags { get; private set; } = string.Empty;

    public string OwnerDepartment { get; private set; } = string.Empty;

    public string BusinessDomain { get; private set; } = string.Empty;

    public string SensitivityLevel { get; private set; } = "Internal";

    public int DefaultQueryLimit { get; private set; } = 200;

    public int MaxQueryLimit { get; private set; } = 1000;

    public bool IsSelectableInChat { get; private set; } = true;

    public bool IsSelectableInAgent { get; private set; } = true;

    /// <summary>
    /// 更新连接信息
    /// </summary>
    public void UpdateConnection(string connectionString, DbProviderType provider)
    {
        ValidateConnection(connectionString, provider);

        ConnectionString = connectionString.Trim();
        Provider = provider;
    }

    public void UpdateSettings(
        bool isEnabled,
        bool isReadOnly,
        BusinessDataExternalSystemType externalSystemType = BusinessDataExternalSystemType.Unknown,
        bool readOnlyCredentialVerified = false)
    {
        ValidateSettings(
            isEnabled,
            isReadOnly,
            externalSystemType,
            readOnlyCredentialVerified);

        IsEnabled = isEnabled;
        IsReadOnly = isReadOnly;
        ExternalSystemType = externalSystemType;
        ReadOnlyCredentialVerified = readOnlyCredentialVerified;
    }

    /// <summary>
    /// 更新描述信息
    /// </summary>
    public void UpdateInfo(string name, string description)
    {
        ValidateInfo(name, description);

        Name = name.Trim();
        Description = description.Trim();
    }

    public void UpdateGovernance(
        string? category,
        IEnumerable<string>? tags,
        string? ownerDepartment,
        string? businessDomain,
        string? sensitivityLevel,
        int defaultQueryLimit,
        int maxQueryLimit,
        bool isSelectableInChat,
        bool isSelectableInAgent)
    {
        ValidateGovernance(defaultQueryLimit, maxQueryLimit);

        Category = NormalizeOptionalText(category, "General");
        Tags = NormalizeTags(tags);
        OwnerDepartment = NormalizeOptionalText(ownerDepartment, string.Empty);
        BusinessDomain = NormalizeOptionalText(businessDomain, string.Empty);
        SensitivityLevel = NormalizeOptionalText(sensitivityLevel, "Internal");
        DefaultQueryLimit = defaultQueryLimit;
        MaxQueryLimit = maxQueryLimit;
        IsSelectableInChat = isSelectableInChat;
        IsSelectableInAgent = isSelectableInAgent;
    }

    private static void ValidateInfo(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Business database name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Business database description is required.", nameof(description));
        }
    }

    private static void ValidateConnection(string connectionString, DbProviderType provider)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Business database connection string is required.", nameof(connectionString));
        }

        if (!Enum.IsDefined(typeof(DbProviderType), provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Business database provider is invalid.");
        }
    }

    private static void ValidateSettings(
        bool isEnabled,
        bool isReadOnly,
        BusinessDataExternalSystemType externalSystemType,
        bool readOnlyCredentialVerified)
    {
        if (!Enum.IsDefined(typeof(BusinessDataExternalSystemType), externalSystemType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(externalSystemType),
                externalSystemType,
                "Business data external system type is invalid.");
        }

        if (externalSystemType == BusinessDataExternalSystemType.CloudReadOnly && !isReadOnly)
        {
            throw new InvalidOperationException("Cloud read-only data source must be configured as read-only.");
        }

        if (externalSystemType == BusinessDataExternalSystemType.SimulationBusiness && !isReadOnly)
        {
            throw new InvalidOperationException("Simulation business data source must be configured as read-only.");
        }

        if (isEnabled
            && externalSystemType == BusinessDataExternalSystemType.CloudReadOnly
            && !readOnlyCredentialVerified)
        {
            throw new InvalidOperationException(
                "Enabled Cloud read-only data source must have a verified read-only credential.");
        }
    }

    private static void ValidateGovernance(int defaultQueryLimit, int maxQueryLimit)
    {
        if (defaultQueryLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultQueryLimit), "Default query limit must be positive.");
        }

        if (maxQueryLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueryLimit), "Max query limit must be positive.");
        }

        if (defaultQueryLimit > maxQueryLimit)
        {
            throw new ArgumentException("Default query limit cannot exceed max query limit.", nameof(defaultQueryLimit));
        }

        if (maxQueryLimit > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueryLimit), "Max query limit cannot exceed 10000 rows.");
        }
    }

    private static string NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(32));
    }

    private static string NormalizeOptionalText(string? value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }
}
