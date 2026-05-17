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

public sealed record RagDocumentUploadBridgeRequest(
    Guid KnowledgeBaseId,
    string FileName,
    Stream Stream,
    string? ContentType = null,
    long? FileSize = null,
    string? Classification = null,
    string? SourceType = null,
    bool IsSanitized = false);

public sealed record RagDocumentUploadBridgeResult(int DocumentId, string Status);

public interface IRagDocumentUploadBridge
{
    Task<RagDocumentUploadBridgeResult> UploadAsync(
        RagDocumentUploadBridgeRequest request,
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

public sealed record AgentReportDocument(
    string Title,
    string Goal,
    IReadOnlyList<string> UploadSummaries,
    IReadOnlyList<AgentReportTable> Tables,
    IReadOnlyList<AgentReportSource> Sources,
    string? CloudReadonlySummary,
    DateTimeOffset GeneratedAt);

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
