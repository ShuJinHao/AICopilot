using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.Services.Contracts;

public sealed record ArtifactWorkspaceStorageInfo(string RootPath, string WorkspaceUrl);

public sealed record ArtifactWorkspaceStorageSettings(
    string RootPath,
    IReadOnlyCollection<string> Folders,
    IReadOnlyCollection<string> AllowedArtifactTypes,
    bool AllowsUserDefinedPath);

public sealed record ArtifactFileWriteResult(string RelativePath, long FileSize, string MimeType);

public sealed record ArtifactFileReadResult(Stream Stream, string FileName, string MimeType, long FileSize);

public sealed record ArtifactWorkspaceFileItem(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long FileSize,
    DateTimeOffset UpdatedAt);

public sealed record ArtifactFileSetWriteRequest(
    string RelativePath,
    byte[] Content,
    string MimeType);

public sealed record ArtifactFileSetPublishedFile(
    string RelativePath,
    long FileSize,
    string MimeType,
    string Sha256);

public sealed record ArtifactFileSetAuthority(
    Guid TaskId,
    Guid? NodeRunId,
    long TaskFencingToken,
    long NodeFencingToken);

public sealed record ArtifactFileSetStage(
    Guid CommitId,
    string WorkspaceCode,
    string OperationKind,
    string StagingReference,
    string PublishedReference,
    string ManifestJson,
    string ManifestDigest,
    IReadOnlyList<ArtifactFileSetPublishedFile> Files,
    DateTimeOffset CreatedAtUtc,
    ArtifactFileSetAuthority Authority);

public sealed record ArtifactFileSetPendingSnapshot(
    IReadOnlyList<ArtifactFileSetStage> Stages,
    bool HasUnreadableEntries);

public interface IArtifactWorkspaceFileSetStore
{
    Task<ArtifactFileSetStage> StageAsync(
        string workspaceCode,
        string operationKind,
        string publishArea,
        IReadOnlyCollection<ArtifactFileSetWriteRequest> files,
        CancellationToken cancellationToken = default,
        ArtifactFileSetAuthority? authority = null);

    Task ConfirmBestEffortAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default);

    Task RollbackBestEffortAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default);

    Task LeavePendingAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyPublishedAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default);

    Task<ArtifactFileSetPendingSnapshot> GetPendingAsync(
        int maximumEntries,
        DateTimeOffset createdBeforeUtc,
        CancellationToken cancellationToken = default);

    Task ConfirmPendingAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default);

    Task RollbackPendingAsync(
        ArtifactFileSetStage stage,
        CancellationToken cancellationToken = default);

    Task MarkPendingAttemptedAsync(
        Guid commitId,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsPendingAsync(
        Guid commitId,
        CancellationToken cancellationToken = default);
}

public static class ArtifactFileSetCommitProtocol
{
    public static async Task<TResult> ExecuteAsync<TResult>(
        this IArtifactWorkspaceFileSetStore storage,
        ArtifactFileSetStage stage,
        Func<CancellationToken, Task<TResult>> persistDatabaseAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(persistDatabaseAsync);

        TResult result;
        try
        {
            result = await persistDatabaseAsync(cancellationToken);
        }
        catch (PersistenceCommitOutcomeUnknownException)
        {
            await storage.LeavePendingAsync(stage, CancellationToken.None);
            throw;
        }
        catch
        {
            await storage.RollbackBestEffortAsync(stage, CancellationToken.None);
            throw;
        }

        await storage.ConfirmBestEffortAsync(stage, CancellationToken.None);
        return result;
    }
}

public interface IArtifactFileSetOperationStore
{
    void AddCompleted(ArtifactFileSetOperation operation);

    void Discard(ArtifactFileSetOperation operation);

    Task<IReadOnlyCollection<ArtifactFileSetOutcomeAuthoritySnapshot>> ListByNodeFenceAsync(
        AgentNodeRunId nodeRunId,
        long taskFencingToken,
        long nodeFencingToken,
        CancellationToken cancellationToken = default);

    Task<ArtifactFileSetOutcomeAuthoritySnapshot?> GetByCommitAsync(
        Guid commitId,
        CancellationToken cancellationToken = default);
}

public sealed record ArtifactFileSetOutcomeAuthoritySnapshot(
    ArtifactFileSetOperation Operation,
    AgentTask Task,
    ArtifactWorkspace Workspace);

public sealed record ArtifactFileSetMaintenanceResult(
    int ConfirmedOperations,
    int RolledBackOperations,
    int FailedOperations,
    int ActiveOperations,
    bool HasUnreadableJournal);

public interface IArtifactFileSetMaintenanceService
{
    Task<ArtifactFileSetMaintenanceResult> RunOnceAsync(
        DateTimeOffset nowUtc,
        TimeSpan reconciliationDelay,
        int batchSize,
        CancellationToken cancellationToken = default);
}

public interface IArtifactWorkspaceFileStore
{
    ArtifactWorkspaceStorageSettings GetSettings();

    Task<ArtifactWorkspaceStorageInfo> CreateWorkspaceAsync(
        string workspaceCode,
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task<ArtifactFileWriteResult> WriteTextAsync(
        string workspaceCode,
        string relativePath,
        string content,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<ArtifactFileWriteResult> WriteBytesAsync(
        string workspaceCode,
        string relativePath,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<ArtifactFileWriteResult> CopyAsync(
        string workspaceCode,
        string sourceRelativePath,
        string targetRelativePath,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<ArtifactFileReadResult?> OpenReadAsync(
        string workspaceCode,
        string relativePath,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ArtifactWorkspaceFileItem>> ListAsync(
        string workspaceCode,
        CancellationToken cancellationToken = default);
}

public sealed record AgentTableFileParseRequest(
    string FileName,
    string ContentType,
    Stream Content);

public sealed record AgentReportTable(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed record AgentReportSource(
    string SourceType,
    string Name,
    string Detail,
    double? Score = null,
    bool IsLowConfidence = false);

public sealed record AgentReportMetric(
    string Name,
    string Value,
    string? Unit = null,
    string? Source = null);

public sealed record AgentReportSourceInfo(
    string? SourceMode,
    bool IsSimulation,
    string? SourceLabel,
    string? SourcePath,
    int RowCount,
    bool IsTruncated,
    string? QueryHash = null);

public sealed record AgentBusinessQueryResultSummaryDto(
    Guid DataSourceId,
    string DataSourceName,
    string SourceMode,
    bool IsSimulation,
    string SourceLabel,
    string QueryHash,
    int RowCount,
    bool IsTruncated,
    Guid? ArtifactId = null);

public sealed record AgentReportDocument(
    string Title,
    string Goal,
    IReadOnlyList<string> UploadSummaries,
    IReadOnlyList<AgentReportTable> Tables,
    IReadOnlyList<AgentReportSource> Sources,
    string? CloudReadonlySummary,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AgentReportMetric>? Metrics = null,
    AgentReportSourceInfo? CloudReadonlySource = null,
    IReadOnlyList<AgentBusinessQueryResultSummaryDto>? BusinessQueryResults = null);

public interface IAgentTableFileParser
{
    Task<AgentReportTable?> ParseAsync(
        AgentTableFileParseRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAgentArtifactDocumentGenerator
{
    Task<byte[]> GeneratePdfAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default);

    Task<byte[]> GeneratePptxAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default);

    Task<byte[]> GenerateXlsxAsync(
        AgentReportDocument document,
        CancellationToken cancellationToken = default);
}
