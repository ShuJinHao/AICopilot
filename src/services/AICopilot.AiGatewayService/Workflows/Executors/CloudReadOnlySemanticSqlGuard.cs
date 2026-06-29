using System.Text.RegularExpressions;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static partial class CloudReadOnlySemanticSqlGuard
{
    private static readonly IReadOnlySet<string> AllowedTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "devices",
            "device_logs",
            "hourly_capacity",
            "pass_station_records"
        };

    private static readonly string[] SensitiveIdentifierFragments =
    [
        "api_key",
        "apikey",
        "bootstrap_secret",
        "connection_string",
        "credential",
        "password",
        "secret",
        "security_stamp",
        "token"
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
        if (ContainsWildcardProjection(lower))
        {
            return "Wildcard SELECT projections are not allowed for Cloud readonly semantic queries.";
        }

        if (SensitiveIdentifierFragments.Any(fragment => lower.Contains(fragment, StringComparison.Ordinal)))
        {
            return "Sensitive Cloud fields are not allowed in semantic queries.";
        }

        var tableNames = ExtractTableNames(lower).ToArray();
        if (tableNames.Length == 0)
        {
            return "Cloud readonly semantic query must reference an allowed Cloud table.";
        }

        var blockedTable = tableNames.FirstOrDefault(table => !AllowedTables.Contains(table));
        return blockedTable is null
            ? null
            : $"Cloud table '{blockedTable}' is not allowed for semantic readonly queries.";
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
            if (string.Equals(value, "lateral", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dot = value.LastIndexOf('.');
            yield return dot >= 0 ? value[(dot + 1)..] : value;
        }
    }

    private static string StripComments(string sql)
    {
        var withoutBlockComments = BlockCommentRegex().Replace(sql, string.Empty);
        return LineCommentRegex().Replace(withoutBlockComments, string.Empty);
    }
}
