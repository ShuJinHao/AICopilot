using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentTaskRunState
{
    public List<AgentUploadSummary> Uploads { get; } = [];

    public List<AgentParsedData> ParsedData { get; } = [];

    public List<AgentReportTable> Tables { get; } = [];

    public List<AgentRagResult> RagResults { get; } = [];

    public string? CloudReadonlySummary { get; set; }

    public IReadOnlyList<Dictionary<string, object?>> CloudReadonlyRows { get; set; } = [];

    public string? CloudReadonlySourceLabel { get; set; }

    public string? CloudReadonlySourcePath { get; set; }

    public string? CloudReadonlySourceMode { get; set; }

    public bool CloudReadonlyIsSimulation { get; set; }

    public int CloudReadonlyRowCount { get; set; }

    public bool CloudReadonlyIsTruncated { get; set; }

    public string? BusinessQueryHash { get; set; }

    public List<AgentBusinessQuerySummary> BusinessQueryResults { get; } = [];

    public List<AgentCloudSandboxQuerySummary> CloudSandboxQueryResults { get; } = [];
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

internal sealed record AgentCloudSandboxQuerySummary(
    string EndpointCode,
    string SourceMode,
    bool IsSandbox,
    string SourceLabel,
    string QueryHash,
    string ResultHash,
    int RowCount,
    bool IsTruncated,
    IReadOnlyCollection<Guid> ArtifactRefs,
    string? TrialMode,
    string? IntentId,
    string? Boundary,
    string? ApprovalStatus);

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
