using System.Globalization;
using System.Text;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReportHtmlRenderer
{
    public static string BuildHtmlReport(AgentReportDocument report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"zh-CN\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine($"  <title>{AgentReportFormatting.EscapeHtml(report.Title)}</title>");
        builder.AppendLine("  <style>body{font-family:Arial,'Microsoft YaHei',sans-serif;margin:32px;color:#1f2937}table{border-collapse:collapse;width:100%;margin:12px 0 24px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6}.muted{color:#6b7280}.metric{display:inline-block;margin:4px 12px 8px 0}</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"<h1>{AgentReportFormatting.EscapeHtml(report.Title)}</h1>");
        builder.AppendLine($"<p>{AgentReportFormatting.EscapeHtml(report.Goal)}</p>");
        builder.AppendLine("<h2>Data Source</h2>");
        builder.AppendLine($"<p>{AgentReportFormatting.EscapeHtml(AgentReportFormatting.BuildSourceMarker(report.CloudReadonlySource))}</p>");
        builder.AppendLine($"<p>{AgentReportFormatting.EscapeHtml(report.CloudReadonlySummary ?? "CloudReadonly was not accessed.")}</p>");
        builder.AppendLine("<h2>Business Query Results</h2>");
        builder.AppendLine(BuildHtmlBusinessQueryResults(report.BusinessQueryResults ?? []));
        builder.AppendLine("<h2>Metrics Summary</h2>");
        foreach (var metric in report.Metrics ?? [])
        {
            builder.AppendLine($"<span class=\"metric\"><strong>{AgentReportFormatting.EscapeHtml(metric.Name)}</strong>: {AgentReportFormatting.EscapeHtml(metric.Value)}{AgentReportFormatting.EscapeHtml(metric.Unit ?? string.Empty)}</span>");
        }

        builder.AppendLine("<h2>Input Files</h2>");
        builder.AppendLine("<ul>");
        foreach (var upload in report.UploadSummaries.DefaultIfEmpty("No upload files."))
        {
            builder.AppendLine($"<li>{AgentReportFormatting.EscapeHtml(upload)}</li>");
        }

        builder.AppendLine("</ul>");
        builder.AppendLine("<h2>Tables</h2>");
        foreach (var table in report.Tables)
        {
            builder.AppendLine($"<h3>{AgentReportFormatting.EscapeHtml(table.Name)}</h3>");
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
            builder.AppendLine($"<li>{AgentReportFormatting.EscapeHtml(source.SourceType)}: {AgentReportFormatting.EscapeHtml(source.Name)} - {AgentReportFormatting.EscapeHtml(source.Detail)}</li>");
        }

        if (report.Sources.Count == 0)
        {
            builder.AppendLine("<li>No knowledge source was retrieved.</li>");
        }

        builder.AppendLine("</ul>");
        builder.AppendLine("<h2>CloudReadonly Boundary</h2>");
        builder.AppendLine($"<p>{AgentReportFormatting.EscapeHtml(report.CloudReadonlySummary ?? "CloudReadonly was not accessed.")}</p>");
        builder.AppendLine("<p class=\"muted\">Draft artifact. Final output still requires approval before finalize.</p>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static string BuildHtmlBusinessQueryResults(
        IReadOnlyList<AgentBusinessQueryResultSummaryDto> businessQueryResults)
    {
        if (businessQueryResults.Count == 0)
        {
            return "<p class=\"muted\">No BusinessDatabase query result.</p>";
        }

        var builder = new StringBuilder();
        builder.AppendLine("<table><thead><tr><th>Data Source</th><th>Source Mode</th><th>Simulation</th><th>Source Label</th><th>Query Hash</th><th>Rows</th><th>Truncated</th><th>Artifact</th></tr></thead><tbody>");
        foreach (var result in businessQueryResults)
        {
            builder.AppendLine(
                $"<tr><td>{AgentReportFormatting.EscapeHtml(result.DataSourceName)}</td><td>{AgentReportFormatting.EscapeHtml(result.SourceMode)}</td><td>{AgentReportFormatting.EscapeHtml(AgentReportFormatting.FormatBool(result.IsSimulation))}</td><td>{AgentReportFormatting.EscapeHtml(result.SourceLabel)}</td><td>{AgentReportFormatting.EscapeHtml(result.QueryHash)}</td><td>{result.RowCount.ToString(CultureInfo.InvariantCulture)}</td><td>{AgentReportFormatting.EscapeHtml(AgentReportFormatting.FormatBool(result.IsTruncated))}</td><td>{AgentReportFormatting.EscapeHtml(result.ArtifactId?.ToString() ?? string.Empty)}</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
        return builder.ToString();
    }

    private static string BuildHtmlTable(AgentReportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr>");
        foreach (var column in table.Columns)
        {
            builder.AppendLine($"<th>{AgentReportFormatting.EscapeHtml(column)}</th>");
        }

        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var row in table.Rows.Take(AgentReportFormatting.HtmlRowLimit))
        {
            builder.AppendLine("<tr>");
            foreach (var column in table.Columns)
            {
                builder.AppendLine($"<td>{AgentReportFormatting.EscapeHtml(row.TryGetValue(column, out var value) ? value : string.Empty)}</td>");
            }

            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");
        return builder.ToString();
    }
}
