using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessDatabaseSafetyValidator
{
    public static string? Validate(
        DbProviderType provider,
        bool isEnabled,
        bool isReadOnly,
        DataSourceExternalSystemType externalSystemType,
        bool readOnlyCredentialVerified,
        int defaultQueryLimit = 200,
        int maxQueryLimit = 1000)
    {
        if (!Enum.IsDefined(typeof(DataSourceExternalSystemType), externalSystemType))
        {
            return "业务库外部系统类型无效。";
        }

        if (!isReadOnly)
        {
            return "业务库必须配置为只读，AICopilot 不允许保存可写业务库。";
        }

        if (defaultQueryLimit <= 0 || maxQueryLimit <= 0)
        {
            return "业务库查询行数限制必须大于 0。";
        }

        if (defaultQueryLimit > maxQueryLimit)
        {
            return "业务库默认查询行数不能大于最大查询行数。";
        }

        if (maxQueryLimit > 10000)
        {
            return "业务库最大查询行数不能超过 10000。";
        }

        if (!isEnabled)
        {
            return null;
        }

        if (externalSystemType == DataSourceExternalSystemType.CloudReadOnly && !readOnlyCredentialVerified)
        {
            return "Cloud 只读数据源启用前必须确认数据库账号已按只读权限验证。";
        }

        if (provider != DbProviderType.PostgreSql && !readOnlyCredentialVerified)
        {
            return "SQL Server/MySQL 数据源启用前必须确认数据库账号已按只读权限验证。";
        }

        return null;
    }
}
