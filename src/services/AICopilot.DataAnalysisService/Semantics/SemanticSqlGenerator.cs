using AICopilot.Services.Contracts;
using System.Text;
using System.Text.RegularExpressions;

namespace AICopilot.DataAnalysisService.Semantics;

public sealed class SemanticSqlGenerator(
    ISqlGuardrail sqlGuardrail) : ISemanticSqlGenerator
{
    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_\\.]*$", RegexOptions.Compiled);
    private static readonly string[] UnsafeFromClauseTokens = [";", "--", "/*", "*/"];

    public GeneratedSemanticSql Generate(SemanticQueryPlan plan, SemanticPhysicalMapping mapping)
    {
        if (plan.Target != mapping.Target)
        {
            throw new InvalidOperationException($"Query target {plan.Target} does not match mapping target {mapping.Target}.");
        }

        ValidateFromClause(mapping);

        var selectSegments = new List<string>();
        foreach (var field in plan.Projection.Fields)
        {
            if (!mapping.IsProjectionFieldAllowed(field))
            {
                throw new InvalidOperationException($"Field '{field}' is not in the projection whitelist.");
            }

            selectSegments.Add($"{ResolveColumn(mapping, field)} AS {field}");
        }

        if (selectSegments.Count == 0)
        {
            throw new InvalidOperationException("Semantic query does not contain any projection fields.");
        }

        var whereSegments = new List<string>();
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var parameterIndex = 0;

        foreach (var defaultFilter in mapping.DefaultFilters)
        {
            whereSegments.Add(BuildFilterClause(mapping, defaultFilter, parameters, ref parameterIndex));
        }

        foreach (var filter in plan.Filters)
        {
            if (!mapping.IsFilterFieldAllowed(filter.Field))
            {
                throw new InvalidOperationException($"Field '{filter.Field}' is not in the filter whitelist.");
            }

            whereSegments.Add(BuildFilterClause(mapping, filter, parameters, ref parameterIndex));
        }

        if (plan.TimeRange != null)
        {
            if (!mapping.FieldMappings.TryGetValue(plan.TimeRange.Field, out var timeColumn))
            {
                throw new InvalidOperationException($"Time range field '{plan.TimeRange.Field}' is not mapped.");
            }

            ValidateIdentifier(timeColumn, "Time range field");
            if (plan.TimeRange.Start.HasValue)
            {
                var parameterName = $"@p{parameterIndex++}";
                parameters[parameterName] = plan.TimeRange.Start.Value.UtcDateTime;
                whereSegments.Add($"t.{timeColumn} >= {parameterName}");
            }

            if (plan.TimeRange.End.HasValue)
            {
                var parameterName = $"@p{parameterIndex++}";
                parameters[parameterName] = plan.TimeRange.End.Value.UtcDateTime;
                whereSegments.Add($"t.{timeColumn} <= {parameterName}");
            }
        }

        var sort = plan.Sort ?? mapping.DefaultSort;
        if (sort != null && !mapping.IsSortFieldAllowed(sort.Field))
        {
            throw new InvalidOperationException($"Field '{sort.Field}' is not in the sort whitelist.");
        }

        var sql = new StringBuilder();
        if (mapping.Provider == DatabaseProviderType.SqlServer)
        {
            sql.Append($"SELECT TOP ({plan.Limit}) ");
        }
        else
        {
            sql.Append("SELECT ");
        }

        sql.Append(string.Join(", ", selectSegments));
        sql.Append(" FROM ");
        sql.Append(ResolveFromClause(mapping));

        if (whereSegments.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", whereSegments));
        }

        if (sort != null)
        {
            sql.Append(" ORDER BY ");
            sql.Append($"{ResolveColumn(mapping, sort.Field)} {(sort.Direction == SemanticSortDirection.Desc ? "DESC" : "ASC")}");
        }

        if (mapping.Provider != DatabaseProviderType.SqlServer)
        {
            sql.Append($" LIMIT {plan.Limit}");
        }

        var sqlText = sql.ToString();
        var guardResult = sqlGuardrail.Validate(sqlText, mapping.Provider);
        if (!guardResult.IsSafe)
        {
            throw new InvalidOperationException(guardResult.ErrorMessage);
        }

        return new GeneratedSemanticSql(sqlText, parameters);
    }

    private static string BuildFilterClause(
        SemanticPhysicalMapping mapping,
        SemanticFilter filter,
        IDictionary<string, object?> parameters,
        ref int parameterIndex)
    {
        var column = ResolveColumn(mapping, filter.Field);
        return filter.Operator switch
        {
            SemanticFilterOperator.Equal => CreateBinaryClause(column, "=", filter.Value, parameters, ref parameterIndex),
            SemanticFilterOperator.Contains => CreateBinaryClause(column, "LIKE", $"%{filter.Value}%", parameters, ref parameterIndex),
            SemanticFilterOperator.GreaterOrEqual => CreateBinaryClause(column, ">=", filter.Value, parameters, ref parameterIndex),
            SemanticFilterOperator.LessOrEqual => CreateBinaryClause(column, "<=", filter.Value, parameters, ref parameterIndex),
            SemanticFilterOperator.In => CreateInClause(column, filter.Value, parameters, ref parameterIndex),
            _ => throw new NotSupportedException($"Unsupported filter operator: {filter.Operator}")
        };
    }

    private static string CreateBinaryClause(
        string column,
        string operatorText,
        object value,
        IDictionary<string, object?> parameters,
        ref int parameterIndex)
    {
        var parameterName = $"@p{parameterIndex++}";
        parameters[parameterName] = value;
        return $"{column} {operatorText} {parameterName}";
    }

    private static string CreateInClause(
        string column,
        string value,
        IDictionary<string, object?> parameters,
        ref int parameterIndex)
    {
        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
        {
            throw new InvalidOperationException("IN filter requires at least one value.");
        }

        var parameterNames = new List<string>();
        foreach (var item in values)
        {
            var parameterName = $"@p{parameterIndex++}";
            parameters[parameterName] = item;
            parameterNames.Add(parameterName);
        }

        return $"{column} IN ({string.Join(", ", parameterNames)})";
    }

    private static string ResolveColumn(SemanticPhysicalMapping mapping, string field)
    {
        if (!mapping.FieldMappings.TryGetValue(field, out var column))
        {
            throw new InvalidOperationException($"Field '{field}' does not have a physical mapping.");
        }

        ValidateIdentifier(column, "Column");
        return string.IsNullOrWhiteSpace(mapping.FromClause)
            ? $"t.{column}"
            : column;
    }

    private static string ResolveFromClause(SemanticPhysicalMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.FromClause))
        {
            ValidateIdentifier(mapping.SourceName, "Source");
            return $"{mapping.SourceName} t";
        }

        foreach (var token in UnsafeFromClauseTokens)
        {
            if (mapping.FromClause.Contains(token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"FROM clause contains unsafe token '{token}'.");
            }
        }

        return mapping.FromClause;
    }

    private static void ValidateFromClause(SemanticPhysicalMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.FromClause))
        {
            ValidateIdentifier(mapping.SourceName, "Source");
        }
    }

    private static void ValidateIdentifier(string identifier, string kind)
    {
        if (!IdentifierRegex.IsMatch(identifier))
        {
            throw new InvalidOperationException($"{kind} '{identifier}' is not a valid SQL identifier.");
        }
    }
}


