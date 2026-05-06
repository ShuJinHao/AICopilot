using System.Text;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;

namespace AICopilot.DataAnalysisService.Plugins;

internal static class DataAnalysisMetadataReader
{
    public static async Task<string> GetTableNamesAsync(
        IDatabaseConnector dbConnector,
        BusinessDatabase db,
        CancellationToken cancellationToken)
    {
        var sql = db.Provider switch
        {
            DbProviderType.PostgreSql => @"
                        SELECT
                            t.table_name AS ""TableName"",
                            obj_description(pgc.oid) AS ""Description""
                        FROM information_schema.tables t
                        INNER JOIN pg_class pgc ON t.table_name = pgc.relname
                        WHERE t.table_schema = 'public'
                          AND t.table_type = 'BASE TABLE';",
            DbProviderType.SqlServer => string.Empty,
            _ => null
        };

        if (sql is null)
        {
            return $"错误：不支持的数据库类型 {db.Provider}";
        }

        var result = await dbConnector.ExecuteQueryAsync(
            BusinessDatabaseContractMapper.ToConnectionInfo(db),
            sql,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return result.ToJson();
    }

    public static async Task<string> BuildTableSchemaAsync(
        IDatabaseConnector dbConnector,
        BusinessDatabase db,
        IReadOnlyCollection<string> tableNames,
        CancellationToken cancellationToken)
    {
        var ddlBuilder = new StringBuilder();

        foreach (var tableName in tableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columns = await GetColumnsAsync(dbConnector, db, tableName, cancellationToken);

            if (!columns.Any())
            {
                ddlBuilder.AppendLine($"-- 警告: 表 '{tableName}' 不存在或没有列。");
                continue;
            }

            ddlBuilder.AppendLine($"CREATE TABLE {tableName} (");

            var columnDefs = new List<string>();
            foreach (var col in columns)
            {
                var colDef = $"  {col.ColumnName} {col.DataType}";
                if (col.IsPrimaryKey)
                {
                    colDef += " PRIMARY KEY";
                }

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

        return ddlBuilder.ToString();
    }

    private static async Task<List<ColumnMetadata>> GetColumnsAsync(
        IDatabaseConnector dbConnector,
        BusinessDatabase db,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql = db.Provider switch
        {
            DbProviderType.PostgreSql => @"
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
            WHERE c.table_name = @TableName AND c.table_schema = 'public';",
            DbProviderType.SqlServer => string.Empty,
            _ => null
        };

        if (sql is null)
        {
            return [];
        }

        var result = await dbConnector.ExecuteQueryAsync(
            BusinessDatabaseContractMapper.ToConnectionInfo(db),
            sql,
            new { TableName = tableName },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var columns = new List<ColumnMetadata>();
        foreach (var row in result)
        {
            var dict = (IDictionary<string, object>)row;
            columns.Add(new ColumnMetadata(
                dict["ColumnName"] as string ?? string.Empty,
                dict["DataType"] as string ?? string.Empty,
                Convert.ToInt32(dict["IsPrimaryKey"]) == 1,
                dict["Description"] as string ?? string.Empty));
        }

        return columns;
    }
}

internal sealed record ColumnMetadata(
    string ColumnName,
    string DataType,
    bool IsPrimaryKey,
    string? Description);
