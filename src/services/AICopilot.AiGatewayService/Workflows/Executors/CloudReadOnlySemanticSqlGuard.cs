using System.Text.RegularExpressions;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Sql;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static partial class CloudReadOnlySemanticSqlGuard
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

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"--.*?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LineCommentRegex();

    public static string? Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "Cloud readonly semantic SQL cannot be empty.";
        }

        var normalized = StripComments(sql).Trim();
        var lower = normalized.ToLowerInvariant();
        if (!Regex.IsMatch(lower, @"^\s*select\b", RegexOptions.CultureInvariant))
        {
            return "Only SELECT statements are allowed for Cloud readonly semantic queries.";
        }

        if (lower.Contains(';', StringComparison.Ordinal))
        {
            return "Multiple SQL statements are not allowed for Cloud readonly semantic queries.";
        }

        if (DangerousSqlVerbs.Any(verb => Regex.IsMatch(lower, $@"\b{Regex.Escape(verb)}\b", RegexOptions.CultureInvariant)))
        {
            return "DDL and DML statements are not allowed for Cloud readonly semantic queries.";
        }

        if (ContainsWildcardProjection(lower))
        {
            return "Wildcard SELECT projections are not allowed for Cloud readonly semantic queries.";
        }

        if (ContainsSystemCatalog(lower))
        {
            return "System catalog metadata is not allowed for Cloud readonly semantic queries.";
        }

        if (CloudReadOnlyGovernedSchema.BlockedFieldFragments.Any(fragment => lower.Contains(fragment, StringComparison.Ordinal)))
        {
            return "Sensitive Cloud fields are not allowed in semantic queries.";
        }

        return SqlAllowlistColumnInspector.ValidatePostgreSqlSelectColumns(
            normalized,
            CloudReadOnlyGovernedSchema.AllowedTables,
            CloudReadOnlyGovernedSchema.AllowedColumns);
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

    private static bool ContainsSystemCatalog(string normalizedSql)
    {
        return normalizedSql.Contains("information_schema.", StringComparison.Ordinal) ||
               normalizedSql.Contains("pg_catalog.", StringComparison.Ordinal) ||
               normalizedSql.Contains("pg_user", StringComparison.Ordinal) ||
               normalizedSql.Contains("pg_shadow", StringComparison.Ordinal) ||
               normalizedSql.Contains("sys.", StringComparison.Ordinal) ||
               normalizedSql.Contains("mysql.", StringComparison.Ordinal);
    }

    private static string StripComments(string sql)
    {
        var withoutBlockComments = BlockCommentRegex().Replace(sql, string.Empty);
        return LineCommentRegex().Replace(withoutBlockComments, string.Empty);
    }
}
