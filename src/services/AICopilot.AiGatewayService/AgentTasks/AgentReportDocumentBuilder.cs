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
        tables.AddRange(BuildCloudReadonlyTables(state));
        var cloudReadonlyQueries = BuildCloudReadonlyQueries(state);

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
            BuildCloudReadonlySummary(state),
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
                .ToArray(),
            state.ReportEvidenceSetDigest,
            state.ReportTruthClasses.ToArray(),
            state.ReportEvidenceAsOfUtc,
            BuildHealthAssessment(state.CloudHealthAssessment),
            cloudReadonlyQueries,
            BuildInference(state.ReasoningOutcome));
    }

    public static AgentReportSourceInfo? BuildCloudReadonlySource(AgentTaskRunState state)
    {
        if (state.CloudReadonlyResults.Count != 0)
        {
            var sourceModes = state.CloudReadonlyResults
                .Select(result => result.SourceMode)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var sourceLabels = state.CloudReadonlyResults
                .Select(result => result.SourceLabel)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return new AgentReportSourceInfo(
                sourceModes.Length == 1 ? sourceModes[0] : "MultipleCloudAiRead",
                state.CloudReadonlyResults.Any(result => result.IsSimulation),
                sourceLabels.Length == 1 ? sourceLabels[0] : "Multiple authorized Cloud sources",
                null,
                state.CloudReadonlyResults.Sum(result => result.RowCount),
                state.CloudReadonlyResults.Any(result => result.IsTruncated));
        }

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
            null,
            state.CloudReadonlyRowCount > 0 ? state.CloudReadonlyRowCount : state.CloudReadonlyRows.Count,
            state.CloudReadonlyIsTruncated,
            state.BusinessQueryHash);
    }

    private static IReadOnlyCollection<AgentReportTable> BuildCloudReadonlyTables(AgentTaskRunState state)
    {
        if (state.CloudReadonlyResults.Count != 0)
        {
            return state.CloudReadonlyResults
                .Where(result => result.Rows.Count != 0)
                .OrderBy(result => result.Intent, StringComparer.Ordinal)
                .ThenBy(result => result.SemanticPlanDigest, StringComparer.Ordinal)
                .Select(result => BuildCloudReadonlyTable(
                    $"CloudReadonly:{result.Intent}",
                    result.Rows))
                .ToArray();
        }

        if (state.CloudReadonlyRows.Count == 0)
        {
            return [];
        }

        return [BuildCloudReadonlyTable(
            state.CloudReadonlySourceLabel ?? "CloudReadonly",
            state.CloudReadonlyRows)];
    }

    private static AgentReportTable BuildCloudReadonlyTable(
        string name,
        IReadOnlyList<Dictionary<string, object?>> sourceRows)
    {
        var columns = sourceRows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rows = sourceRows
            .Take(AgentReportFormatting.PreviewRowLimit)
            .Select(row => columns.ToDictionary(
                column => column,
                column => row.TryGetValue(column, out var value) ? AgentReportFormatting.FormatCellValue(value) : string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return new AgentReportTable(name, columns, rows);
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

        foreach (var result in state.CloudReadonlyResults
                     .OrderBy(item => item.Intent, StringComparer.Ordinal)
                     .ThenBy(item => item.SemanticPlanDigest, StringComparer.Ordinal))
        {
            metrics.Add(new(
                $"cloud.{result.Intent}.rows",
                result.RowCount.ToString(CultureInfo.InvariantCulture),
                Source: result.SourceLabel));
            metrics.Add(new(
                $"cloud.{result.Intent}.truncated",
                AgentReportFormatting.FormatBool(result.IsTruncated),
                Source: result.SourceLabel));
            AddNumericAverages(metrics, result.Intent, result.SourceLabel, result.Rows);
        }

        if (state.CloudReadonlyResults.Count == 0)
        {
            AddNumericAverages(
                metrics,
                string.Empty,
                sourceInfo?.SourceLabel,
                state.CloudReadonlyRows);
        }

        return metrics;
    }

    private static void AddNumericAverages(
        ICollection<AgentReportMetric> metrics,
        string intent,
        string? sourceLabel,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var numericColumns = rows
            .SelectMany(row => row.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(column => rows.Any(row =>
                row.TryGetValue(column, out var value) && AgentReportFormatting.TryParseNumber(value, out _)))
            .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
            .Take(4);
        foreach (var column in numericColumns)
        {
            var values = rows
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
                string.IsNullOrEmpty(intent) ? $"avg.{column}" : $"avg.{intent}.{column}",
                values.Average().ToString("0.###", CultureInfo.InvariantCulture),
                Source: sourceLabel));
        }
    }

    private static IReadOnlyList<AgentReportCloudReadonlyQuery> BuildCloudReadonlyQueries(
        AgentTaskRunState state) =>
        state.CloudReadonlyResults
            .OrderBy(result => result.Intent, StringComparer.Ordinal)
            .ThenBy(result => result.SemanticPlanDigest, StringComparer.Ordinal)
            .Select(result => new AgentReportCloudReadonlyQuery(
                result.Intent,
                result.SemanticPlanDigest,
                result.Summary,
                result.SourceMode,
                result.IsSimulation,
                result.SourceLabel,
                result.RowCount,
                result.IsTruncated,
                result.QueriedAtUtc))
            .ToArray();

    private static string? BuildCloudReadonlySummary(AgentTaskRunState state) =>
        state.CloudReadonlyResults.Count == 0
            ? state.CloudReadonlySummary
            : string.Join(
                Environment.NewLine,
                state.CloudReadonlyResults
                    .OrderBy(result => result.Intent, StringComparer.Ordinal)
                    .ThenBy(result => result.SemanticPlanDigest, StringComparer.Ordinal)
                    .Select(result => $"{result.Intent}: {result.Summary}"));

    private static AgentReportHealthAssessment? BuildHealthAssessment(
        AgentCloudHealthAssessmentOutput? assessment) =>
        assessment is null
            ? null
            : new AgentReportHealthAssessment(
                assessment.AlgorithmVersion,
                assessment.TruthClass,
                assessment.HealthScore,
                assessment.HealthLevel,
                assessment.SafeSummary,
                assessment.Findings,
                assessment.Confidence,
                assessment.MissingRate,
                assessment.SourceAsOfUtc,
                assessment.IsSimulation,
                assessment.RowCount,
                assessment.IsTruncated,
                assessment.TypedMetrics);

    private static AgentReportInference? BuildInference(AgentReasoningToolOutput? inference) =>
        inference is null
            ? null
            : new AgentReportInference(
                inference.TruthClass,
                inference.SafeSummary,
                inference.Findings,
                inference.CitationRefs,
                inference.EvidenceWarnings,
                inference.ConflictStatus,
                inference.Confidence);
}
