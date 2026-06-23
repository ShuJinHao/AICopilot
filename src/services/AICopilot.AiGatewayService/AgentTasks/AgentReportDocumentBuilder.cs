using System.Globalization;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReportDocumentBuilder
{
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
                .Select(item => $"{item.FileName} ({item.FileSize} bytes, sha256={AgentReportFormatting.SafeShaPrefix(item.Sha256)})")
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
            cloudReadonlySource,
            state.BusinessQueryResults
                .Select(item => new AgentBusinessQueryResultSummaryDto(
                    item.DataSourceId,
                    item.DataSourceName,
                    item.SourceMode,
                    item.IsSimulation,
                    item.SourceLabel,
                    item.QueryHash,
                    item.RowCount,
                    item.IsTruncated,
                    item.ArtifactId))
                .ToArray());
    }

    public static AgentReportSourceInfo? BuildCloudReadonlySource(AgentTaskRunState state)
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
            state.CloudReadonlyIsTruncated,
            state.BusinessQueryHash);
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
            .Take(AgentReportFormatting.PreviewRowLimit)
            .Select(row => columns.ToDictionary(
                column => column,
                column => row.TryGetValue(column, out var value) ? AgentReportFormatting.FormatCellValue(value) : string.Empty,
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
            metrics.Add(new("isSimulation", AgentReportFormatting.FormatBool(sourceInfo.IsSimulation), Source: sourceInfo.SourceLabel));
            metrics.Add(new("sourceLabel", sourceInfo.SourceLabel ?? string.Empty, Source: sourceInfo.SourceLabel));
            metrics.Add(new("cloudReadonlyRows", sourceInfo.RowCount.ToString(CultureInfo.InvariantCulture), Source: sourceInfo.SourceLabel));
            metrics.Add(new("cloudReadonlyTruncated", AgentReportFormatting.FormatBool(sourceInfo.IsTruncated), Source: sourceInfo.SourceLabel));
        }

        var numericColumns = state.CloudReadonlyRows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(column => state.CloudReadonlyRows.Any(row =>
                row.TryGetValue(column, out var value) && AgentReportFormatting.TryParseNumber(value, out _)))
            .Take(4);
        foreach (var column in numericColumns)
        {
            var values = state.CloudReadonlyRows
                .Select(row => row.TryGetValue(column, out var value) && AgentReportFormatting.TryParseNumber(value, out var number)
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
}
