using System.ComponentModel;
using AICopilot.AgentPlugin;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.DataAnalysisService.Plugins;

/// <summary>
/// 数据分析插件，作为 AI tool facade 暴露只读数据库元数据探索和查询能力。
/// </summary>
public class DataAnalysisPlugin(
    IDatabaseConnector dbConnector,
    ILogger<DataAnalysisPlugin> logger) : AgentPluginBase
{
    public override string Description => "提供数据库结构查询和SQL执行能力，用于回答涉及业务数据的统计分析问题。";

    [Description("获取指定数据库中所有表的名称和描述。这是探索数据库结构的第一步。")]
    public async Task<string> GetTableNamesAsync(
        IServiceProvider sp,
        [Description("目标数据库的名称")] string databaseName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = await DataAnalysisDatabaseResolver.GetEnabledReadOnlyDatabaseAsync(
                sp,
                databaseName,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await DataAnalysisMetadataReader.GetTableNamesAsync(
                dbConnector,
                db,
                cancellationToken);
            return DataAnalysisToolResultFormatter.Limit(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取表名失败。Database: {DbName}", databaseName);
            return DataAnalysisToolResultFormatter.Limit(
                DataAnalysisToolResultFormatter.BuildSafeFailureMessage("获取表名时发生错误", ex));
        }
    }

    [Description("获取指定表的详细结构定义(DDL)，包含列名、数据类型、主键和外键信息。")]
    public async Task<string> GetTableSchemaAsync(
        IServiceProvider sp,
        [Description("目标数据库的名称")] string databaseName,
        [Description("需要查询的表名列表，如 'Orders, Customers'")] string[] tableNames,
        CancellationToken cancellationToken = default)
    {
        if (tableNames.Length == 0)
        {
            return "错误：请提供至少一个表名。";
        }

        try
        {
            var db = await DataAnalysisDatabaseResolver.GetEnabledReadOnlyDatabaseAsync(
                sp,
                databaseName,
                cancellationToken);
            var result = await DataAnalysisMetadataReader.BuildTableSchemaAsync(
                dbConnector,
                db,
                tableNames,
                cancellationToken);
            return DataAnalysisToolResultFormatter.Limit(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取表结构失败。Database: {DbName}", databaseName);
            return DataAnalysisToolResultFormatter.Limit(
                DataAnalysisToolResultFormatter.BuildSafeFailureMessage("获取表结构时发生错误", ex));
        }
    }

    [Description("在指定数据库上执行查询 SQL 语句，并返回 JSON 格式的结果。")]
    public async Task<string> ExecuteSqlQueryAsync(
        IServiceProvider sp,
        [Description("目标数据库的名称")] string databaseName,
        [Description("要执行的 SQL 查询语句 (仅限 SELECT，不需要人类可读，去除换行符)")] string sqlQuery,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sqlQuery))
        {
            return "错误：SQL 语句不能为空。";
        }

        try
        {
            var db = await DataAnalysisDatabaseResolver.GetEnabledReadOnlyDatabaseAsync(
                sp,
                databaseName,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await DataAnalysisSqlQueryRunner.ExecuteAsync(
                sp,
                dbConnector,
                db,
                sqlQuery,
                cancellationToken);
            return DataAnalysisToolResultFormatter.Limit(result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("SQL 执行被拦截: {Message}", ex.Message);
            return DataAnalysisToolResultFormatter.Limit($"安全警告: 查询被系统拒绝。原因: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SQL 执行异常");
            return DataAnalysisToolResultFormatter.Limit("SQL 执行错误: 查询执行失败，请检查 SQL 语法、表名或列名是否正确。");
        }
    }
}
