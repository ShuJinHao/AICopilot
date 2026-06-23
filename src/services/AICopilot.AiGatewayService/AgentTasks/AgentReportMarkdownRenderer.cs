using System.Globalization;
using System.Text;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReportMarkdownRenderer
{
    public static string BuildMarkdownReport(AgentReportDocument report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {report.Title}");
        builder.AppendLine();
        builder.AppendLine("## Task Goal");
        builder.AppendLine(report.Goal);
        builder.AppendLine();
        builder.AppendLine("## Data Source");
        builder.AppendLine("- " + AgentReportFormatting.BuildSourceMarker(report.CloudReadonlySource));
        builder.AppendLine("- " + (report.CloudReadonlySummary ?? "CloudReadonly was not accessed."));
        builder.AppendLine();
        builder.AppendLine("## Business Query Results");
        builder.AppendLine(BuildMarkdownBusinessQueryResults(report.BusinessQueryResults ?? []));
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
                $"| {AgentReportFormatting.EscapeMarkdownCell(metric.Name)} | {AgentReportFormatting.EscapeMarkdownCell(metric.Value + (metric.Unit ?? string.Empty))} | {AgentReportFormatting.EscapeMarkdownCell(metric.Source ?? string.Empty)} |");
        }

        return builder.ToString();
    }

    private static string BuildMarkdownBusinessQueryResults(
        IReadOnlyList<AgentBusinessQueryResultSummaryDto> businessQueryResults)
    {
        if (businessQueryResults.Count == 0)
        {
            return "- No BusinessDatabase query result.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("| Data Source | Source Mode | Simulation | Source Label | Query Hash | Rows | Truncated | Artifact |");
        builder.AppendLine("| --- | --- | --- | --- | --- | ---: | --- | --- |");
        foreach (var result in businessQueryResults)
        {
            builder.AppendLine(
                $"| {AgentReportFormatting.EscapeMarkdownCell(result.DataSourceName)} | {AgentReportFormatting.EscapeMarkdownCell(result.SourceMode)} | {AgentReportFormatting.EscapeMarkdownCell(AgentReportFormatting.FormatBool(result.IsSimulation))} | {AgentReportFormatting.EscapeMarkdownCell(result.SourceLabel)} | {AgentReportFormatting.EscapeMarkdownCell(result.QueryHash)} | {result.RowCount.ToString(CultureInfo.InvariantCulture)} | {AgentReportFormatting.EscapeMarkdownCell(AgentReportFormatting.FormatBool(result.IsTruncated))} | {AgentReportFormatting.EscapeMarkdownCell(result.ArtifactId?.ToString() ?? string.Empty)} |");
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
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(AgentReportFormatting.EscapeMarkdownCell)) + " |");
        builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
        foreach (var row in table.Rows.Take(AgentReportFormatting.PreviewRowLimit))
        {
            builder.AppendLine("| " + string.Join(" | ", table.Columns.Select(column =>
                AgentReportFormatting.EscapeMarkdownCell(row.TryGetValue(column, out var value) ? value : string.Empty))) + " |");
        }

        return builder.ToString();
    }
}
