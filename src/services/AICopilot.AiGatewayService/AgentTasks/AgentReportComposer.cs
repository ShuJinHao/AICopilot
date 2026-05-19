using System.Globalization;
using System.Text;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReportComposer
{
    private const int PreviewRowLimit = 20;

    public static AgentReportDocument BuildReportDocument(AgentTask task, AgentTaskRunState state)
    {
        var cloudReadonlySource = BuildCloudReadonlySource(state);
        var tables = state.Tables.ToList();
        var cloudReadonlyTable = BuildCloudReadonlyTable(state);
        if (cloudReadonlyTable is not null)
        {
            tables.Add(cloudReadonlyTable);
        }

        return new AgentReportDocument(
            task.Title,
            task.Goal,
            state.Uploads
                .Select(item => $"{item.FileName} ({item.FileSize} bytes, sha256={SafeShaPrefix(item.Sha256)})")
                .ToArray(),
            tables,
            state.RagResults
                .Select(item => new AgentReportSource(
                    "RAG",
                    item.DocumentName,
                    $"DocumentId={item.DocumentId}, Chunk={item.ChunkIndex}",
                    item.Score,
                    item.IsLowConfidence))
                .ToArray(),
            state.CloudReadonlySummary,
            DateTimeOffset.UtcNow,
            BuildMetrics(state, tables, cloudReadonlySource),
            cloudReadonlySource);
    }

    public static string BuildMarkdownReport(AgentTask task, AgentTaskRunState state)
    {
        var report = BuildReportDocument(task, state);
        var builder = new StringBuilder();
        builder.AppendLine($"# {report.Title}");
        builder.AppendLine();
        builder.AppendLine("## Task Goal");
        builder.AppendLine(report.Goal);
        builder.AppendLine();
        builder.AppendLine("## Data Source");
        builder.AppendLine("- " + BuildSourceMarker(report.CloudReadonlySource));
        builder.AppendLine("- " + (report.CloudReadonlySummary ?? "CloudReadonly was not accessed."));
        builder.AppendLine();
        builder.AppendLine("## Metrics Summary");
        builder.AppendLine(BuildMarkdownMetrics(report.Metrics ?? []));
        builder.AppendLine();
        builder.AppendLine("## Input Files");
        builder.AppendLine(report.UploadSummaries.Count == 0
            ? "- No upload files."
            : string.Join(Environment.NewLine, report.UploadSummaries.Select(item => $"- {item}")));
        builder.AppendLine();
        builder.AppendLine("## Tables");
        if (report.Tables.Count == 0)
        {
            builder.AppendLine("- No table data.");
        }
        else
        {
            foreach (var table in report.Tables)
            {
                builder.AppendLine($"### {table.Name}");
                builder.AppendLine(BuildMarkdownTable(table));
                builder.AppendLine();
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Knowledge Sources");
        builder.AppendLine(report.Sources.Count == 0
            ? "- No knowledge source was retrieved."
            : string.Join(Environment.NewLine, report.Sources.Select(item =>
                $"- {item.SourceType}: {item.Name}; {item.Detail}{(item.Score.HasValue ? $"; score={item.Score:F2}" : string.Empty)}{(item.IsLowConfidence ? "; lowConfidence=true" : string.Empty)}")));
        builder.AppendLine();
        builder.AppendLine("## CloudReadonly Boundary");
        builder.AppendLine(report.CloudReadonlySummary ?? "CloudReadonly was not accessed.");
        builder.AppendLine();
        builder.AppendLine("> Draft artifact. Final output still requires approval before finalize.");
        return builder.ToString();
    }

    public static string BuildHtmlReport(AgentTask task, AgentTaskRunState state)
    {
        var report = BuildReportDocument(task, state);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"zh-CN\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine($"  <title>{EscapeHtml(report.Title)}</title>");
        builder.AppendLine("  <style>body{font-family:Arial,'Microsoft YaHei',sans-serif;margin:32px;color:#1f2937}table{border-collapse:collapse;width:100%;margin:12px 0 24px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6}.muted{color:#6b7280}.metric{display:inline-block;margin:4px 12px 8px 0}</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"<h1>{EscapeHtml(report.Title)}</h1>");
        builder.AppendLine($"<p>{EscapeHtml(report.Goal)}</p>");
        builder.AppendLine("<h2>Data Source</h2>");
        builder.AppendLine($"<p>{EscapeHtml(BuildSourceMarker(report.CloudReadonlySource))}</p>");
        builder.AppendLine($"<p>{EscapeHtml(report.CloudReadonlySummary ?? "CloudReadonly was not accessed.")}</p>");
        builder.AppendLine("<h2>Metrics Summary</h2>");
        foreach (var metric in report.Metrics ?? [])
        {
            builder.AppendLine($"<span class=\"metric\"><strong>{EscapeHtml(metric.Name)}</strong>: {EscapeHtml(metric.Value)}{EscapeHtml(metric.Unit ?? string.Empty)}</span>");
        }

        builder.AppendLine("<h2>Input Files</h2>");
        builder.AppendLine("<ul>");
        foreach (var upload in report.UploadSummaries.DefaultIfEmpty("No upload files."))
        {
            builder.AppendLine($"<li>{EscapeHtml(upload)}</li>");
        }

        builder.AppendLine("</ul>");
        builder.AppendLine("<h2>Tables</h2>");
        foreach (var table in report.Tables)
        {
            builder.AppendLine($"<h3>{EscapeHtml(table.Name)}</h3>");
            builder.AppendLine(BuildHtmlTable(table));
        }

        if (report.Tables.Count == 0)
        {
            builder.AppendLine("<p class=\"muted\">No table data.</p>");
        }

        builder.AppendLine("<h2>Knowledge Sources</h2>");
        builder.AppendLine("<ul>");
        foreach (var source in report.Sources)
        {
            builder.AppendLine($"<li>{EscapeHtml(source.SourceType)}: {EscapeHtml(source.Name)} - {EscapeHtml(source.Detail)}</li>");
        }

        if (report.Sources.Count == 0)
        {
            builder.AppendLine("<li>No knowledge source was retrieved.</li>");
        }

        builder.AppendLine("</ul>");
        builder.AppendLine("<h2>CloudReadonly Boundary</h2>");
        builder.AppendLine($"<p>{EscapeHtml(report.CloudReadonlySummary ?? "CloudReadonly was not accessed.")}</p>");
        builder.AppendLine("<p class=\"muted\">Draft artifact. Final output still requires approval before finalize.</p>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public static object BuildChartPayload(AgentTaskRunState state)
    {
        if (state.CloudReadonlyRows.Count > 0)
        {
            var cloudReadonlyPayload = TryBuildChartPayload(
                state.CloudReadonlySourceLabel ?? "cloud-readonly",
                state.CloudReadonlyRows.Take(PreviewRowLimit).ToArray(),
                BuildCloudReadonlySource(state),
                state.CloudReadonlyRowCount > 0 ? state.CloudReadonlyRowCount : state.CloudReadonlyRows.Count,
                state.CloudReadonlyIsTruncated);
            if (cloudReadonlyPayload is not null)
            {
                return cloudReadonlyPayload;
            }
        }

        var table = state.Tables.FirstOrDefault(table => table.Rows.Count > 0 && table.Columns.Count > 0);
        if (table is not null)
        {
            var rows = table.Rows
                .Take(PreviewRowLimit)
                .Select(row => row.ToDictionary(
                    item => item.Key,
                    item => (object?)item.Value,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var tablePayload = TryBuildChartPayload(table.Name, rows, null, table.Rows.Count, false);
            if (tablePayload is not null)
            {
                return tablePayload;
            }
        }

        var labels = state.ParsedData.Select(item => item.FileName).DefaultIfEmpty("input").ToArray();
        var values = state.ParsedData.Select(item => (double)item.Preview.Length).DefaultIfEmpty(0).ToArray();
        return BuildChartPayloadObject(
            "parsed-preview",
            labels,
            [new ChartSeries("PreviewLength", "previewLength", values)],
            null,
            labels.Length,
            false);
    }

    private static object? TryBuildChartPayload(
        string source,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        AgentReportSourceInfo? sourceInfo,
        int rowCount,
        bool isTruncated)
    {
        var columns = rows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (columns.Length == 0)
        {
            return null;
        }

        var numericColumns = columns
            .Where(column => rows.Any(row =>
                row.TryGetValue(column, out var value) && TryParseNumber(value, out _)))
            .Take(5)
            .ToArray();
        if (numericColumns.Length == 0)
        {
            return null;
        }

        var labelColumn = columns.FirstOrDefault(column =>
            !numericColumns.Contains(column, StringComparer.OrdinalIgnoreCase) &&
            rows.Any(row => row.TryGetValue(column, out var value) && value is not null)) ?? columns[0];
        var labels = rows
            .Select(row => row.TryGetValue(labelColumn, out var value) ? FormatChartLabel(value) : string.Empty)
            .ToArray();
        var series = numericColumns
            .Select(column => new ChartSeries(
                column,
                column,
                rows
                    .Select(row => row.TryGetValue(column, out var value) && TryParseNumber(value, out var number) ? number : 0)
                    .ToArray()))
            .ToArray();

        return BuildChartPayloadObject(source, labels, series, sourceInfo, rowCount, isTruncated);
    }

    private static object BuildChartPayloadObject(
        string source,
        IReadOnlyList<string> labels,
        IReadOnlyList<ChartSeries> series,
        AgentReportSourceInfo? sourceInfo,
        int rowCount,
        bool isTruncated)
    {
        var firstSeries = series.FirstOrDefault();
        return new
        {
            schemaVersion = 2,
            chartType = "bar",
            type = "bar",
            source,
            labels,
            values = firstSeries?.Values ?? Array.Empty<double>(),
            series = series.Select(item => new
            {
                item.Name,
                item.Field,
                item.Values
            }).ToArray(),
            sourceInfo = sourceInfo is null
                ? null
                : new
                {
                    sourceInfo.SourceMode,
                    sourceInfo.IsSimulation,
                    sourceInfo.SourceLabel,
                    sourceInfo.SourcePath,
                    sourceInfo.RowCount,
                    sourceInfo.IsTruncated
                },
            rowCount,
            truncated = isTruncated,
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    private static AgentReportSourceInfo? BuildCloudReadonlySource(AgentTaskRunState state)
    {
        if (string.IsNullOrWhiteSpace(state.CloudReadonlySourceMode) &&
            string.IsNullOrWhiteSpace(state.CloudReadonlySourceLabel) &&
            state.CloudReadonlyRows.Count == 0)
        {
            return null;
        }

        return new AgentReportSourceInfo(
            state.CloudReadonlySourceMode,
            state.CloudReadonlyIsSimulation,
            state.CloudReadonlySourceLabel,
            state.CloudReadonlySourcePath,
            state.CloudReadonlyRowCount > 0 ? state.CloudReadonlyRowCount : state.CloudReadonlyRows.Count,
            state.CloudReadonlyIsTruncated);
    }

    private static AgentReportTable? BuildCloudReadonlyTable(AgentTaskRunState state)
    {
        if (state.CloudReadonlyRows.Count == 0)
        {
            return null;
        }

        var columns = state.CloudReadonlyRows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rows = state.CloudReadonlyRows
            .Take(PreviewRowLimit)
            .Select(row => columns.ToDictionary(
                column => column,
                column => row.TryGetValue(column, out var value) ? FormatCellValue(value) : string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return new AgentReportTable(state.CloudReadonlySourceLabel ?? "CloudReadonly", columns, rows);
    }

    private static IReadOnlyList<AgentReportMetric> BuildMetrics(
        AgentTaskRunState state,
        IReadOnlyList<AgentReportTable> tables,
        AgentReportSourceInfo? sourceInfo)
    {
        var metrics = new List<AgentReportMetric>
        {
            new("tables", tables.Count.ToString(CultureInfo.InvariantCulture)),
            new("ragSources", state.RagResults.Count.ToString(CultureInfo.InvariantCulture))
        };

        if (sourceInfo is not null)
        {
            metrics.Add(new("sourceMode", sourceInfo.SourceMode ?? "Unknown", Source: sourceInfo.SourceLabel));
            metrics.Add(new("isSimulation", FormatBool(sourceInfo.IsSimulation), Source: sourceInfo.SourceLabel));
            metrics.Add(new("sourceLabel", sourceInfo.SourceLabel ?? string.Empty, Source: sourceInfo.SourceLabel));
            metrics.Add(new("cloudReadonlyRows", sourceInfo.RowCount.ToString(CultureInfo.InvariantCulture), Source: sourceInfo.SourceLabel));
            metrics.Add(new("cloudReadonlyTruncated", FormatBool(sourceInfo.IsTruncated), Source: sourceInfo.SourceLabel));
        }

        var numericColumns = state.CloudReadonlyRows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(column => state.CloudReadonlyRows.Any(row =>
                row.TryGetValue(column, out var value) && TryParseNumber(value, out _)))
            .Take(4);
        foreach (var column in numericColumns)
        {
            var values = state.CloudReadonlyRows
                .Select(row => row.TryGetValue(column, out var value) && TryParseNumber(value, out var number)
                    ? (double?)number
                    : null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            if (values.Length == 0)
            {
                continue;
            }

            metrics.Add(new(
                $"avg.{column}",
                values.Average().ToString("0.###", CultureInfo.InvariantCulture),
                Source: sourceInfo?.SourceLabel));
        }

        return metrics;
    }

    private static string BuildMarkdownMetrics(IReadOnlyList<AgentReportMetric> metrics)
    {
        if (metrics.Count == 0)
        {
            return "- No metrics.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("| Metric | Value | Source |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var metric in metrics)
        {
            builder.AppendLine(
                $"| {EscapeMarkdownCell(metric.Name)} | {EscapeMarkdownCell(metric.Value + (metric.Unit ?? string.Empty))} | {EscapeMarkdownCell(metric.Source ?? string.Empty)} |");
        }

        return builder.ToString();
    }

    private static string BuildMarkdownTable(AgentReportTable table)
    {
        if (table.Columns.Count == 0)
        {
            return "_No columns_";
        }

        var builder = new StringBuilder();
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(EscapeMarkdownCell)) + " |");
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
        foreach (var row in table.Rows.Take(PreviewRowLimit))
        {
            builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(column =>
                EscapeMarkdownCell(row.TryGetValue(column, out var value) ? value : string.Empty))) + " |");
        }

        return builder.ToString();
    }

    private static string BuildHtmlTable(AgentReportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr>");
        foreach (var column in table.Columns)
        {
            builder.AppendLine($"<th>{EscapeHtml(column)}</th>");
        }

        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var row in table.Rows.Take(50))
        {
            builder.AppendLine("<tr>");
            foreach (var column in table.Columns)
            {
                builder.AppendLine($"<td>{EscapeHtml(row.TryGetValue(column, out var value) ? value : string.Empty)}</td>");
            }

            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildSourceMarker(AgentReportSourceInfo? sourceInfo)
    {
        if (sourceInfo is null)
        {
            return "sourceMode=Local; isSimulation=false; sourceLabel=Local workspace data";
        }

        return $"sourceMode={sourceInfo.SourceMode ?? "Unknown"}; isSimulation={FormatBool(sourceInfo.IsSimulation)}; sourceLabel={sourceInfo.SourceLabel ?? string.Empty}; rowCount={sourceInfo.RowCount}; truncated={FormatBool(sourceInfo.IsTruncated)}";
    }

    private static bool TryParseNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double doubleValue:
                number = doubleValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                return TryParseNumber(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, out number);
        }
    }

    private static bool TryParseNumber(string value, out double number)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
               || double.TryParse(value, out number);
    }

    private static string FormatCellValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolValue => FormatBool(boolValue),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string FormatChartLabel(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            bool boolValue => FormatBool(boolValue),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string SafeShaPrefix(string sha256)
    {
        return sha256.Length <= 12 ? sha256 : sha256[..12];
    }

    private static string EscapeMarkdownCell(string value)
    {
        return (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }

    private static string EscapeHtml(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private sealed record ChartSeries(string Name, string Field, IReadOnlyList<double> Values);
}
