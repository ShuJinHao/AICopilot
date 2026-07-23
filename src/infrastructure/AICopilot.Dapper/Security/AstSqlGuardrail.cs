using System.Text.RegularExpressions;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Sql;
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

    public (bool IsSafe, string? ErrorMessage) Validate(
        string sql,
        DatabaseProviderType provider,
        BusinessQuerySecurityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            profile.EnsureComplete();
        }
        catch (InvalidOperationException)
        {
            return (false, "安全拦截：业务数据源安全 profile 缺少显式 schema、table 或 column 范围。");
        }

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
        catch (Exception)
        {
            if (string.Equals(normalizedSql, sql, StringComparison.Ordinal))
            {
                return (false, "安全拦截：SQL 语句未通过安全语法解析。");
            }

            try
            {
                statements = new SqlQueryParser().Parse(normalizedSql, CreateDialect(provider));
            }
            catch
            {
                return (false, "安全拦截：SQL 语句未通过安全语法解析。");
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

        var structureError = ValidateQuery(selectStatement.Query, profile);
        if (!string.IsNullOrWhiteSpace(structureError))
        {
            return (false, structureError);
        }

        var identifierError = ValidateBlockedIdentifiers(normalizedSql, profile);
        if (!string.IsNullOrWhiteSpace(identifierError))
        {
            return (false, identifierError);
        }

        if (profile.AllowedColumns.Count > 0)
        {
            var columnError = SqlAllowlistColumnInspector.ValidateSelectColumns(
                normalizedSql,
                provider,
                profile.AllowedTables,
                profile.AllowedColumns);
            if (!string.IsNullOrWhiteSpace(columnError))
            {
                return (false, columnError);
            }
        }

        var functionError = ValidateForbiddenFunctions(normalizedSql);
        if (!string.IsNullOrWhiteSpace(functionError))
        {
            return (false, functionError);
        }

        return (true, null);
    }

    private static string? ValidateQuery(
        Query query,
        BusinessQuerySecurityProfile profile,
        IReadOnlySet<string>? inheritedCteNames = null)
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

        var cteNames = inheritedCteNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(inheritedCteNames, StringComparer.OrdinalIgnoreCase);

        if (query.With != null)
        {
            foreach (var cte in query.With.CteTables)
            {
                cteNames.Add(cte.Alias.Name.Value);
            }

            foreach (var cte in query.With.CteTables)
            {
                var cteError = ValidateQuery(cte.Query, profile, cteNames);
                if (!string.IsNullOrWhiteSpace(cteError))
                {
                    return cteError;
                }
            }
        }

        return ValidateSetExpression(query.Body, profile, cteNames);
    }

    private static string? ValidateSetExpression(
        SetExpression expression,
        BusinessQuerySecurityProfile profile,
        IReadOnlySet<string> cteNames)
    {
        return expression switch
        {
            SetExpression.SelectExpression selectExpression =>
                ValidateSelect(selectExpression.Select, profile, cteNames),
            SetExpression.QueryExpression queryExpression =>
                ValidateQuery(queryExpression.Query, profile, cteNames),
            _ => "安全拦截：仅允许单个 SELECT 查询，禁止 UNION、INTERSECT、VALUES 或其他集合操作。"
        };
    }

    private static string? ValidateSelect(
        Select select,
        BusinessQuerySecurityProfile profile,
        IReadOnlySet<string> cteNames)
    {
        if (select.Into != null)
        {
            return "安全拦截：禁止使用 SELECT INTO 写入临时表或目标表。";
        }

        if (select.Projection.Any(item =>
                item is SelectItem.Wildcard or SelectItem.QualifiedWildcard))
        {
            return "安全拦截：受治理业务查询禁止使用通配符投影。";
        }

        if (select.From == null)
        {
            return "安全拦截：受治理业务查询必须引用已授权业务表。";
        }

        foreach (var source in select.From)
        {
            var sourceError = ValidateTableWithJoins(source, profile, cteNames);
            if (!string.IsNullOrWhiteSpace(sourceError))
            {
                return sourceError;
            }
        }

        return null;
    }

    private static string? ValidateTableWithJoins(
        TableWithJoins tableWithJoins,
        BusinessQuerySecurityProfile profile,
        IReadOnlySet<string> cteNames)
    {
        var relationError = ValidateTableFactor(tableWithJoins.Relation, profile, cteNames);
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
            var joinError = ValidateTableFactor(join.Relation, profile, cteNames);
            if (!string.IsNullOrWhiteSpace(joinError))
            {
                return joinError;
            }
        }

        return null;
    }

    private static string? ValidateTableFactor(
        TableFactor? tableFactor,
        BusinessQuerySecurityProfile profile,
        IReadOnlySet<string> cteNames)
    {
        if (tableFactor == null)
        {
            return "安全拦截：查询中包含空的 FROM 结构。";
        }

        return tableFactor switch
        {
            TableFactor.Table table when table.Args == null && !table.WithOrdinality =>
                ValidateTableProfile(table.Name, profile, cteNames),
            TableFactor.Derived derived when derived.SubQuery != null =>
                ValidateQuery(derived.SubQuery, profile, cteNames),
            _ => "安全拦截：查询中包含未允许的 FROM 结构，当前仅支持普通表、视图和派生子查询。"
        };
    }

    private static string? ValidateTableProfile(
        ObjectName tableName,
        BusinessQuerySecurityProfile profile,
        IReadOnlySet<string> cteNames)
    {
        var identifiers = tableName.Values
            .Select(identifier => identifier.Value)
            .ToArray();
        if (identifiers.Length == 0)
        {
            return "安全拦截：查询表名不能为空。";
        }

        var table = identifiers[^1];
        if (identifiers.Length == 1 && cteNames.Contains(table))
        {
            return null;
        }

        if (IsSystemCatalog(identifiers))
        {
            return "安全拦截：禁止访问数据库系统目录。";
        }

        if (identifiers.Length != 2)
        {
            return "安全拦截：受治理业务查询必须使用 schema.table 全限定表名。";
        }

        var schema = identifiers[0];
        if (!profile.AllowedSchemas.Contains(schema))
        {
            return $"安全拦截：Schema '{schema}' 未被当前业务数据源授权。";
        }

        return profile.AllowedTables.Contains(table)
            ? null
            : $"Table '{table}' is not allowed for governed query execution.";
    }

    private static bool IsSystemCatalog(IReadOnlyCollection<string> identifiers)
    {
        return identifiers.Any(identifier =>
            identifier.Equals("information_schema", StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("pg_catalog", StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("pg_user", StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("pg_shadow", StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("sys", StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("mysql", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ValidateBlockedIdentifiers(
        string sql,
        BusinessQuerySecurityProfile profile)
    {
        var blocked = profile.BlockedIdentifierFragments.FirstOrDefault(fragment =>
            !string.IsNullOrWhiteSpace(fragment) &&
            Regex.IsMatch(
                sql,
                $@"\b{Regex.Escape(fragment)}[a-z0-9_]*\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        return blocked is null
            ? null
            : "安全拦截：查询引用了当前业务数据源禁止访问的敏感字段。";
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
