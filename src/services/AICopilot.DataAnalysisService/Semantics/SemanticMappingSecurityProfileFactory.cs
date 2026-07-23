using System.Text.RegularExpressions;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.Semantics;

internal sealed record GovernedSemanticMappingSource(
    string QualifiedFromClause,
    BusinessQuerySecurityProfile SecurityProfile);

internal static partial class SemanticMappingSecurityProfileFactory
{
    private const string DefaultSchema = "public";

    [GeneratedRegex(
        @"(?:^|\b(?:INNER|LEFT|RIGHT|FULL|CROSS)\s+JOIN\s+|\bJOIN\s+)(?<table>(?:[A-Za-z_][A-Za-z0-9_]*\.)?[A-Za-z_][A-Za-z0-9_]*)(?:\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TableSourceRegex();

    [GeneratedRegex(
        @"\b(?<alias>[A-Za-z_][A-Za-z0-9_]*)\.(?<column>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedColumnRegex();

    public static GovernedSemanticMappingSource Create(SemanticPhysicalMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.Provider != DatabaseProviderType.PostgreSql)
        {
            throw new InvalidOperationException(
                "Governed semantic SQL currently requires PostgreSQL profile enforcement.");
        }

        var rawFromClause = string.IsNullOrWhiteSpace(mapping.FromClause)
            ? $"{mapping.SourceName} t"
            : mapping.FromClause;
        var matches = TableSourceRegex().Matches(rawFromClause);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                "Semantic physical mapping does not contain an explicitly governable table source.");
        }

        var schemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qualifiedFromClause = TableSourceRegex().Replace(rawFromClause, match =>
        {
            var rawTable = match.Groups["table"].Value;
            var parts = rawTable.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2)
            {
                throw new InvalidOperationException(
                    "Semantic physical mapping table names must be schema.table or table.");
            }

            var schema = parts.Length == 2 ? parts[0] : DefaultSchema;
            var table = parts[^1];
            schemas.Add(schema);
            tables.Add(table);
            var alias = match.Groups["alias"].Success
                ? match.Groups["alias"].Value
                : table;
            aliases[alias] = table;

            var prefixLength = match.Groups["table"].Index - match.Index;
            var prefix = match.Value[..prefixLength];
            var suffix = match.Value[(prefixLength + rawTable.Length)..];
            return $"{prefix}{schema}.{table}{suffix}";
        });

        var columns = tables.ToDictionary(
            table => table,
            _ => (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var expression in mapping.FieldMappings.Values.Concat([rawFromClause]))
        {
            foreach (Match columnMatch in QualifiedColumnRegex().Matches(expression))
            {
                if (aliases.TryGetValue(columnMatch.Groups["alias"].Value, out var table))
                {
                    ((HashSet<string>)columns[table]).Add(columnMatch.Groups["column"].Value);
                }
            }
        }

        if (tables.Count == 1)
        {
            var onlyTable = tables.Single();
            foreach (var expression in mapping.FieldMappings.Values)
            {
                if (Regex.IsMatch(
                        expression,
                        @"^[A-Za-z_][A-Za-z0-9_]*$",
                        RegexOptions.CultureInvariant))
                {
                    ((HashSet<string>)columns[onlyTable]).Add(expression);
                }
            }
        }

        var profile = new BusinessQuerySecurityProfile(
            tables,
            columns,
            CloudReadOnlyGovernedSchema.BlockedFieldFragments
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            schemas);
        profile.EnsureComplete();
        return new GovernedSemanticMappingSource(qualifiedFromClause, profile);
    }
}
