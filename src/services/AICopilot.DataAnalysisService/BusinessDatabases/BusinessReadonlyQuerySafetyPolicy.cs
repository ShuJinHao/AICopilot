using System.Text.RegularExpressions;
using AICopilot.Services.CrossCutting.Sql;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessReadonlyQuerySafetyPolicy
{
    private static readonly string[] DangerousSqlVerbs =
    [
        "insert",
        "update",
        "delete",
        "drop",
        "alter",
        "create",
        "truncate",
        "merge",
        "grant",
        "revoke",
        "copy",
        "execute",
        "call"
    ];

    private static readonly string[] SensitiveIdentifierFragments =
    [
        "api_key",
        "apikey",
        "connection_string",
        "credential",
        "password",
        "secret",
        "token"
    ];

    private static readonly string[] SystemCatalogFragments =
    [
        "information_schema.",
        "pg_catalog.",
        "pg_user",
        "pg_shadow",
        "sys.",
        "mysql."
    ];

    public static string? Validate(string sql, BusinessQuerySafetySchema? schema = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "SQL statement cannot be empty.";
        }

        var normalized = StripComments(sql).Trim();
        var lower = normalized.ToLowerInvariant();
        if (!Regex.IsMatch(lower, @"^\s*select\b", RegexOptions.CultureInvariant))
        {
            return "Only SELECT statements are allowed.";
        }

        if (lower.Contains(';', StringComparison.Ordinal))
        {
            return "Multiple SQL statements are not allowed.";
        }

        if (DangerousSqlVerbs.Any(verb => Regex.IsMatch(lower, $@"\b{verb}\b", RegexOptions.CultureInvariant)))
        {
            return "DDL and DML statements are not allowed.";
        }

        if (SystemCatalogFragments.Any(fragment => lower.Contains(fragment, StringComparison.Ordinal)))
        {
            return "System catalog metadata is not allowed in business queries.";
        }

        var blockedFragments = AllSensitiveFragments(schema);
        if (blockedFragments.Any(fragment => lower.Contains(fragment.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            return "Sensitive fields such as passwords, tokens, keys, or connection strings are not allowed.";
        }

        if (schema is not null)
        {
            if (ContainsWildcardProjection(lower))
            {
                return "Wildcard SELECT projections are not allowed in governed business queries.";
            }

            var tableNames = ExtractTableNames(lower).ToArray();
            if (tableNames.Length == 0)
            {
                return "Business query must reference an allowed business table.";
            }

            var blockedTable = tableNames.FirstOrDefault(table => !schema.AllowedTables.Contains(table));
            if (blockedTable is not null)
            {
                return $"Table '{blockedTable}' is not allowed for this data source.";
            }

            if (schema.AllowedColumns is not null)
            {
                var columnError = SqlAllowlistColumnInspector.ValidatePostgreSqlSelectColumns(
                    normalized,
                    schema.AllowedTables,
                    schema.AllowedColumns);
                if (columnError is not null)
                {
                    return columnError;
                }
            }
        }
        else
        {
            return "Governed query safety schema is required.";
        }

        return null;
    }

    public static IReadOnlyList<string> AllSensitiveFragments(BusinessQuerySafetySchema? schema)
    {
        return SensitiveIdentifierFragments
            .Concat((IEnumerable<string>?)schema?.BlockedFieldFragments ?? Array.Empty<string>())
            .Concat((IEnumerable<string>?)schema?.SensitiveColumnFragments ?? Array.Empty<string>())
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsWildcardProjection(string normalizedSql)
    {
        var selectMatch = Regex.Match(
            normalizedSql,
            @"^\s*select\s+(?:all\s+|distinct\s+)?(?<projection>.*?)\bfrom\b",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!selectMatch.Success)
        {
            return false;
        }

        var projection = selectMatch.Groups["projection"].Value;
        return Regex.IsMatch(
            projection,
            @"(^|,)\s*(?:[a-z_][a-z0-9_]*\.)?\*\s*(,|$)",
            RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> ExtractTableNames(string normalizedSql)
    {
        foreach (Match match in Regex.Matches(
                     normalizedSql,
                     @"\b(?:from|join)\s+([a-z_][a-z0-9_]*(?:\.[a-z_][a-z0-9_]*)?)",
                     RegexOptions.CultureInvariant))
        {
            var value = match.Groups[1].Value;
            var dot = value.LastIndexOf('.');
            yield return dot >= 0 ? value[(dot + 1)..] : value;
        }
    }

    private static string StripComments(string sql)
    {
        var withoutBlockComments = Regex.Replace(sql, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"--.*?$", string.Empty, RegexOptions.Multiline);
    }
}
