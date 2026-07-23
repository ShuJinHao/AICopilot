using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentTaskRunState
{
    public List<AgentUploadSummary> Uploads { get; } = [];

    public List<AgentParsedData> ParsedData { get; } = [];

    public List<AgentReportTable> Tables { get; } = [];

    public List<AgentRagResult> RagResults { get; } = [];

    public List<AgentCloudReadonlyQuerySnapshot> CloudReadonlyResults { get; } = [];

    public void MergeCloudReadonlyResults(IEnumerable<AgentCloudReadonlyQuerySnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            CloudReadonlyResults.RemoveAll(existing => existing.HasSameQueryIdentity(snapshot));
            CloudReadonlyResults.Add(snapshot);
        }

        CloudReadonlyResults.Sort(static (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(left.Intent, right.Intent);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.SemanticPlanDigest, right.SemanticPlanDigest);
        });
    }

    public void ApplyCloudReadonlyResult(
        AgentTaskPlanCloudReadonlyIntentDocument intent,
        CloudReadonlyAgentToolResult result)
    {
        CloudReadonlyResults.RemoveAll(snapshot =>
            string.Equals(snapshot.Intent, intent.Intent, StringComparison.Ordinal));
        CloudReadonlyResults.Add(new AgentCloudReadonlyQuerySnapshot(
            intent.Intent, intent.SemanticPlanDigest, result.Summary, result.Rows,
            result.SourceLabel, result.SourceMode, result.IsSimulation, result.RowCount,
            result.IsTruncated, result.QueriedAtUtc));
        CloudReadonlyResults.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Intent, right.Intent));
        (CloudReadonlySummary, CloudReadonlyRows, CloudReadonlySourceLabel, CloudReadonlySourcePath,
            CloudReadonlySourceMode, CloudReadonlyIsSimulation, CloudReadonlyRowCount,
            CloudReadonlyIsTruncated, CloudReadonlyQueriedAtUtc, CloudHealthAssessment) =
            (result.Summary, result.Rows, result.SourceLabel, result.SourcePath,
                result.SourceMode, result.IsSimulation, result.RowCount,
                result.IsTruncated, result.QueriedAtUtc, null);
    }

    public string? CloudReadonlySummary { get; set; }

    public IReadOnlyList<Dictionary<string, object?>> CloudReadonlyRows { get; set; } = [];

    public string? CloudReadonlySourceLabel { get; set; }

    public string? CloudReadonlySourcePath { get; set; }

    public string? CloudReadonlySourceMode { get; set; }

    public bool CloudReadonlyIsSimulation { get; set; }

    public int CloudReadonlyRowCount { get; set; }

    public bool CloudReadonlyIsTruncated { get; set; }

    public DateTimeOffset? CloudReadonlyQueriedAtUtc { get; set; }

    public AgentCloudHealthAssessmentOutput? CloudHealthAssessment { get; set; }

    public AgentReasoningToolOutput? ReasoningOutcome { get; set; }

    public string? ReportEvidenceSetDigest { get; set; }

    public IReadOnlyCollection<string> ReportTruthClasses { get; set; } = [];

    public DateTimeOffset? ReportEvidenceAsOfUtc { get; set; }

    public string? BusinessQueryHash { get; set; }

    public List<AgentBusinessQuerySummary> BusinessQueryResults { get; } = [];
}

internal sealed record AgentBusinessQuerySummary(
    Guid DataSourceId,
    string DataSourceName,
    string SourceMode,
    bool IsSimulation,
    string SourceLabel,
    string QueryHash,
    int RowCount,
    bool IsTruncated,
    Guid? ArtifactId);

internal sealed record AgentCloudReadonlyQuerySnapshot(
    string Intent,
    string SemanticPlanDigest,
    string Summary,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    string SourceLabel,
    string SourceMode,
    bool IsSimulation,
    int RowCount,
    bool IsTruncated,
    DateTimeOffset QueriedAtUtc)
{
    public bool HasSameQueryIdentity(AgentCloudReadonlyQuerySnapshot other) =>
        string.Equals(Intent, other.Intent, StringComparison.Ordinal) &&
        string.Equals(SemanticPlanDigest, other.SemanticPlanDigest, StringComparison.Ordinal);
}

internal sealed record AgentUploadSummary(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSize,
    string Sha256,
    string? StoragePath,
    string? Preview);

internal sealed record AgentParsedData(string FileName, string Format, string Preview);

internal sealed record AgentRagResult(
    Guid KnowledgeBaseId,
    int DocumentId,
    string DocumentName,
    int ChunkIndex,
    double Score,
    bool IsLowConfidence,
    string? LowConfidenceReason,
    string Text);
