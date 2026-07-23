using System.Text.RegularExpressions;
using AICopilot.Services.Contracts;
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace AICopilot.Services.CrossCutting.Sql;

public static partial class SqlAllowlistColumnInspector
{
    private static readonly IReadOnlySet<string> AllowedBusinessQueryFunctions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AVG",
            "COALESCE",
            "COUNT",
            "DATE_PART",
            "DATE_TRUNC",
            "MAX",
            "MIN",
            "NULLIF",
            "ROUND",
            "SUM"
        };

    private static readonly IReadOnlyDictionary<string, SourceBinding> EmptySources =
        new Dictionary<string, SourceBinding>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> EmptyAliases =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static string? ValidateSelectColumns(
        string sql,
        DatabaseProviderType provider,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        IReadOnlyList<Statement> statements;
        try
        {
            statements = new SqlQueryParser().Parse(
                ParameterPlaceholderRegex().Replace(sql, "NULL"),
                CreateDialect(provider));
        }
        catch (Exception)
        {
            return "SQL statement did not parse for governed column validation.";
        }

        if (statements.Count != 1)
        {
            return "Multiple SQL statements are not allowed for governed column validation.";
        }

        if (statements[0] is not Statement.Select selectStatement)
        {
            return "Only SELECT statements are allowed for governed column validation.";
        }

        return ValidateQuery(
            selectStatement.Query,
            new ColumnScope(EmptySources, null, EmptyAliases),
            allowedTables,
            allowedColumns,
            out _);
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

    private static string? ValidateQuery(
        Query query,
        ColumnScope outerScope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns,
        out IReadOnlySet<string> projectionNames)
    {
        projectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cteSources = new Dictionary<string, SourceBinding>(StringComparer.OrdinalIgnoreCase);
        if (query.With is not null)
        {
            if (query.With.Recursive)
            {
                return "Recursive CTE is not supported for governed column validation.";
            }

            foreach (var cte in query.With.CteTables)
            {
                var cteError = ValidateQuery(
                    cte.Query,
                    new ColumnScope(cteSources, outerScope, EmptyAliases),
                    allowedTables,
                    allowedColumns,
                    out var cteProjectionNames);
                if (cteError is not null)
                {
                    return cteError;
                }

                var cteColumns = ResolveAliasColumns(cte.Alias, cteProjectionNames);
                if (cteColumns.Count == 0)
                {
                    return $"CTE '{cte.Alias.Name.Value}' must expose named columns for governed column validation.";
                }

                cteSources[cte.Alias.Name.Value] = new SourceBinding(
                    cte.Alias.Name.Value,
                    cteColumns,
                    IsDerived: true);
            }
        }

        return ValidateSetExpression(
            query.Body,
            query.OrderBy,
            new ColumnScope(cteSources, outerScope, EmptyAliases),
            allowedTables,
            allowedColumns,
            out projectionNames);
    }

    private static string? ValidateSetExpression(
        SetExpression expression,
        OrderBy? orderBy,
        ColumnScope outerScope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns,
        out IReadOnlySet<string> projectionNames)
    {
        projectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return expression switch
        {
            SetExpression.SelectExpression selectExpression => ValidateSelect(
                selectExpression.Select,
                orderBy,
                outerScope,
                allowedTables,
                allowedColumns,
                out projectionNames),
            SetExpression.QueryExpression queryExpression => ValidateQuery(
                queryExpression.Query,
                outerScope,
                allowedTables,
                allowedColumns,
                out projectionNames),
            _ => "Set operations, VALUES, and TABLE expressions are not supported for governed column validation."
        };
    }

    private static string? ValidateSelect(
        Select select,
        OrderBy? orderBy,
        ColumnScope outerScope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns,
        out IReadOnlySet<string> projectionNames)
    {
        var localSources = new Dictionary<string, SourceBinding>(StringComparer.OrdinalIgnoreCase);
        projectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (select.From is not null)
        {
            foreach (var source in select.From)
            {
                var sourceError = AddTableWithJoins(
                    source,
                    localSources,
                    outerScope,
                    allowedTables,
                    allowedColumns);
                if (sourceError is not null)
                {
                    return sourceError;
                }
            }
        }

        var sourceScope = new ColumnScope(localSources, outerScope, EmptyAliases);
        var collectedProjectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in select.Projection)
        {
            var itemError = ValidateSelectItem(
                item,
                sourceScope,
                collectedProjectionNames,
                allowedTables,
                allowedColumns);
            if (itemError is not null)
            {
                return itemError;
            }
        }

        projectionNames = collectedProjectionNames;
        var expressionScope = new ColumnScope(localSources, outerScope, collectedProjectionNames);
        foreach (var expression in EnumerateNullable(select.PreWhere, select.Selection, select.Having, select.QualifyBy))
        {
            var expressionError = ValidateExpression(expression, expressionScope, allowedTables, allowedColumns);
            if (expressionError is not null)
            {
                return expressionError;
            }
        }

        if (select.GroupBy is GroupByExpression.Expressions groupBy)
        {
            foreach (var expression in groupBy.ColumnNames)
            {
                var groupError = ValidateExpression(expression, expressionScope, allowedTables, allowedColumns);
                if (groupError is not null)
                {
                    return groupError;
                }
            }
        }
        else if (select.GroupBy is not null and not GroupByExpression.All)
        {
            return "Unsupported GROUP BY expression for governed column validation.";
        }

        if (orderBy?.Expressions is not null)
        {
            foreach (var orderByExpression in orderBy.Expressions)
            {
                var orderError = ValidateExpression(
                    orderByExpression.Expression,
                    expressionScope,
                    allowedTables,
                    allowedColumns);
                if (orderError is not null)
                {
                    return orderError;
                }
            }
        }

        return null;
    }

    private static string? AddTableWithJoins(
        TableWithJoins tableWithJoins,
        Dictionary<string, SourceBinding> localSources,
        ColumnScope outerScope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        if (tableWithJoins.Relation is null)
        {
            return "FROM relation is missing for governed column validation.";
        }

        var relationError = AddTableFactor(
            tableWithJoins.Relation,
            localSources,
            outerScope,
            allowedTables,
            allowedColumns);
        if (relationError is not null)
        {
            return relationError;
        }

        if (tableWithJoins.Joins is null)
        {
            return null;
        }

        foreach (var join in tableWithJoins.Joins)
        {
            if (join.Relation is null)
            {
                return "JOIN relation is missing for governed column validation.";
            }

            var joinSourceError = AddTableFactor(
                join.Relation,
                localSources,
                outerScope,
                allowedTables,
                allowedColumns);
            if (joinSourceError is not null)
            {
                return joinSourceError;
            }

            if (join.JoinOperator is null)
            {
                return "JOIN operator is missing for governed column validation.";
            }

            var joinError = ValidateJoinOperator(
                join.JoinOperator,
                new ColumnScope(localSources, outerScope, EmptyAliases),
                allowedTables,
                allowedColumns);
            if (joinError is not null)
            {
                return joinError;
            }
        }

        return null;
    }

    private static string? AddTableFactor(
        TableFactor tableFactor,
        Dictionary<string, SourceBinding> localSources,
        ColumnScope outerScope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        switch (tableFactor)
        {
            case TableFactor.Table table when table.Args is null && !table.WithOrdinality:
            {
                var tableName = LastIdentifier(table.Name);
                if (outerScope.TryGetSource(tableName, out var cteBinding) && cteBinding.IsDerived)
                {
                    AddSource(localSources, ResolveAlias(table.Alias, tableName), cteBinding);
                    return null;
                }

                if (!allowedTables.Contains(tableName))
                {
                    return $"Table '{tableName}' is not allowed for governed column validation.";
                }

                if (!allowedColumns.TryGetValue(tableName, out var columns))
                {
                    return $"Table '{tableName}' does not have governed column metadata.";
                }

                if (table.Alias?.Columns?.Any() == true)
                {
                    return $"Column-renaming alias for table '{tableName}' is not supported for governed column validation.";
                }

                AddSource(
                    localSources,
                    ResolveAlias(table.Alias, tableName),
                    new SourceBinding(tableName, columns, IsDerived: false));
                return null;
            }
            case TableFactor.Derived derived when derived.SubQuery is not null:
            {
                if (derived.Alias is null)
                {
                    return "Derived subqueries must have an alias for governed column validation.";
                }

                var subqueryError = ValidateQuery(
                    derived.SubQuery,
                    new ColumnScope(localSources, outerScope, EmptyAliases),
                    allowedTables,
                    allowedColumns,
                    out var projectionNames);
                if (subqueryError is not null)
                {
                    return subqueryError;
                }

                var derivedColumns = ResolveAliasColumns(derived.Alias, projectionNames);
                if (derivedColumns.Count == 0)
                {
                    return $"Derived table '{derived.Alias.Name.Value}' must expose named columns for governed column validation.";
                }

                AddSource(
                    localSources,
                    derived.Alias.Name.Value,
                    new SourceBinding(derived.Alias.Name.Value, derivedColumns, IsDerived: true));
                return null;
            }
            case TableFactor.NestedJoin nested:
                if (nested.TableWithJoins is null)
                {
                    return "Nested JOIN structure is missing for governed column validation.";
                }

                return nested.Alias is null
                    ? AddTableWithJoins(nested.TableWithJoins, localSources, outerScope, allowedTables, allowedColumns)
                    : "Aliased nested joins are not supported for governed column validation.";
            default:
                return "Unsupported FROM structure for governed column validation.";
        }
    }

    private static string? ValidateJoinOperator(
        JoinOperator joinOperator,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        return joinOperator switch
        {
            JoinOperator.Inner inner => ValidateJoinConstraint(inner.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.LeftOuter leftOuter => ValidateJoinConstraint(leftOuter.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.RightOuter rightOuter => ValidateJoinConstraint(rightOuter.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.FullOuter fullOuter => ValidateJoinConstraint(fullOuter.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.LeftSemi leftSemi => ValidateJoinConstraint(leftSemi.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.RightSemi rightSemi => ValidateJoinConstraint(rightSemi.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.LeftAnti leftAnti => ValidateJoinConstraint(leftAnti.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.RightAnti rightAnti => ValidateJoinConstraint(rightAnti.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.AsOf asOf => ValidateExpression(asOf.MatchCondition, scope, allowedTables, allowedColumns) ??
                                      ValidateJoinConstraint(asOf.Constraint, scope, allowedTables, allowedColumns),
            JoinOperator.ConstrainedJoinOperator constrained => ValidateJoinConstraint(constrained.JoinConstraint, scope, allowedTables, allowedColumns),
            JoinOperator.CrossJoin or JoinOperator.CrossApply or JoinOperator.OuterApply => null,
            _ => "Unsupported JOIN operator for governed column validation."
        };
    }

    private static string? ValidateJoinConstraint(
        JoinConstraint constraint,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        return constraint switch
        {
            JoinConstraint.On on => ValidateExpression(on.Expression, scope, allowedTables, allowedColumns),
            JoinConstraint.Using usingColumns => usingColumns.Idents
                .Select(ident => ValidateIdentifier(ident.Value, scope))
                .FirstOrDefault(error => error is not null),
            JoinConstraint.Natural => "NATURAL JOIN is not supported for governed column validation.",
            JoinConstraint.None => null,
            _ => "Unsupported JOIN constraint for governed column validation."
        };
    }

    private static string? ValidateSelectItem(
        SelectItem item,
        ColumnScope scope,
        HashSet<string> projectionNames,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        switch (item)
        {
            case SelectItem.UnnamedExpression unnamed:
            {
                var error = ValidateExpression(unnamed.Expression, scope, allowedTables, allowedColumns);
                if (error is not null)
                {
                    return error;
                }

                var projectionName = ResolveProjectionName(unnamed.Expression);
                if (projectionName is not null)
                {
                    projectionNames.Add(projectionName);
                }

                return null;
            }
            case SelectItem.ExpressionWithAlias aliased:
            {
                var error = ValidateExpression(aliased.Expression, scope, allowedTables, allowedColumns);
                if (error is not null)
                {
                    return error;
                }

                projectionNames.Add(aliased.Alias.Value);
                return null;
            }
            case SelectItem.Wildcard:
            case SelectItem.QualifiedWildcard:
                return "Wildcard SELECT projections are not allowed for governed column validation.";
            default:
                return "Unsupported SELECT projection for governed column validation.";
        }
    }

    private static string? ValidateExpression(
        Expression? expression,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        if (expression is null)
        {
            return null;
        }

        return expression switch
        {
            Expression.Identifier identifier => ValidateIdentifier(identifier.Ident.Value, scope),
            Expression.CompoundIdentifier compound => ValidateCompoundIdentifier(compound.Idents.Select(ident => ident.Value).ToArray(), scope),
            Expression.LiteralValue => null,
            Expression.BinaryOp binary => ValidateExpression(binary.Left, scope, allowedTables, allowedColumns) ??
                                          ValidateExpression(binary.Right, scope, allowedTables, allowedColumns),
            Expression.UnaryOp unary => ValidateExpression(unary.Expression, scope, allowedTables, allowedColumns),
            Expression.Nested nested => ValidateExpression(nested.Expression, scope, allowedTables, allowedColumns),
            Expression.Between between => ValidateExpression(between.Expression, scope, allowedTables, allowedColumns) ??
                                          ValidateExpression(between.Low, scope, allowedTables, allowedColumns) ??
                                          ValidateExpression(between.High, scope, allowedTables, allowedColumns),
            Expression.Like like => ValidateExpression(like.Expression, scope, allowedTables, allowedColumns) ??
                                    ValidateExpression(like.Pattern, scope, allowedTables, allowedColumns),
            Expression.ILike like => ValidateExpression(like.Expression, scope, allowedTables, allowedColumns) ??
                                     ValidateExpression(like.Pattern, scope, allowedTables, allowedColumns),
            Expression.InList inList => ValidateExpression(inList.Expression, scope, allowedTables, allowedColumns) ??
                                        ValidateExpressions(inList.List, scope, allowedTables, allowedColumns),
            Expression.InSubquery inSubquery => ValidateExpression(inSubquery.Expression, scope, allowedTables, allowedColumns) ??
                                                ValidateQuery(inSubquery.SubQuery, scope, allowedTables, allowedColumns, out _),
            Expression.Exists exists => ValidateQuery(exists.SubQuery, scope, allowedTables, allowedColumns, out _),
            Expression.Subquery subquery => ValidateQuery(subquery.Query, scope, allowedTables, allowedColumns, out _),
            Expression.Case caseExpression => ValidateExpression(caseExpression.Operand, scope, allowedTables, allowedColumns) ??
                                               ValidateExpressions(caseExpression.Conditions, scope, allowedTables, allowedColumns) ??
                                               ValidateExpressions(caseExpression.Results, scope, allowedTables, allowedColumns) ??
                                               ValidateExpression(caseExpression.ElseResult, scope, allowedTables, allowedColumns),
            Expression.Cast cast => ValidateExpression(cast.Expression, scope, allowedTables, allowedColumns),
            Expression.Collate collate => ValidateExpression(collate.Expression, scope, allowedTables, allowedColumns),
            Expression.Extract extract => ValidateExpression(extract.Expression, scope, allowedTables, allowedColumns),
            Expression.Substring substring => ValidateExpression(substring.Expression, scope, allowedTables, allowedColumns) ??
                                               ValidateExpression(substring.SubstringFrom, scope, allowedTables, allowedColumns) ??
                                               ValidateExpression(substring.SubstringFor, scope, allowedTables, allowedColumns),
            Expression.Position position => ValidateExpression(position.Expression, scope, allowedTables, allowedColumns) ??
                                             ValidateExpression(position.In, scope, allowedTables, allowedColumns),
            Expression.Tuple tuple => ValidateExpressions(tuple.Expressions, scope, allowedTables, allowedColumns),
            Expression.Function function => ValidateFunction(function, scope, allowedTables, allowedColumns),
            Expression.IsNull isNull => ValidateExpression(isNull.Expression, scope, allowedTables, allowedColumns),
            Expression.IsNotNull isNotNull => ValidateExpression(isNotNull.Expression, scope, allowedTables, allowedColumns),
            Expression.IsTrue isTrue => ValidateExpression(isTrue.Expression, scope, allowedTables, allowedColumns),
            Expression.IsFalse isFalse => ValidateExpression(isFalse.Expression, scope, allowedTables, allowedColumns),
            Expression.IsNotTrue isNotTrue => ValidateExpression(isNotTrue.Expression, scope, allowedTables, allowedColumns),
            Expression.IsNotFalse isNotFalse => ValidateExpression(isNotFalse.Expression, scope, allowedTables, allowedColumns),
            Expression.IsUnknown isUnknown => ValidateExpression(isUnknown.Expression, scope, allowedTables, allowedColumns),
            Expression.IsNotUnknown isNotUnknown => ValidateExpression(isNotUnknown.Expression, scope, allowedTables, allowedColumns),
            Expression.IsDistinctFrom distinct => ValidateExpression(distinct.Expression1, scope, allowedTables, allowedColumns) ??
                                                  ValidateExpression(distinct.Expression2, scope, allowedTables, allowedColumns),
            Expression.IsNotDistinctFrom distinct => ValidateExpression(distinct.Expression1, scope, allowedTables, allowedColumns) ??
                                                     ValidateExpression(distinct.Expression2, scope, allowedTables, allowedColumns),
            Expression.Wildcard or Expression.QualifiedWildcard => null,
            _ => $"Unsupported expression '{expression.GetType().Name}' for governed column validation."
        };
    }

    private static string? ValidateFunction(
        Expression.Function function,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        var functionName = function.Name.Values.LastOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(functionName) ||
            function.Name.Values.Count != 1 ||
            !AllowedBusinessQueryFunctions.Contains(functionName))
        {
            return "Function is not allowed for governed business query execution.";
        }

        foreach (var arguments in EnumerateNullable(function.Args, function.Parameters))
        {
            var argumentError = ValidateFunctionArguments(arguments, scope, allowedTables, allowedColumns);
            if (argumentError is not null)
            {
                return argumentError;
            }
        }

        var filterError = ValidateExpression(function.Filter, scope, allowedTables, allowedColumns);
        if (filterError is not null)
        {
            return filterError;
        }

        if (function.WithinGroup is not null)
        {
            foreach (var orderByExpression in function.WithinGroup)
            {
                var orderError = ValidateExpression(orderByExpression.Expression, scope, allowedTables, allowedColumns);
                if (orderError is not null)
                {
                    return orderError;
                }
            }
        }

        if (function.Over is WindowType.WindowSpecType windowSpec)
        {
            if (windowSpec.Spec is null)
            {
                return "Window specification is missing for governed column validation.";
            }

            foreach (var partition in windowSpec.Spec.PartitionBy ?? Enumerable.Empty<Expression>())
            {
                var partitionError = ValidateExpression(partition, scope, allowedTables, allowedColumns);
                if (partitionError is not null)
                {
                    return partitionError;
                }
            }

            foreach (var orderByExpression in windowSpec.Spec.OrderBy ?? Enumerable.Empty<OrderByExpression>())
            {
                var orderError = ValidateExpression(orderByExpression.Expression, scope, allowedTables, allowedColumns);
                if (orderError is not null)
                {
                    return orderError;
                }
            }
        }

        return null;
    }

    private static string? ValidateFunctionArguments(
        FunctionArguments arguments,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        return arguments switch
        {
            FunctionArguments.None => null,
            FunctionArguments.Subquery subquery => ValidateQuery(subquery.Query, scope, allowedTables, allowedColumns, out _),
            FunctionArguments.List list => ValidateFunctionArgumentList(list.ArgumentList, scope, allowedTables, allowedColumns),
            _ => "Unsupported function arguments for governed column validation."
        };
    }

    private static string? ValidateFunctionArgumentList(
        FunctionArgumentList argumentList,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        if (argumentList.Args is null)
        {
            return null;
        }

        foreach (var argument in argumentList.Args)
        {
            var argumentError = argument switch
            {
                FunctionArg.Named named => ValidateFunctionArgExpression(named.Arg, scope, allowedTables, allowedColumns),
                FunctionArg.Unnamed unnamed => ValidateFunctionArgExpression(unnamed.FunctionArgExpression, scope, allowedTables, allowedColumns),
                _ => "Unsupported function argument for governed column validation."
            };
            if (argumentError is not null)
            {
                return argumentError;
            }
        }

        if (argumentList.Clauses is null)
        {
            return null;
        }

        foreach (var clause in argumentList.Clauses)
        {
            if (clause is FunctionArgumentClause.OrderBy orderBy)
            {
                foreach (var orderByExpression in orderBy.OrderByExpressions)
                {
                    var orderError = ValidateExpression(orderByExpression.Expression, scope, allowedTables, allowedColumns);
                    if (orderError is not null)
                    {
                        return orderError;
                    }
                }
            }
            else if (clause is FunctionArgumentClause.Limit limit)
            {
                var limitError = ValidateExpression(limit.LimitExpression, scope, allowedTables, allowedColumns);
                if (limitError is not null)
                {
                    return limitError;
                }
            }
        }

        return null;
    }

    private static string? ValidateFunctionArgExpression(
        FunctionArgExpression expression,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        return expression switch
        {
            FunctionArgExpression.FunctionExpression functionExpression => ValidateExpression(functionExpression.Expression, scope, allowedTables, allowedColumns),
            FunctionArgExpression.Wildcard => null,
            FunctionArgExpression.QualifiedWildcard qualifiedWildcard => ValidateCompoundIdentifier(
                qualifiedWildcard.Name.Values.Select(ident => ident.Value).Append("*").ToArray(),
                scope),
            _ => "Unsupported function argument expression for governed column validation."
        };
    }

    private static string? ValidateExpressions(
        IEnumerable<Expression>? expressions,
        ColumnScope scope,
        IReadOnlySet<string> allowedTables,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowedColumns)
    {
        if (expressions is null)
        {
            return null;
        }

        foreach (var expression in expressions)
        {
            var error = ValidateExpression(expression, scope, allowedTables, allowedColumns);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private static string? ValidateIdentifier(string identifier, ColumnScope scope)
    {
        if (scope.OutputAliases.Contains(identifier))
        {
            return null;
        }

        var matchingSources = scope.ResolveSources()
            .Where(source => source.Columns.Contains(identifier))
            .ToArray();
        return matchingSources.Length switch
        {
            0 => $"Column '{identifier}' is not allowed for governed column validation.",
            1 => null,
            _ => $"Column '{identifier}' is ambiguous for governed column validation."
        };
    }

    private static string? ValidateCompoundIdentifier(IReadOnlyList<string> idents, ColumnScope scope)
    {
        if (idents.Count == 0)
        {
            return null;
        }

        if (idents[^1] == "*")
        {
            return idents.Count == 1
                ? null
                : scope.TryGetSource(idents[^2], out _)
                    ? null
                    : $"Source '{idents[^2]}' is not available for governed column validation.";
        }

        if (idents.Count == 1)
        {
            return ValidateIdentifier(idents[0], scope);
        }

        var qualifier = idents[^2];
        var column = idents[^1];
        if (!scope.TryGetSource(qualifier, out var source))
        {
            return $"Source '{qualifier}' is not available for governed column validation.";
        }

        return source.Columns.Contains(column)
            ? null
            : $"Column '{column}' is not allowed for governed source '{qualifier}'.";
    }

    private static IReadOnlySet<string> ResolveAliasColumns(
        TableAlias alias,
        IReadOnlySet<string> fallbackColumns)
    {
        return alias.Columns?.Any() == true
            ? alias.Columns.Select(column => column.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : fallbackColumns;
    }

    private static string ResolveAlias(TableAlias? alias, string tableName)
    {
        return alias?.Name.Value ?? tableName;
    }

    private static void AddSource(
        Dictionary<string, SourceBinding> sources,
        string alias,
        SourceBinding binding)
    {
        sources[alias] = binding;
    }

    private static string LastIdentifier(ObjectName name)
    {
        return name.Values.Last().Value;
    }

    private static string? ResolveProjectionName(Expression expression)
    {
        return expression switch
        {
            Expression.Identifier identifier => identifier.Ident.Value,
            Expression.CompoundIdentifier compound => compound.Idents.Last().Value,
            _ => null
        };
    }

    private static IEnumerable<T> EnumerateNullable<T>(params T?[] items)
        where T : class
    {
        return items.Where(item => item is not null)!;
    }

    private sealed record SourceBinding(
        string SourceName,
        IReadOnlySet<string> Columns,
        bool IsDerived);

    private sealed class ColumnScope(
        IReadOnlyDictionary<string, SourceBinding> sources,
        ColumnScope? outerScope,
        IReadOnlySet<string> outputAliases)
    {
        public IReadOnlySet<string> OutputAliases { get; } = outputAliases;

        public bool TryGetSource(string alias, out SourceBinding binding)
        {
            return sources.TryGetValue(alias, out binding!) ||
                   outerScope?.TryGetSource(alias, out binding!) == true;
        }

        public IEnumerable<SourceBinding> ResolveSources()
        {
            return outerScope is null
                ? sources.Values
                : sources.Values.Concat(outerScope.ResolveSources()).Distinct();
        }
    }

    [GeneratedRegex(@"@[a-z_][a-z0-9_]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParameterPlaceholderRegex();
}
