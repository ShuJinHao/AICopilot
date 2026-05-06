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
        bool readOnlyCredentialVerified = false)
    {
        ValidateInfo(name, description);
        ValidateConnection(connectionString, provider);
        ValidateSettings(isReadOnly, externalSystemType);

        Id = BusinessDatabaseId.New();
        Name = name.Trim();
        Description = description.Trim();
        ConnectionString = connectionString.Trim();
        Provider = provider;
        IsReadOnly = isReadOnly;
        ExternalSystemType = externalSystemType;
        ReadOnlyCredentialVerified = readOnlyCredentialVerified;
        IsEnabled = true;
        CreatedAt = DateTime.UtcNow;
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
        ValidateSettings(isReadOnly, externalSystemType);

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
        bool isReadOnly,
        BusinessDataExternalSystemType externalSystemType)
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
    }
}
