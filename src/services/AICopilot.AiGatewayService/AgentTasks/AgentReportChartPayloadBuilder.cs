using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentReportChartPayloadBuilder
{
    public static object BuildChartPayload(AgentTaskRunState state)
    {
        foreach (var result in state.CloudReadonlyResults
                     .OrderBy(item => item.Intent, StringComparer.Ordinal)
                     .ThenBy(item => item.SemanticPlanDigest, StringComparer.Ordinal))
        {
            var resultSource = new AgentReportSourceInfo(
                result.SourceMode,
                result.IsSimulation,
                result.SourceLabel,
                null,
                result.RowCount,
                result.IsTruncated);
            var resultPayload = TryBuildChartPayload(
                state,
                result.Intent,
                result.Rows.Take(AgentReportFormatting.PreviewRowLimit).ToArray(),
                resultSource,
                result.RowCount,
                result.IsTruncated);
            if (resultPayload is not null)
            {
                return resultPayload;
            }
        }

        if (state.CloudReadonlyRows.Count > 0)
        {
            var cloudReadonlyPayload = TryBuildChartPayload(
                state,
                state.CloudReadonlySourceLabel ?? "cloud-readonly",
                state.CloudReadonlyRows.Take(AgentReportFormatting.PreviewRowLimit).ToArray(),
                AgentReportDocumentBuilder.BuildCloudReadonlySource(state),
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
                .Take(AgentReportFormatting.PreviewRowLimit)
                .Select(row => row.ToDictionary(
                    item => item.Key,
                    item => (object?)item.Value,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var tablePayload = TryBuildChartPayload(state, table.Name, rows, null, table.Rows.Count, false);
            if (tablePayload is not null)
            {
                return tablePayload;
            }
        }

        var labels = state.ParsedData.Select(item => item.FileName).DefaultIfEmpty("input").ToArray();
        var values = state.ParsedData.Select(item => (double)item.Preview.Length).DefaultIfEmpty(0).ToArray();
        return BuildChartPayloadObject(
            state,
            "parsed-preview",
            labels,
            [new ChartSeries("PreviewLength", "previewLength", values)],
            null,
            labels.Length,
            false);
    }

    private static object? TryBuildChartPayload(
        AgentTaskRunState state,
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
                row.TryGetValue(column, out var value) && AgentReportFormatting.TryParseNumber(value, out _)))
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
            .Select(row => row.TryGetValue(labelColumn, out var value) ? AgentReportFormatting.FormatChartLabel(value) : string.Empty)
            .ToArray();
        var series = numericColumns
            .Select(column => new ChartSeries(
                column,
                column,
                rows
                    .Select(row => row.TryGetValue(column, out var value) && AgentReportFormatting.TryParseNumber(value, out var number) ? number : 0)
                    .ToArray()))
            .ToArray();

        return BuildChartPayloadObject(state, source, labels, series, sourceInfo, rowCount, isTruncated);
    }

    private static object BuildChartPayloadObject(
        AgentTaskRunState state,
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
                    sourceInfo.IsTruncated,
                    sourceInfo.QueryHash
                },
            rowCount,
            truncated = isTruncated,
            evidenceSetDigest = state.ReportEvidenceSetDigest,
            truthClasses = state.ReportTruthClasses,
            evidenceAsOfUtc = state.ReportEvidenceAsOfUtc,
            cloudReadonlyQueries = state.CloudReadonlyResults
                .OrderBy(item => item.Intent, StringComparer.Ordinal)
                .ThenBy(item => item.SemanticPlanDigest, StringComparer.Ordinal)
                .Select(item => new
                {
                    item.Intent,
                    item.SemanticPlanDigest,
                    item.Summary,
                    item.SourceMode,
                    item.IsSimulation,
                    item.SourceLabel,
                    item.RowCount,
                    item.IsTruncated,
                    item.QueriedAtUtc
                })
                .ToArray(),
            aiInference = state.ReasoningOutcome is null
                ? null
                : new
                {
                    state.ReasoningOutcome.TruthClass,
                    state.ReasoningOutcome.SafeSummary,
                    state.ReasoningOutcome.Findings,
                    state.ReasoningOutcome.CitationRefs,
                    state.ReasoningOutcome.EvidenceWarnings,
                    state.ReasoningOutcome.ConflictStatus,
                    state.ReasoningOutcome.Confidence
                },
            currentHealthAssessment = state.CloudHealthAssessment is null
                ? null
                : new
                {
                    state.CloudHealthAssessment.AlgorithmVersion,
                    state.CloudHealthAssessment.TruthClass,
                    state.CloudHealthAssessment.HealthScore,
                    state.CloudHealthAssessment.HealthLevel,
                    state.CloudHealthAssessment.SafeSummary,
                    state.CloudHealthAssessment.Findings,
                    state.CloudHealthAssessment.Confidence,
                    state.CloudHealthAssessment.MissingRate,
                    state.CloudHealthAssessment.SourceAsOfUtc,
                    state.CloudHealthAssessment.IsSimulation,
                    state.CloudHealthAssessment.RowCount,
                    state.CloudHealthAssessment.IsTruncated,
                    state.CloudHealthAssessment.TypedMetrics
                },
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed record ChartSeries(string Name, string Field, IReadOnlyList<double> Values);
}
