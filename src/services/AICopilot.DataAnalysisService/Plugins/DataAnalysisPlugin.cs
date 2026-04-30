using AICopilot.AgentPlugin;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Specifications.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.DataAnalysisService.Services;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Repository;
using AICopilot.Visualization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AICopilot.DataAnalysisService.Plugins;

// 用于映射元数据查询结果
// 用于映射元数据查询结果
public record ColumnMetadata
{
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsPrimaryKey { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// 数据分析插件
/// 提供数据库元数据探索和SQL执行能力，是Text-to-SQL的核心组件。
/// </summary>
public class DataAnalysisPlugin(
    IDatabaseConnector dbConnector,
    ILogger<DataAnalysisPlugin> logger) : AgentPluginBase
{
    private const int MaxToolResultBytes = 256 * 1024;
    private static readonly DatabaseQueryOptions QueryOptions = new(MaxRows: 200, CommandTimeoutSeconds: 15);

    public override string Description => "提供数据库结构查询和SQL执行能力，用于回答涉及业务数据的统计分析问题。";

    // 辅助方法：根据名称获取数据库配置
    // 这个方法不暴露给 AI，仅供内部使用
    private async Task<BusinessDatabase> GetDatabaseAsync(IServiceProvider sp, string databaseName, CancellationToken ct)
    {
        var repository = sp.GetRequiredService<IReadRepository<BusinessDatabase>>();
        var db = await repository.FirstOrDefaultAsync(new BusinessDatabaseByNameSpec(databaseName), ct);

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

    [Description("获取指定数据库中所有表的名称和描述。这是探索数据库结构的第一步。")]
    public async Task<string> GetTableNamesAsync(
        IServiceProvider sp,
        [Description("目标数据库的名称")] string databaseName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 获取数据库配置
            var db = await GetDatabaseAsync(sp, databaseName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 根据数据库类型构建查询元数据的 SQL
            var sql = string.Empty;
            switch (db.Provider)
            {
                case DbProviderType.PostgreSql:
                    // PostgreSQL: 从 information_schema 获取表名，关联 pg_description 获取注释
                    sql = @"
                        SELECT
                            t.table_name AS ""TableName"",
                            obj_description(pgc.oid) AS ""Description""
                        FROM information_schema.tables t
                        INNER JOIN pg_class pgc ON t.table_name = pgc.relname
                        WHERE t.table_schema = 'public'
                          AND t.table_type = 'BASE TABLE';";
                    break;

                case DbProviderType.SqlServer:
                    // SQL Server
                    break;

                default:
                    return $"错误：不支持的数据库类型 {db.Provider}";
            }

            // 执行查询
            // 这里使用了基础设施层的 ExecuteQueryAsync，它返回 IEnumerable<dynamic>
            var result = await dbConnector.ExecuteQueryAsync(BusinessDatabaseContractMapper.ToConnectionInfo(db), sql, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 序列化结果
            return LimitToolResult(result.ToJson());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取表名失败。Database: {DbName}", databaseName);
            return LimitToolResult(BuildSafeFailureMessage("获取表名时发生错误", ex));
        }
    }

    // 内部辅助方法：查询单个表的列元数据
    private async Task<List<ColumnMetadata>> GetColumnsAsync(
        BusinessDatabase db,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql = string.Empty;
        switch (db.Provider)
        {
            case DbProviderType.PostgreSql:
                // PostgreSQL 元数据查询
                // 包含列名、类型、是否主键
                // 注意：此处简化了查询，实际生产中可能需要更复杂的关联来获取外键
                sql = @"
            SELECT
                c.column_name AS ""ColumnName"",
                c.data_type AS ""DataType"",
                CASE WHEN tc.constraint_type = 'PRIMARY KEY' THEN 1 ELSE 0 END AS ""IsPrimaryKey"",
                pg_catalog.col_description(format('%s.%s', c.table_schema, c.table_name)::regclass::oid, c.ordinal_position) AS ""Description""
            FROM information_schema.columns c
            LEFT JOIN information_schema.key_column_usage kcu
                ON c.table_name = kcu.table_name AND c.column_name = kcu.column_name
            LEFT JOIN information_schema.table_constraints tc
                ON kcu.constraint_name = tc.constraint_name AND tc.constraint_type = 'PRIMARY KEY'
            WHERE c.table_name = @TableName AND c.table_schema = 'public';";
                break;

            case DbProviderType.SqlServer:
                // SQL Server 元数据查询
                break;

            default:
                return [];
        }

        var result = await dbConnector.ExecuteQueryAsync(BusinessDatabaseContractMapper.ToConnectionInfo(db), sql, new { TableName = tableName }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Dapper 返回的是 dynamic，需要手动映射到强类型
        var columns = new List<ColumnMetadata>();
        foreach (var row in result)
        {
            var dict = (IDictionary<string, object>)row;
            columns.Add(new ColumnMetadata
            {
                ColumnName = dict["ColumnName"] as string ?? "",
                DataType = dict["DataType"] as string ?? "",
                IsPrimaryKey = Convert.ToInt32(dict["IsPrimaryKey"]) == 1,
                Description = dict["Description"] as string ?? ""
            });
        }

        return columns;
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
            var db = await GetDatabaseAsync(sp, databaseName, cancellationToken);
            var ddlBuilder = new StringBuilder();

            foreach (var tableName in tableNames)
            {
                // 1. 查询列信息
                cancellationToken.ThrowIfCancellationRequested();
                var columns = await GetColumnsAsync(db, tableName, cancellationToken);

                if (!columns.Any())
                {
                    ddlBuilder.AppendLine($"-- 警告: 表 '{tableName}' 不存在或没有列。");
                    continue;
                }

                // 2. 构建 CREATE TABLE 语句
                ddlBuilder.AppendLine($"CREATE TABLE {tableName} (");

                var columnDefs = new List<string>();
                foreach (var col in columns)
                {
                    // 格式: ColumnName DataType [PK/FK] [Comment]
                    var colDef = $"  {col.ColumnName} {col.DataType}";

                    if (col.IsPrimaryKey) colDef += " PRIMARY KEY";

                    // 如果有描述，作为注释添加，帮助 AI 理解字段含义
                    if (!string.IsNullOrWhiteSpace(col.Description))
                    {
                        colDef += $" -- {col.Description}";
                    }

                    columnDefs.Add(colDef);
                }

                ddlBuilder.AppendLine(string.Join(",\n", columnDefs));
                ddlBuilder.AppendLine(");");
                ddlBuilder.AppendLine();
            }

            return LimitToolResult(ddlBuilder.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取表结构失败。Database: {DbName}", databaseName);
            return LimitToolResult(BuildSafeFailureMessage("获取表结构时发生错误", ex));
        }
    }

    [Description("在指定数据库上执行查询 SQL 语句，并返回 JSON 格式的结果。")]
    public async Task<string> ExecuteSqlQueryAsync(
        IServiceProvider sp,
        [Description("目标数据库的名称")] string databaseName,
        [Description("要执行的 SQL 查询语句 (仅限 SELECT，不需要人类可读，去除换行符)")] string sqlQuery,
        CancellationToken cancellationToken = default)
    {
        // 1. 基础校验
        if (string.IsNullOrWhiteSpace(sqlQuery)) return "错误：SQL 语句不能为空。";

        try
        {
            var db = await GetDatabaseAsync(sp, databaseName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 2. 执行查询
            var queryResult = await dbConnector.ExecuteQueryWithMetadataAsync(BusinessDatabaseContractMapper.ToConnectionInfo(db), sqlQuery,
                options: QueryOptions,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var data = queryResult.Rows.ToList();
            var firstRow = data.FirstOrDefault();
            var schema = new List<SchemaColumn>();

            if (firstRow != null)
            {
                foreach (var kvp in firstRow)
                {
                    // 获取值的运行时类型，如果为 null 则默认为 object
                    var type = kvp.Value?.GetType() ?? typeof(object);
                    schema.Add(new SchemaColumn(kvp.Key, type));
                }
            }

            // 3. 【关键步骤】将原始结果捕获到上下文中
            var vizContext = sp.GetRequiredService<VisualizationContext>();
            vizContext.CaptureResult(data, schema);

            var auditLogWriter = sp.GetRequiredService<IAuditLogWriter>();
            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    "DataAnalysis",
                    "DataAnalysis.ExecuteFreeSqlQuery",
                    "BusinessDatabase",
                    db.Id.ToString(),
                    db.Name,
                    AuditResults.Succeeded,
                    $"自由 SQL 查询已执行。Rows={queryResult.ReturnedRowCount}; Truncated={queryResult.IsTruncated}; ElapsedMs={queryResult.ElapsedMilliseconds}."));
            await auditLogWriter.SaveChangesAsync(cancellationToken);

            if (data.Count == 0)
            {
                return LimitToolResult("查询执行成功，但未返回任何结果 (0 rows)。");
            }

            var preview = data.Take(5).ToJson();
            if (queryResult.IsTruncated)
            {
                return LimitToolResult($"查询执行成功，结果已截断。共返回 {queryResult.ReturnedRowCount} 行，当前仅保留前 {data.Count} 行用于后续分析。预览数据：{preview}");
            }

            return LimitToolResult(preview);
        }
        catch (InvalidOperationException ex) // 捕获安全拦截异常
        {
            logger.LogWarning("SQL 执行被拦截: {Message}", ex.Message);
            return LimitToolResult($"安全警告: 查询被系统拒绝。原因: {ex.Message}");
        }
        catch (Exception ex)
        {
            // 这里是 ReAct 模式中“错误自愈”的关键！
            // 我们必须返回详细的数据库错误信息（如 "Column 'xxx' not found"），
            // 这样 Agent 才能看到错误 -> 思考原因 -> 修正 SQL -> 重试。
            logger.LogError(ex, "SQL 执行异常");
            return LimitToolResult("SQL 执行错误: 查询执行失败，请检查 SQL 语法、表名或列名是否正确。");
        }
    }

    private static string BuildSafeFailureMessage(string prefix, Exception ex)
    {
        return ex switch
        {
            ArgumentException or InvalidOperationException => $"{prefix}: {ex.Message}",
            _ => $"{prefix}: 当前只读数据源暂时不可用，请稍后重试或联系管理员检查配置。"
        };
    }

    private static string LimitToolResult(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) <= MaxToolResultBytes)
        {
            return value;
        }

        const string suffix = "\n[系统提示] 工具输出过大，已截断为前 256KB 预览。";
        var builder = new StringBuilder(value.Length);
        var currentBytes = 0;
        foreach (var ch in value)
        {
            var charBytes = Encoding.UTF8.GetByteCount(ch.ToString());
            if (currentBytes + charBytes + Encoding.UTF8.GetByteCount(suffix) > MaxToolResultBytes)
            {
                break;
            }

            builder.Append(ch);
            currentBytes += charBytes;
        }

        builder.Append(suffix);
        return builder.ToString();
    }
}

