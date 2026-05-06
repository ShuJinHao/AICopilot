using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.DataAnalysisService.Plugins;

internal static class DataAnalysisDatabaseResolver
{
    public static async Task<BusinessDatabase> GetEnabledReadOnlyDatabaseAsync(
        IServiceProvider serviceProvider,
        string databaseName,
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

        return db;
    }
}
