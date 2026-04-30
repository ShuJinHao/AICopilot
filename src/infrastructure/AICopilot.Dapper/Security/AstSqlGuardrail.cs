using System.Text.RegularExpressions;
using AICopilot.Services.Contracts;
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace AICopilot.Dapper.Security;

/// <summary>
/// 基于 SQL AST 的只读查询防护，仅允许单条 SELECT/CTE 查询。
/// </summary>
public sealed partial class AstSqlGuardrail : ISqlGuardrail
{
    private static readonly HashSet<string> ForbiddenFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BENCHMARK",
        "DBMS_LOCK.SLEEP",
        "DBMS_PIPE.RECEIVE_MESSAGE",
        "DBMS_PIPE.SEND_MESSAGE",
        "Dblink_Connect",
        "Dblink_Exec",
        "Load_File",
        "Pg_Cancel_Backend",
        "Pg_Reload_Conf",
        "Pg_Sleep",
        "Pg_Terminate_Backend",
        "Sleep",
        "Sp_ExecuteSql",
        "Sys_Eval",
        "Sys_Exec",
        "Xp_CmdShell"
    };

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"--.*?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LineCommentRegex();

    public (bool IsSafe, string? ErrorMessage) Validate(string sql, DatabaseProviderType provider)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (false, "SQL 语句不能为空。");
        }

        var normalizedSql = NormalizeSql(sql);
        IReadOnlyList<Statement> statements;
        try
        {
            statements = new SqlQueryParser().Parse(sql, CreateDialect(provider));
        }
        catch (Exception ex)
        {
            if (string.Equals(normalizedSql, sql, StringComparison.Ordinal))
            {
                return (false, $"安全拦截：SQL 语句未通过语法解析。{ex.Message}");
            }

            try
            {
                statements = new SqlQueryParser().Parse(normalizedSql, CreateDialect(provider));
            }
            catch
            {
                return (false, $"安全拦截：SQL 语句未通过语法解析。{ex.Message}");
            }
        }

        if (statements.Count != 1)
        {
            return (false, "安全拦截：禁止在单次调用中执行多条 SQL 语句。");
        }

        if (statements[0] is not Statement.Select selectStatement)
        {
            return (false, "安全拦截：仅允许执行 SELECT 或 WITH ... SELECT 查询。");
        }

        var structureError = ValidateQuery(selectStatement.Query);
        if (!string.IsNullOrWhiteSpace(structureError))
        {
            return (false, structureError);
        }

        var functionError = ValidateForbiddenFunctions(normalizedSql);
        if (!string.IsNullOrWhiteSpace(functionError))
        {
            return (false, functionError);
        }

        return (true, null);
    }

    private static string? ValidateQuery(Query query)
    {
        if (query.ForClause != null)
        {
            return "安全拦截：禁止在只读查询中使用 FOR UPDATE / FOR SHARE 等锁定子句。";
        }

        if (HasItems(query.Locks))
        {
            return "安全拦截：禁止在只读查询中声明显式锁。";
        }

        if (HasItems(query.Settings))
        {
            return "安全拦截：禁止在只读查询中修改运行时设置。";
        }

        if (query.FormatClause != null)
        {
            return "安全拦截：禁止在只读查询中使用导出或格式化子句。";
        }

        if (query.With != null)
        {
            foreach (var cte in query.With.CteTables)
            {
                var cteError = ValidateQuery(cte.Query);
                if (!string.IsNullOrWhiteSpace(cteError))
                {
                    return cteError;
                }
            }
        }

        return ValidateSetExpression(query.Body);
    }

    private static string? ValidateSetExpression(SetExpression expression)
    {
        return expression switch
        {
            SetExpression.SelectExpression selectExpression => ValidateSelect(selectExpression.Select),
            SetExpression.QueryExpression queryExpression => ValidateQuery(queryExpression.Query),
            _ => "安全拦截：仅允许单个 SELECT 查询，禁止 UNION、INTERSECT、VALUES 或其他集合操作。"
        };
    }

    private static string? ValidateSelect(Select select)
    {
        if (select.Into != null)
        {
            return "安全拦截：禁止使用 SELECT INTO 写入临时表或目标表。";
        }

        if (select.From == null)
        {
            return null;
        }

        foreach (var source in select.From)
        {
            var sourceError = ValidateTableWithJoins(source);
            if (!string.IsNullOrWhiteSpace(sourceError))
            {
                return sourceError;
            }
        }

        return null;
    }

    private static string? ValidateTableWithJoins(TableWithJoins tableWithJoins)
    {
        var relationError = ValidateTableFactor(tableWithJoins.Relation);
        if (!string.IsNullOrWhiteSpace(relationError))
        {
            return relationError;
        }

        if (tableWithJoins.Joins == null)
        {
            return null;
        }

        foreach (var join in tableWithJoins.Joins)
        {
            var joinError = ValidateTableFactor(join.Relation);
            if (!string.IsNullOrWhiteSpace(joinError))
            {
                return joinError;
            }
        }

        return null;
    }

    private static string? ValidateTableFactor(TableFactor? tableFactor)
    {
        if (tableFactor == null)
        {
            return "安全拦截：查询中包含空的 FROM 结构。";
        }

        return tableFactor switch
        {
            TableFactor.Table table when table.Args == null && !table.WithOrdinality => null,
            TableFactor.Derived derived when derived.SubQuery != null => ValidateQuery(derived.SubQuery),
            _ => "安全拦截：查询中包含未允许的 FROM 结构，当前仅支持普通表、视图和派生子查询。"
        };
    }

    private static string? ValidateForbiddenFunctions(string sql)
    {
        foreach (var functionName in ForbiddenFunctionNames)
        {
            if (Regex.IsMatch(
                    sql,
                    $@"\b{Regex.Escape(functionName)}\s*\(",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return $"安全拦截：检测到不允许调用的系统函数 '{functionName}'。";
            }
        }

        return null;
    }

    private static string NormalizeSql(string sql)
    {
        var withoutBlockComments = BlockCommentRegex().Replace(sql, string.Empty);
        return LineCommentRegex().Replace(withoutBlockComments, string.Empty);
    }

    private static bool HasItems<T>(IEnumerable<T>? source)
    {
        return source?.Any() == true;
    }

    private static Dialect CreateDialect(DatabaseProviderType provider)
    {
        return provider switch
        {
            DatabaseProviderType.PostgreSql => new PostgreSqlDialect(),
            DatabaseProviderType.SqlServer => new MsSqlDialect(),
            DatabaseProviderType.MySql => new MySqlDialect(),
            _ => new GenericDialect()
        };
    }
}

