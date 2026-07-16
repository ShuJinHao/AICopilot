using System.Globalization;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.Artifacts;

internal static class AgentArtifactDocumentFormatting
{
    public static AgentReportTable BuildSummaryTable(AgentReportDocument document)
    {
        var rows = new List<IReadOnlyDictionary<string, string>>
        {
            SummaryRow("Title", document.Title),
            SummaryRow("Goal", document.Goal),
            SummaryRow("GeneratedAt", document.GeneratedAt.ToString("O", CultureInfo.InvariantCulture)),
            SummaryRow("SourceMarker", BuildSourceMarker(document)),
            SummaryRow("CloudReadonly", document.CloudReadonlySummary ?? string.Empty)
        };

        if (document.CloudReadonlySource is not null)
        {
            rows.Add(SummaryRow("SourceMode", document.CloudReadonlySource.SourceMode ?? string.Empty));
            rows.Add(SummaryRow("IsSimulation", FormatBool(document.CloudReadonlySource.IsSimulation)));
            rows.Add(SummaryRow("SourceLabel", document.CloudReadonlySource.SourceLabel ?? string.Empty));
            rows.Add(SummaryRow("SourcePath", document.CloudReadonlySource.SourcePath ?? string.Empty));
            rows.Add(SummaryRow("RowCount", document.CloudReadonlySource.RowCount.ToString(CultureInfo.InvariantCulture)));
            rows.Add(SummaryRow("Truncated", FormatBool(document.CloudReadonlySource.IsTruncated)));
            rows.Add(SummaryRow("QueryHash", document.CloudReadonlySource.QueryHash ?? string.Empty));
        }

        foreach (var result in document.BusinessQueryResults ?? [])
        {
            rows.Add(SummaryRow(
                $"BusinessQuery:{result.DataSourceName}",
                $"sourceMode={result.SourceMode}; isSimulation={FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; queryHash={result.QueryHash}; rows={result.RowCount}; truncated={FormatBool(result.IsTruncated)}"));
        }

        foreach (var metric in document.Metrics ?? [])
        {
            rows.Add(SummaryRow($"Metric:{metric.Name}", metric.Value + (metric.Unit ?? string.Empty)));
        }

        return new AgentReportTable("Summary", ["Field", "Value"], rows);
    }

    public static AgentReportTable BuildDataTable(AgentReportDocument document)
    {
        if (document.Tables.Count == 0)
        {
            return new AgentReportTable(
                "Data",
                ["Message"],
                [new Dictionary<string, string> { ["Message"] = "No table data." }]);
        }

        var columns = new[] { "Table" }
            .Concat(document.Tables.SelectMany(table => table.Columns).Distinct(StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var table in document.Tables)
        {
            foreach (var sourceRow in table.Rows)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Table"] = table.Name
                };
                foreach (var column in columns.Skip(1))
                {
                    row[column] = sourceRow.TryGetValue(column, out var value) ? value : string.Empty;
                }

                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(new Dictionary<string, string> { ["Table"] = "No table rows" });
        }

        return new AgentReportTable("Data", columns, rows);
    }

    public static AgentReportTable BuildSourcesTable(AgentReportDocument document)
    {
        var columns = new[]
        {
            "SourceType",
            "Name",
            "Detail",
            "Score",
            "IsLowConfidence",
            "SourceMode",
            "IsSimulation",
            "SourceLabel",
            "SourcePath",
            "RowCount",
            "Truncated",
            "QueryHash",
            "Marker"
        };
        var rows = new List<IReadOnlyDictionary<string, string>>();
        if (document.CloudReadonlySource is not null)
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = "CloudReadonly",
                ["Name"] = document.CloudReadonlySource.SourceLabel ?? "CloudReadonly",
                ["Detail"] = document.CloudReadonlySummary ?? string.Empty,
                ["Score"] = string.Empty,
                ["IsLowConfidence"] = "false",
                ["SourceMode"] = document.CloudReadonlySource.SourceMode ?? string.Empty,
                ["IsSimulation"] = FormatBool(document.CloudReadonlySource.IsSimulation),
                ["SourceLabel"] = document.CloudReadonlySource.SourceLabel ?? string.Empty,
                ["SourcePath"] = document.CloudReadonlySource.SourcePath ?? string.Empty,
                ["RowCount"] = document.CloudReadonlySource.RowCount.ToString(CultureInfo.InvariantCulture),
                ["Truncated"] = FormatBool(document.CloudReadonlySource.IsTruncated),
                ["QueryHash"] = document.CloudReadonlySource.QueryHash ?? string.Empty,
                ["Marker"] = BuildSourceMarker(document)
            });
        }

        foreach (var result in document.BusinessQueryResults ?? [])
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = "BusinessDatabase",
                ["Name"] = result.DataSourceName,
                ["Detail"] = $"queryHash={result.QueryHash}",
                ["Score"] = string.Empty,
                ["IsLowConfidence"] = "false",
                ["SourceMode"] = result.SourceMode,
                ["IsSimulation"] = FormatBool(result.IsSimulation),
                ["SourceLabel"] = result.SourceLabel,
                ["SourcePath"] = "BusinessDataSourceCenter/TextToSql",
                ["RowCount"] = result.RowCount.ToString(CultureInfo.InvariantCulture),
                ["Truncated"] = FormatBool(result.IsTruncated),
                ["QueryHash"] = result.QueryHash,
                ["Marker"] = $"sourceMode={result.SourceMode}; isSimulation={FormatBool(result.IsSimulation)}; sourceLabel={result.SourceLabel}; rowCount={result.RowCount}; truncated={FormatBool(result.IsTruncated)}; queryHash={result.QueryHash}"
            });
        }

        foreach (var source in document.Sources)
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = source.SourceType,
                ["Name"] = source.Name,
                ["Detail"] = source.Detail,
                ["Score"] = source.Score?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                ["IsLowConfidence"] = FormatBool(source.IsLowConfidence),
                ["SourceMode"] = string.Empty,
                ["IsSimulation"] = string.Empty,
                ["SourceLabel"] = string.Empty,
                ["SourcePath"] = string.Empty,
                ["RowCount"] = string.Empty,
                ["Truncated"] = string.Empty,
                ["QueryHash"] = string.Empty,
                ["Marker"] = string.Empty
            });
        }

        if (rows.Count == 0)
        {
            rows.Add(new Dictionary<string, string>
            {
                ["SourceType"] = "Local",
                ["Name"] = "Local workspace data",
                ["Detail"] = string.Empty,
                ["Score"] = string.Empty,
                ["IsLowConfidence"] = "false",
                ["SourceMode"] = "Local",
                ["IsSimulation"] = "false",
                ["SourceLabel"] = "Local workspace data",
                ["SourcePath"] = string.Empty,
                ["RowCount"] = "0",
                ["Truncated"] = "false",
                ["QueryHash"] = string.Empty,
                ["Marker"] = BuildSourceMarker(document)
            });
        }

        return new AgentReportTable("Sources", columns, rows);
    }

    public static string BuildSourceMarker(AgentReportDocument document)
    {
        var source = document.CloudReadonlySource;
        if (source is null)
        {
            return "sourceMode=Local; isSimulation=false; sourceLabel=Local workspace data";
        }

        var queryHash = string.IsNullOrWhiteSpace(source.QueryHash) ? string.Empty : $"; queryHash={source.QueryHash}";
        return $"sourceMode={source.SourceMode ?? "Unknown"}; isSimulation={FormatBool(source.IsSimulation)}; sourceLabel={source.SourceLabel ?? string.Empty}; rowCount={source.RowCount}; truncated={FormatBool(source.IsTruncated)}{queryHash}";
    }

    public static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static IReadOnlyDictionary<string, string> SummaryRow(string field, string value)
    {
        return new Dictionary<string, string>
        {
            ["Field"] = field,
            ["Value"] = value
        };
    }
}
