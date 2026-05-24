using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.DataAnalysisService.Plugins;

internal static class DataAnalysisDatabaseResolver
{
    public static async Task<BusinessDatabase> GetEnabledReadOnlyDatabaseAsync(
        IServiceProvider serviceProvider,
        string databaseName,
        DataSourceAccessPurpose accessPurpose,
        CancellationToken cancellationToken)
    {
        var repository = serviceProvider.GetRequiredService<IReadRepository<BusinessDatabase>>();
        var db = await repository.FirstOrDefaultAsync(new BusinessDatabaseByNameSpec(databaseName), cancellationToken);

        if (db == null)
        {
            throw new ArgumentException($"未找到名称为 '{databaseName}' 的数据库。请检查名称是否正确。");
        }

        if (!db.IsEnabled)
        {
            throw new InvalidOperationException($"数据库 '{databaseName}' 已被禁用。");
        }

        if (!db.IsReadOnly)
        {
            throw new InvalidOperationException($"数据库 '{databaseName}' 未配置为只读模式，系统已拒绝 AI 查询。");
        }

        var accessService = serviceProvider.GetService<BusinessDatabaseAccessService>();
        if (accessService is null)
        {
            throw new InvalidOperationException("数据源权限服务未配置，系统已按默认拒绝策略拦截。");
        }

        var authorized = accessPurpose switch
        {
            DataSourceAccessPurpose.Query => await accessService.CanQueryAsync(db, cancellationToken),
            DataSourceAccessPurpose.SchemaView => await accessService.CanSchemaViewAsync(db, cancellationToken),
            _ => false
        };
        if (!authorized)
        {
            throw new InvalidOperationException("当前用户未获得该数据源的访问授权。");
        }

        return db;
    }
}

internal enum DataSourceAccessPurpose
{
    SchemaView = 1,
    Query = 2
}
